using System.Collections.Generic;
using RoR2;
using RoR2.Navigation;
using UnityEngine;

namespace HighCommand.Client
{
    /// <summary>
    /// The ground as the camera sees it, built once per stage. Strategy cameras never ride the real surface: 0 A.D.
    /// reads its heightmap filtered over a radius that grows with the zoom, Spring and Warcraft III follow a smoothed
    /// copy of the terrain, and none of them looks at trees, rocks or buildings. A RoR2 level is a pile of meshes, not
    /// a heightmap, so the mode makes that copy itself:
    ///  * Height: where heroes can walk (the stock ground node graph: its nodes and the links between them), gaps filled
    ///    in from around them, then blurred at six strengths. Read at a strength that grows with the zoom, it is a
    ///    smooth surface of hills and valleys with no trees, rocks, roofs or steps in it.
    ///  * Free height: for the camera's own position, which heights of a column of the level are inside big ground
    ///    (terrain, cliffs), read with rays the first time the camera comes there and kept. Small pieces of the level do
    ///    not count: the camera may pass through them (they are hidden when they cover a hero).
    /// </summary>
    internal static class CameraGround
    {
        internal static bool Valid { get; private set; }

        // ---- height ----------------------------------------------------------------------------------------------

        private const float Cell = 4f;       // m per cell of the height field
        private const float Margin = 100f;   // m of field around the walkable box (the camera may look past its edge)
        private const int MaxCells = 1500000;

        private static readonly int[] BlurRadius = { 0, 1, 2, 4, 8, 16 };     // cells, three box passes each
        private static readonly float[] LevelSigma = new float[BlurRadius.Length];
        private static readonly float[] LogSigma = new float[BlurRadius.Length];
        private static float[][] levels;
        private static int w, h;
        private static float ox, oz;          // world position of the centre of cell (0, 0)
        private static int samples;
        private static float buildMs;

        /// <summary>The finest strength the camera reads the height at (m): close up, about the ground under it.</summary>
        internal const float FineSigma = 4f;

        /// <summary>Forgets the stage (the new one is built when its node graph exists).</summary>
        internal static void Clear()
        {
            Valid = false;
            levels = null;
            Columns.Clear();
        }

        /// <summary>Builds the height field from the ground node graph. <paramref name="box"/> holds all the nodes.</summary>
        internal static void Build(NodeGraph ground, List<NodeGraph.NodeIndex> nodes, Bounds box)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Clear();
            lastGround = ground;
            lastNodes = nodes;
            lastBox = box;
            rayTop = box.max.y + 400f;
            rayBottom = box.min.y - 300f;

            ox = box.min.x - Margin;
            oz = box.min.z - Margin;
            w = Mathf.CeilToInt((box.size.x + 2f * Margin) / Cell) + 1;
            h = Mathf.CeilToInt((box.size.z + 2f * Margin) / Cell) + 1;
            if (w * h > MaxCells)
            {
                Plugin.Log.LogWarning($"CameraGround: map too big for the height field ({w}x{h} cells); the camera reads the ground directly.");
                return;
            }

            var sum = new float[w * h];
            var weight = new float[w * h];
            samples = 0;
            var links = new List<NodeGraph.LinkIndex>(8);
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!ground.GetNodePosition(nodes[i], out Vector3 a)) continue;
                Splat(sum, weight, a, 1f);

                // the ground between linked nodes too, so a path is one continuous strip; a drop or a jump between
                // two heights (steeper than 45 degrees) is not ground and adds nothing
                links.Clear();
                ground.GetActiveNodeLinks(nodes[i], links);
                foreach (NodeGraph.LinkIndex link in links)
                {
                    if (!ground.GetNodePosition(ground.GetLinkEndNode(link), out Vector3 b)) continue;
                    float dx = b.x - a.x, dz = b.z - a.z;
                    float flat = Mathf.Sqrt(dx * dx + dz * dz);
                    if (Mathf.Abs(b.y - a.y) > flat) continue;
                    int steps = Mathf.FloorToInt(flat / 2f);
                    for (int k = 1; k < steps; k++) Splat(sum, weight, Vector3.Lerp(a, b, k / (float)steps), 0.5f);
                }
            }
            if (samples == 0) return;

            float[] filled = Fill(sum, weight, w, h);

            // the walkable ground alone, lightly blurred: what the free camera steps up onto
            walkLevel = (float[])filled.Clone();
            var scratch = new float[w * h];
            for (int pass = 0; pass < 3; pass++) BoxBlur(walkLevel, scratch, w, h, 1);

            // Ground nobody walks on (a mountain, a big mesa) still holds the camera up when the camera is panned over it,
            // as the terrain does in any strategy game: away from the walkable ground the field takes the top of the big
            // ground under the open sky when it is higher (never lower: over a chasm or the void the camera keeps the
            // rims' level), at most RockReach above. Near the walkable ground it does not: there the heroes' own level is
            // looked at (a cliff beside them would lift the view off them), and the camera tilts up over the cliff
            // instead. The change between the two is spread over RockNear..RockFar from the walkable ground.
            int raised = 0;
            if (RaiseByRock)
            {
                float[] away = DistanceToWalkable(weight);
                for (int cz = 0; cz < h; cz++)
                    for (int cx = 0; cx < w; cx++)
                    {
                        int i = cz * w + cx;
                        float share = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(RockNear, RockFar, away[i]));
                        if (share <= 0f) continue;
                        float top = BigTop(ox + cx * Cell, oz + cz * Cell);
                        if (float.IsNaN(top) || top <= filled[i]) continue;
                        filled[i] += (Mathf.Min(top, filled[i] + RockReach) - filled[i]) * share;
                        raised++;
                    }
            }
            rockCells = raised;

            levels = new float[BlurRadius.Length][];
            var temp = new float[w * h];
            for (int k = 0; k < BlurRadius.Length; k++)
            {
                int r = BlurRadius[k];
                var level = (float[])filled.Clone();
                for (int pass = 0; pass < 3 && r > 0; pass++) BoxBlur(level, temp, w, h, r);
                levels[k] = level;
                // three box passes of radius r spread a point like a bell of variance r(r+1) cells^2
                LevelSigma[k] = r > 0 ? Cell * Mathf.Sqrt(r * (r + 1f)) : Cell * 0.5f;
                LogSigma[k] = Mathf.Log(LevelSigma[k]);
            }
            Valid = true;
            buildMs = (float)clock.Elapsed.TotalMilliseconds;
            Plugin.Log.LogInfo($"CameraGround: {w}x{h} cells of {Cell:0} m, {samples} samples from {nodes.Count} nodes, {rockCells} cells raised by big ground, built in {buildMs:0} ms.");
        }

        private static NodeGraph lastGround;
        private static List<NodeGraph.NodeIndex> lastNodes;
        private static Bounds lastBox;

        /// <summary>Testing: builds the field of the current stage again (after a change of RaiseByRock).</summary>
        internal static void Rebuild()
        {
            if (lastGround && lastNodes != null) Build(lastGround, lastNodes, lastBox);
        }

        internal static string Describe() => Valid ? $"{w}x{h} cells, {samples} samples, {rockCells} raised by rock, built in {buildMs:0} ms, {Columns.Count} columns read" : "none";

        /// <summary>Testing (hc_cam rock): big ground nobody walks on lifts the field (rebuilt on the next stage / hc_cam rock).</summary>
        internal static bool RaiseByRock = true;

        private const float WalkWeight = 0.15f;  // a cell with this much walkable ground in it is walkable
        private const float RockReach = 80f;     // m: big ground lifts the field at most this far above the walkable level
        private const float RockNear = 12f, RockFar = 40f; // m from the walkable ground: no lift / the full lift by rock
        private static int rockCells;

        /// <summary>Metres from every cell to the nearest walkable cell (a two-pass chamfer distance).</summary>
        private static float[] DistanceToWalkable(float[] weight)
        {
            const float straight = Cell, diagonal = Cell * 1.41421356f;
            var d = new float[w * h];
            for (int i = 0; i < d.Length; i++) d[i] = weight[i] >= WalkWeight ? 0f : float.MaxValue;
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    int i = z * w + x;
                    float v = d[i];
                    if (x > 0) v = Mathf.Min(v, d[i - 1] + straight);
                    if (z > 0)
                    {
                        v = Mathf.Min(v, d[i - w] + straight);
                        if (x > 0) v = Mathf.Min(v, d[i - w - 1] + diagonal);
                        if (x < w - 1) v = Mathf.Min(v, d[i - w + 1] + diagonal);
                    }
                    d[i] = v;
                }
            for (int z = h - 1; z >= 0; z--)
                for (int x = w - 1; x >= 0; x--)
                {
                    int i = z * w + x;
                    float v = d[i];
                    if (x < w - 1) v = Mathf.Min(v, d[i + 1] + straight);
                    if (z < h - 1)
                    {
                        v = Mathf.Min(v, d[i + w] + straight);
                        if (x < w - 1) v = Mathf.Min(v, d[i + w + 1] + diagonal);
                        if (x > 0) v = Mathf.Min(v, d[i + w - 1] + diagonal);
                    }
                    d[i] = v;
                }
            return d;
        }

        /// <summary>Height of the first big ground met from the sky straight down, NaN when there is none.</summary>
        private static float BigTop(float x, float z)
        {
            var origin = new Vector3(x, rayTop, z);
            float remaining = rayTop - rayBottom;
            int mask = LayerIndex.world.mask;
            for (int n = 0; n < 6 && remaining > 0f; n++)
            {
                if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, remaining, mask, QueryTriggerInteraction.Ignore)) break;
                if (LevelPieces.IsBig(hit.collider)) return hit.point.y;
                float step = hit.distance + 0.05f;
                origin.y -= step;
                remaining -= step;
            }
            return float.NaN;
        }

        /// <summary>
        /// Height of the camera's ground at a point, blurred by <paramref name="sigma"/> metres (the spread of the bell
        /// it is averaged with). NaN when there is no field.
        /// </summary>
        internal static float Height(Vector2 xz, float sigma)
        {
            if (!Valid) return float.NaN;
            float u = (xz.x - ox) / Cell, v = (xz.y - oz) / Cell;
            int last = levels.Length - 1;
            if (sigma <= LevelSigma[0]) return Bilinear(levels[0], w, h, u, v);
            if (sigma >= LevelSigma[last]) return Bilinear(levels[last], w, h, u, v);
            float ls = Mathf.Log(sigma);
            for (int k = 1; k <= last; k++)
            {
                if (sigma > LevelSigma[k]) continue;
                float t = (ls - LogSigma[k - 1]) / (LogSigma[k] - LogSigma[k - 1]);
                return Mathf.Lerp(Bilinear(levels[k - 1], w, h, u, v), Bilinear(levels[k], w, h, u, v), t);
            }
            return Bilinear(levels[last], w, h, u, v);
        }

        private static float[] walkLevel;

        /// <summary>
        /// Height of the walkable ground at a point, lightly blurred (about 6 m), without the lift by rock: where heroes
        /// can stand (or the level of the paths around a gap). NaN when there is no field.
        /// </summary>
        internal static float WalkHeight(Vector2 xz)
        {
            if (!Valid) return float.NaN;
            return Bilinear(walkLevel, w, h, (xz.x - ox) / Cell, (xz.y - oz) / Cell);
        }

        private static void Splat(float[] sum, float[] weight, Vector3 p, float amount)
        {
            float u = (p.x - ox) / Cell, v = (p.z - oz) / Cell;
            int x0 = Mathf.FloorToInt(u), z0 = Mathf.FloorToInt(v);
            float tx = u - x0, tz = v - z0;
            Add(sum, weight, x0, z0, p.y, amount * (1f - tx) * (1f - tz));
            Add(sum, weight, x0 + 1, z0, p.y, amount * tx * (1f - tz));
            Add(sum, weight, x0, z0 + 1, p.y, amount * (1f - tx) * tz);
            Add(sum, weight, x0 + 1, z0 + 1, p.y, amount * tx * tz);
            samples++;
        }

        private static void Add(float[] sum, float[] weight, int x, int z, float y, float amount)
        {
            if (x < 0 || z < 0 || x >= w || z >= h || amount <= 0f) return;
            int i = z * w + x;
            sum[i] += y * amount;
            weight[i] += amount;
        }

        /// <summary>
        /// Fills the cells without ground (a chasm, the void around an island, the margin) from around them: the data
        /// is summed into ever coarser grids, then every grid takes its gaps from the coarser one (push-pull).
        /// </summary>
        private static float[] Fill(float[] sum, float[] weight, int width, int height)
        {
            var sums = new List<float[]> { sum };
            var weights = new List<float[]> { weight };
            var widths = new List<int> { width };
            var heights = new List<int> { height };
            int cw = width, ch = height;
            while (cw > 1 || ch > 1)
            {
                int nw = (cw + 1) / 2, nh = (ch + 1) / 2;
                var ns = new float[nw * nh];
                var nwt = new float[nw * nh];
                float[] ps = sums[sums.Count - 1], pw = weights[weights.Count - 1];
                for (int z = 0; z < ch; z++)
                    for (int x = 0; x < cw; x++)
                    {
                        int j = (z / 2) * nw + x / 2;
                        ns[j] += ps[z * cw + x];
                        nwt[j] += pw[z * cw + x];
                    }
                sums.Add(ns);
                weights.Add(nwt);
                widths.Add(nw);
                heights.Add(nh);
                cw = nw;
                ch = nh;
            }

            int top = sums.Count - 1;
            float[] values = { weights[top][0] > 0f ? sums[top][0] / weights[top][0] : 0f };
            for (int level = top - 1; level >= 0; level--)
            {
                int lw = widths[level], lh = heights[level], uw = widths[level + 1], uh = heights[level + 1];
                float[] s = sums[level], wt = weights[level];
                var v = new float[lw * lh];
                for (int z = 0; z < lh; z++)
                    for (int x = 0; x < lw; x++)
                    {
                        int i = z * lw + x;
                        float up = Bilinear(values, uw, uh, (x + 0.5f) * 0.5f - 0.5f, (z + 0.5f) * 0.5f - 0.5f);
                        v[i] = wt[i] > 0f ? Mathf.Lerp(up, s[i] / wt[i], Mathf.Clamp01(wt[i])) : up;
                    }
                values = v;
            }
            return values;
        }

        /// <summary>Bilinear read of a grid at fractional cell coordinates (cell centres at whole numbers), edges held.</summary>
        private static float Bilinear(float[] grid, int gw, int gh, float u, float v)
        {
            u = Mathf.Clamp(u, 0f, gw - 1);
            v = Mathf.Clamp(v, 0f, gh - 1);
            int x0 = Mathf.Min((int)u, gw - 1), z0 = Mathf.Min((int)v, gh - 1);
            int x1 = Mathf.Min(x0 + 1, gw - 1), z1 = Mathf.Min(z0 + 1, gh - 1);
            float tx = u - x0, tz = v - z0;
            float a = Mathf.Lerp(grid[z0 * gw + x0], grid[z0 * gw + x1], tx);
            float b = Mathf.Lerp(grid[z1 * gw + x0], grid[z1 * gw + x1], tx);
            return Mathf.Lerp(a, b, tz);
        }

        /// <summary>Separable box blur in place (temp is scratch space of the same size), edges held.</summary>
        private static void BoxBlur(float[] data, float[] temp, int width, int height, int r)
        {
            float norm = 1f / (2 * r + 1);
            for (int z = 0; z < height; z++)
            {
                int row = z * width;
                float acc = 0f;
                for (int k = -r; k <= r; k++) acc += data[row + Mathf.Clamp(k, 0, width - 1)];
                for (int x = 0; x < width; x++)
                {
                    temp[row + x] = acc * norm;
                    acc += data[row + Mathf.Min(width - 1, x + r + 1)] - data[row + Mathf.Max(0, x - r)];
                }
            }
            for (int x = 0; x < width; x++)
            {
                float acc = 0f;
                for (int k = -r; k <= r; k++) acc += temp[Mathf.Clamp(k, 0, height - 1) * width + x];
                for (int z = 0; z < height; z++)
                {
                    data[z * width + x] = acc * norm;
                    acc += temp[Mathf.Min(height - 1, z + r + 1) * width + x] - temp[Mathf.Max(0, z - r) * width + x];
                }
            }
        }

        // ---- free height (the camera's own position) -------------------------------------------------------------
        //
        // A column of the level, read with rays from far above (the tops of solid ground: surfaces facing up) and from
        // far below (the undersides: surfaces facing down). A point is inside the ground when the first surface above
        // it is a top; under a cave roof or a bridge the first one is an underside. Only big pieces are read.

        private const float ColumnCell = 2f;
        private const int MaxColumns = 250000;
        private const int MaxLayers = 16;

        /// <summary>m the camera keeps above a top of solid ground.</summary>
        internal const float Clearance = 1.0f;

        private sealed class Column
        {
            internal float[] Tops;
            internal float[] Unders;
        }

        private static readonly Dictionary<long, Column> Columns = new Dictionary<long, Column>(4096);
        private static readonly List<float> Found = new List<float>(MaxLayers);
        private static float rayTop = 2000f, rayBottom = -2000f;

        /// <summary>
        /// The lowest height at or above p.y where a camera at p's spot is out of big ground and at least Clearance above
        /// the top under it: p.y itself almost always.
        /// </summary>
        internal static float FreeHeight(Vector3 p)
        {
            Column c = ColumnAt(p.x, p.z);
            float y = p.y;
            for (int pass = 0; pass < 6; pass++)
            {
                float topAbove = float.PositiveInfinity, underAbove = float.PositiveInfinity, topBelow = float.NegativeInfinity;
                for (int i = 0; i < c.Tops.Length; i++)
                {
                    float t = c.Tops[i];
                    if (t > y) { if (t < topAbove) topAbove = t; }
                    else if (t > topBelow) topBelow = t;
                }
                for (int i = 0; i < c.Unders.Length; i++)
                {
                    float u = c.Unders[i];
                    if (u > y && u < underAbove) underAbove = u;
                }
                if (topAbove < underAbove) { y = topAbove + Clearance; continue; } // inside: up out of it
                if (y - topBelow < Clearance) { y = topBelow + Clearance; continue; } // grazing the top below
                break;
            }
            return y;
        }

        /// <summary>p is inside big ground (the first surface above it is a top); <paramref name="top"/> is that top.</summary>
        internal static bool Inside(Vector3 p, out float top)
        {
            top = float.NaN;
            if (!Valid) return false;
            Column c = ColumnAt(p.x, p.z);
            float topAbove = float.PositiveInfinity, underAbove = float.PositiveInfinity;
            for (int i = 0; i < c.Tops.Length; i++)
                if (c.Tops[i] > p.y && c.Tops[i] < topAbove) topAbove = c.Tops[i];
            for (int i = 0; i < c.Unders.Length; i++)
                if (c.Unders[i] > p.y && c.Unders[i] < underAbove) underAbove = c.Unders[i];
            if (topAbove >= underAbove || float.IsPositiveInfinity(topAbove)) return false;
            top = topAbove;
            return true;
        }

        /// <summary>
        /// The ground at p's spot: the top of the big ground p is inside of, else the highest top below p (the floor
        /// under it), else NaN.
        /// </summary>
        internal static float GroundAt(Vector3 p)
        {
            if (!Valid) return float.NaN;
            if (Inside(p, out float top)) return top;
            Column c = ColumnAt(p.x, p.z);
            float below = float.NaN;
            for (int i = 0; i < c.Tops.Length; i++)
                if (c.Tops[i] <= p.y && (float.IsNaN(below) || c.Tops[i] > below)) below = c.Tops[i];
            return below;
        }

        /// <summary>Testing: the surfaces of the column at p (tops and undersides) and the free height there.</summary>
        internal static string DescribeColumn(Vector3 p)
        {
            Column c = ColumnAt(p.x, p.z);
            var sb = new System.Text.StringBuilder("tops");
            for (int i = 0; i < c.Tops.Length; i++) sb.Append(' ').Append(c.Tops[i].ToString("0.0"));
            sb.Append(" unders");
            for (int i = 0; i < c.Unders.Length; i++) sb.Append(' ').Append(c.Unders[i].ToString("0.0"));
            sb.Append(" free ").Append(FreeHeight(p).ToString("0.0"));
            return sb.ToString();
        }

        private static Column ColumnAt(float x, float z)
        {
            int cx = Mathf.FloorToInt(x / ColumnCell), cz = Mathf.FloorToInt(z / ColumnCell);
            long key = ((long)cx << 32) ^ (uint)cz;
            if (Columns.TryGetValue(key, out Column known)) return known;
            if (Columns.Count >= MaxColumns) Columns.Clear();
            var column = Read((cx + 0.5f) * ColumnCell, (cz + 0.5f) * ColumnCell);
            Columns[key] = column;
            return column;
        }

        private static Column Read(float x, float z)
        {
            return new Column
            {
                Tops = Surfaces(new Vector3(x, rayTop, z), Vector3.down),
                Unders = Surfaces(new Vector3(x, rayBottom, z), Vector3.up)
            };
        }

        /// <summary>Heights of the big-ground surfaces a vertical ray meets facing it (one ray per surface).</summary>
        private static float[] Surfaces(Vector3 origin, Vector3 direction)
        {
            Found.Clear();
            int mask = LayerIndex.world.mask;
            float remaining = rayTop - rayBottom;
            for (int n = 0; n < MaxLayers && remaining > 0f; n++)
            {
                if (!Physics.Raycast(origin, direction, out RaycastHit hit, remaining, mask, QueryTriggerInteraction.Ignore)) break;
                float step = hit.distance + 0.05f;
                origin += direction * step;
                remaining -= step;
                if (LevelPieces.IsBig(hit.collider)) Found.Add(hit.point.y);
            }
            return Found.ToArray();
        }
    }
}
