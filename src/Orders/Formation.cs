using System.Collections.Generic;
using RoR2;
using RoR2.Navigation;
using UnityEngine;

namespace HighCommand.Orders
{
    /// <summary>Spreads a group over a destination so heroes do not stack on one spot, and snaps points to walkable ground.</summary>
    internal static class Formation
    {
        internal const byte Blob = 0, Line = 1, Column = 2;

        /// <summary>
        /// One destination per hero. <paramref name="current"/> are the heroes' present positions (same order as the
        /// result), used for facing and to give each hero the slot nearest to it.
        /// </summary>
        internal static Vector3[] Layout(IList<Vector3> current, Vector3 point, byte kind, float spacing = 2.8f)
        {
            int n = current.Count;
            var result = new Vector3[n];
            if (n == 0) return result;
            if (n == 1)
            {
                result[0] = SnapToGround(point);
                return result;
            }

            Vector3 centroid = Vector3.zero;
            for (int i = 0; i < n; i++) centroid += current[i];
            centroid /= n;

            Vector3 facing = point - centroid;
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.01f) facing = Vector3.forward;
            facing.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, facing);

            var slots = new List<Vector3>(n);
            for (int i = 0; i < n; i++)
            {
                switch (kind)
                {
                    case Line:
                        slots.Add(right * ((i - (n - 1) * 0.5f) * spacing));
                        break;
                    case Column:
                        slots.Add(-facing * (i * spacing) + facing * ((n - 1) * 0.5f * spacing));
                        break;
                    default:
                        slots.Add(BlobSlot(i, spacing));
                        break;
                }
            }

            // each hero takes the free slot nearest to where it stands now
            var used = new bool[n];
            var order = new List<int>(n);
            for (int i = 0; i < n; i++) order.Add(i);
            order.Sort((a, b) => (current[a] - point).sqrMagnitude.CompareTo((current[b] - point).sqrMagnitude));

            foreach (int hero in order)
            {
                int best = -1;
                float bestDist = float.MaxValue;
                for (int s = 0; s < n; s++)
                {
                    if (used[s]) continue;
                    float d = (point + slots[s] - current[hero]).sqrMagnitude;
                    if (d < bestDist) { bestDist = d; best = s; }
                }
                used[best] = true;
                result[hero] = SnapToGround(point + slots[best]);
            }
            return result;
        }

        private static Vector3 BlobSlot(int index, float spacing)
        {
            if (index == 0) return Vector3.zero;
            // rings of 6, 12, 18 ...
            int ring = 1;
            int remaining = index - 1;
            while (remaining >= ring * 6)
            {
                remaining -= ring * 6;
                ring++;
            }
            int inRing = ring * 6;
            float angle = (remaining / (float)inRing) * Mathf.PI * 2f + ring * 0.35f;
            float radius = ring * spacing;
            return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }

        /// <summary>Puts a point on the walkable ground: nearest ground node within 18 m, else straight down.</summary>
        internal static Vector3 SnapToGround(Vector3 p)
        {
            SceneInfo info = SceneInfo.instance;
            if (info && info.groundNodes)
            {
                // the block-grid search can hand back a node that is much farther than the limit: check it
                NodeGraph.NodeIndex node = info.groundNodes.FindClosestNode(p, HullClassification.Human, 18f);
                if (node.nodeIndex != -1 && info.groundNodes.GetNodePosition(node, out Vector3 np)
                    && (np - p).sqrMagnitude <= 24f * 24f)
                    return np;
            }
            // the first drop-pod spawn point is far up in the sky: a long ray finds the ground under it
            if (Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out RaycastHit hit, 2500f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                return hit.point;
            return p;
        }
    }
}
