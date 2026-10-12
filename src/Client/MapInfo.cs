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

            MinimapTexture = BuildTexture(ground, nodes, positions, box);
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

        private static Texture2D BuildTexture(NodeGraph ground, List<NodeGraph.NodeIndex> nodes, List<Vector3> positions, Bounds box)
        {
            int size = TextureSize;
            var pixels = new Color32[size * size];
            Color32 clear = new Color32(10, 14, 20, 200);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

            float sx = (size - 1) / Mathf.Max(0.001f, box.size.x);
            float sz = (size - 1) / Mathf.Max(0.001f, box.size.z);
            float scale = Mathf.Min(sx, sz); // keep aspect: the picture is square, the map is not
            float ox = (size - 1 - box.size.x * scale) * 0.5f;
            float oz = (size - 1 - box.size.z * scale) * 0.5f;
            mmScale = scale;
            mmOffsetX = ox;
            mmOffsetZ = oz;

            Vector2Int ToPixel(Vector3 p)
            {
                return new Vector2Int(
                    Mathf.RoundToInt((p.x - box.min.x) * scale + ox),
                    Mathf.RoundToInt((p.z - box.min.z) * scale + oz));
            }

            // links first (thin lines), then nodes (soft blobs) on top
            var links = new List<NodeGraph.LinkIndex>(8);
            Color32 linkColor = new Color32(70, 110, 150, 255);
            for (int i = 0; i < nodes.Count; i++)
            {
                links.Clear();
                ground.GetActiveNodeLinks(nodes[i], links);
                if (!ground.GetNodePosition(nodes[i], out Vector3 a)) continue;
                Vector2Int pa = ToPixel(a);
                foreach (NodeGraph.LinkIndex link in links)
                {
                    NodeGraph.NodeIndex end = ground.GetLinkEndNode(link);
                    if (!ground.GetNodePosition(end, out Vector3 b)) continue;
                    DrawLine(pixels, size, pa, ToPixel(b), linkColor);
                }
            }

            float minY = box.min.y, maxY = box.max.y;
            foreach (Vector3 p in positions)
            {
                Vector2Int px = ToPixel(p);
                float h = Mathf.InverseLerp(minY, maxY, p.y);
                var c = Color32.Lerp(new Color32(60, 120, 90, 255), new Color32(210, 190, 110, 255), h);
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        if (dx * dx + dy * dy > 5) continue;
                        Put(pixels, size, px.x + dx, px.y + dy, c);
                    }
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "HighCommand.Minimap",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static void Put(Color32[] pixels, int size, int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= size || y >= size) return;
            pixels[y * size + x] = c;
        }

        private static void DrawLine(Color32[] pixels, int size, Vector2Int a, Vector2Int b, Color32 c)
        {
            int dx = Mathf.Abs(b.x - a.x), dy = Mathf.Abs(b.y - a.y);
            int sx = a.x < b.x ? 1 : -1, sy = a.y < b.y ? 1 : -1;
            int err = dx - dy;
            int guard = 4096;
            while (guard-- > 0)
            {
                Put(pixels, size, a.x, a.y, c);
                if (a.x == b.x && a.y == b.y) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; a.x += sx; }
                if (e2 < dx) { err += dx; a.y += sy; }
            }
        }
    }
}
