using System.Collections.Generic;
using RoR2;
using RoR2.Navigation;
using UnityEngine;

namespace HighCommand.Client
{
    /// <summary>
    /// What the mode knows about the current map: its walkable extent (from the stock node graph) and a stylised
    /// top-down picture of the walkable area for the minimap. Rebuilt at the start of every stage.
    /// </summary>
    internal static class MapInfo
    {
        internal static bool Valid { get; private set; }

        /// <summary>World-space box around all walkable ground nodes (padded).</summary>
        internal static Bounds Bounds { get; private set; }

        internal static Texture2D MinimapTexture { get; private set; }

        internal const int TextureSize = 512;

        /// <summary>Builds the map data for the current scene. Safe to call repeatedly; failures leave Valid=false.</summary>
        internal static void Rebuild()
        {
            Valid = false;
            if (MinimapTexture) { Object.Destroy(MinimapTexture); MinimapTexture = null; }

            SceneInfo info = SceneInfo.instance;
            if (!info) return;

            NodeGraph ground = info.groundNodes;
            if (!ground) return;

            var nodes = new List<NodeGraph.NodeIndex>(1024);
            ground.GetActiveNodesForHullMask(HullMask.Human, nodes);
            if (nodes.Count == 0) ground.GetActiveNodesForHullMask(HullMask.Golem | HullMask.Human | HullMask.BeetleQueen, nodes);
            if (nodes.Count == 0) return;

            var positions = new List<Vector3>(nodes.Count);
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (NodeGraph.NodeIndex n in nodes)
            {
                if (!ground.GetNodePosition(n, out Vector3 p)) continue;
                positions.Add(p);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            if (positions.Count == 0) return;

            var box = new Bounds((min + max) * 0.5f, max - min);
            box.Expand(new Vector3(24f, 20f, 24f));
            Bounds = box;

            MinimapTexture = BuildTexture(ground, nodes, min.y, max.y, box);
            Valid = true;

            if (Settings.VerboseLog.Value)
                Plugin.Log.LogInfo($"MapInfo: {positions.Count} ground nodes, bounds center {box.center} size {box.size}");
        }

        // The picture is square and the map is not: one uniform scale, the map centred in the picture.
        private static float mmScale = 1f, mmOffsetX, mmOffsetZ;

        /// <summary>World position -> 0..1 minimap coordinates (x right, y up in the picture; north = +Z).</summary>
        internal static Vector2 WorldToMinimap01(Vector3 world)
        {
            Bounds b = Bounds;
            float span = TextureSize - 1;
            return new Vector2(
                ((world.x - b.min.x) * mmScale + mmOffsetX) / span,
                ((world.z - b.min.z) * mmScale + mmOffsetZ) / span);
        }

        /// <summary>A distance in meters expressed in 0..1 minimap units.</summary>
        internal static float MetersToMinimap01(float meters) => meters * mmScale / (TextureSize - 1);

        /// <summary>0..1 minimap coordinates -> world XZ (Y = box center; callers find the ground).</summary>
        internal static Vector3 Minimap01ToWorld(Vector2 uv)
        {
            Bounds b = Bounds;
            float span = TextureSize - 1;
            return new Vector3(
                b.min.x + (uv.x * span - mmOffsetX) / mmScale,
                b.center.y,
                b.min.z + (uv.y * span - mmOffsetZ) / mmScale);
        }

        // ---- the picture ----------------------------------------------------------------------------------------
        //
        // The walkable area is stamped as thick strokes along the node links and discs at the nodes into a coverage
        // buffer (and a height buffer), both are blurred, and the result is shaded like a small relief map: dark
        // ground, a soft haze around the walkable area, a colour by height, a light edge and a faint grid.

        private static Texture2D BuildTexture(NodeGraph ground, List<NodeGraph.NodeIndex> nodes, float minY, float maxY, Bounds box)
        {
            int size = TextureSize;

            float sx = (size - 1) / Mathf.Max(0.001f, box.size.x);
            float sz = (size - 1) / Mathf.Max(0.001f, box.size.z);
            float scale = Mathf.Min(sx, sz); // keep aspect: the picture is square, the map is not
            float ox = (size - 1 - box.size.x * scale) * 0.5f;
            float oz = (size - 1 - box.size.z * scale) * 0.5f;
            mmScale = scale;
            mmOffsetX = ox;
            mmOffsetZ = oz;

            float heightSpan = Mathf.Max(1f, maxY - minY);
            var cover = new float[size * size];
            var heightSum = new float[size * size];
            var heightWeight = new float[size * size];

            Vector2 ToPixel(Vector3 p) => new Vector2((p.x - box.min.x) * scale + ox, (p.z - box.min.z) * scale + oz);

            float linkHalf = Mathf.Max(2.2f, 3.6f * scale);
            float nodeRadius = Mathf.Max(3f, 5.2f * scale);

            var links = new List<NodeGraph.LinkIndex>(8);
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!ground.GetNodePosition(nodes[i], out Vector3 a)) continue;
                Vector2 pa = ToPixel(a);
                float ha = (a.y - minY) / heightSpan;

                links.Clear();
                ground.GetActiveNodeLinks(nodes[i], links);
                foreach (NodeGraph.LinkIndex link in links)
                {
                    NodeGraph.NodeIndex end = ground.GetLinkEndNode(link);
                    if (!ground.GetNodePosition(end, out Vector3 b)) continue;
                    Stamp(cover, heightSum, heightWeight, size, pa, ToPixel(b), linkHalf, ha, (b.y - minY) / heightSpan);
                }
                Stamp(cover, heightSum, heightWeight, size, pa, pa, nodeRadius, ha, ha);
            }

            var temp = new float[size * size];
            BoxBlur(cover, temp, size, 2);
            BoxBlur(cover, temp, size, 2);
            BoxBlur(heightSum, temp, size, 3);
            BoxBlur(heightWeight, temp, size, 3);

            // height per pixel, then the shaded picture
            var height = new float[size * size];
            for (int i = 0; i < height.Length; i++)
                height[i] = heightWeight[i] > 0.0005f ? Mathf.Clamp01(heightSum[i] / heightWeight[i]) : 0f;

            var pixels = new Color32[size * size];
            Vector3 deep = new Vector3(0.15f, 0.36f, 0.40f);
            Vector3 mid = new Vector3(0.36f, 0.56f, 0.42f);
            Vector3 high = new Vector3(0.85f, 0.75f, 0.47f);
            Vector3 ground0 = new Vector3(0.028f, 0.040f, 0.060f);
            Vector3 ground1 = new Vector3(0.060f, 0.085f, 0.115f);
            Vector3 haze = new Vector3(0.16f, 0.30f, 0.42f);
            Vector3 rim = new Vector3(0.62f, 0.86f, 0.92f);

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int i = y * size + x;
                    float c = cover[i];

                    // background: darker towards the corners, with a faint grid
                    float nx = x / (float)(size - 1) * 2f - 1f, ny = y / (float)(size - 1) * 2f - 1f;
                    float vignette = Mathf.Clamp01(1f - (nx * nx + ny * ny) * 0.5f);
                    Vector3 col = Vector3.Lerp(ground0, ground1, vignette);
                    if (x % 64 == 0 || y % 64 == 0) col += new Vector3(0.025f, 0.035f, 0.05f);

                    float hazeAmount = Mathf.SmoothStep(0f, 0.45f, c) * 0.55f;
                    col = Vector3.Lerp(col, haze * 0.55f, hazeAmount);

                    float inside = Mathf.SmoothStep(0.30f, 0.58f, c);
                    if (inside > 0f)
                    {
                        float h = height[i];
                        Vector3 land = h < 0.5f ? Vector3.Lerp(deep, mid, h * 2f) : Vector3.Lerp(mid, high, (h - 0.5f) * 2f);

                        // a little relief: light from the upper left
                        int xl = Mathf.Max(0, x - 2), xr = Mathf.Min(size - 1, x + 2);
                        int yd = Mathf.Max(0, y - 2), yu = Mathf.Min(size - 1, y + 2);
                        float slope = (height[y * size + xl] - height[y * size + xr]) + (height[yu * size + x] - height[yd * size + x]);
                        float shade = Mathf.Clamp(slope * 3.2f, -0.22f, 0.22f);
                        land *= 1.0f + shade;

                        // light edge where the coverage fades out
                        float edge = Mathf.Clamp01(1f - Mathf.Abs(c - 0.46f) / 0.16f);
                        land = Vector3.Lerp(land, rim, edge * 0.55f);
                        col = Vector3.Lerp(col, land, inside);
                    }

                    pixels[i] = new Color32(
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(col.x) * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(col.y) * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(col.z) * 255f),
                        255);
                }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "HighCommand.Minimap",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear
            };
            tex.SetPixels32(pixels);
            tex.Apply(true, false);
            return tex;
        }

        /// <summary>Adds a stroke (a disc when both ends are the same) to the coverage and height buffers.</summary>
        private static void Stamp(float[] cover, float[] heightSum, float[] heightWeight, int size, Vector2 a, Vector2 b,
            float halfWidth, float heightA, float heightB)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - halfWidth - 1f));
            int x1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + halfWidth + 1f));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - halfWidth - 1f));
            int y1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + halfWidth + 1f));

            Vector2 d = b - a;
            float len2 = Mathf.Max(1e-4f, d.sqrMagnitude);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float t = Mathf.Clamp01(((x + 0.5f - a.x) * d.x + (y + 0.5f - a.y) * d.y) / len2);
                    float qx = a.x + d.x * t - (x + 0.5f), qy = a.y + d.y * t - (y + 0.5f);
                    float dist = Mathf.Sqrt(qx * qx + qy * qy);
                    float c = Mathf.Clamp01(halfWidth - dist + 0.5f);
                    if (c <= 0f) continue;
                    int i = y * size + x;
                    if (c > cover[i]) cover[i] = c;
                    heightSum[i] += c * Mathf.Lerp(heightA, heightB, t);
                    heightWeight[i] += c;
                }
        }

        /// <summary>Separable box blur in place (the temp buffer is scratch space of the same size).</summary>
        private static void BoxBlur(float[] data, float[] temp, int size, int radius)
        {
            float norm = 1f / (2 * radius + 1);
            for (int y = 0; y < size; y++)
            {
                int row = y * size;
                float sum = 0f;
                for (int k = -radius; k <= radius; k++) sum += data[row + Mathf.Clamp(k, 0, size - 1)];
                for (int x = 0; x < size; x++)
                {
                    temp[row + x] = sum * norm;
                    sum += data[row + Mathf.Min(size - 1, x + radius + 1)] - data[row + Mathf.Max(0, x - radius)];
                }
            }
            for (int x = 0; x < size; x++)
            {
                float sum = 0f;
                for (int k = -radius; k <= radius; k++) sum += temp[Mathf.Clamp(k, 0, size - 1) * size + x];
                for (int y = 0; y < size; y++)
                {
                    data[y * size + x] = sum * norm;
                    sum += temp[Mathf.Min(size - 1, y + radius + 1) * size + x] - temp[Mathf.Max(0, y - radius) * size + x];
                }
            }
        }
    }
}
