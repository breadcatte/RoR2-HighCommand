using RoR2;
using RoR2.Navigation;
using UnityEngine;

namespace HighCommand.Orders
{
    /// <summary>
    /// How far a walk really is: along the stage's ground node graph, not as the crow flies (a chest on a ledge right
    /// above a hero can be a long way round). Used once per click, never per frame.
    /// </summary>
    internal static class PathCost
    {
        private const float Unreachable = 100000f;

        /// <summary>Meters along the walkable ground; a rough guess where the stage has no usable node graph.</summary>
        internal static float Walk(Vector3 from, Vector3 to)
        {
            float rough = Rough(from, to);
            SceneInfo info = SceneInfo.instance;
            NodeGraph graph = info ? info.groundNodes : null;
            if (!graph || graph.GetNodeCount() < 20) return rough; // a stub graph (the Bazaar) finds no paths

            using (var path = new Path(graph))
            {
                var request = new NodeGraph.PathRequest
                {
                    startPos = from,
                    endPos = to,
                    hullClassification = HullClassification.Human,
                    maxJumpHeight = 12f,
                    maxSpeed = 8f,
                    path = path
                };
                PathTask task = graph.ComputePath(request);
                if (task == null || !task.wasReachable || path.waypointsCount == 0) return rough + Unreachable;

                float length = 0f;
                Vector3 last = from;
                for (int i = 0; i < path.waypointsCount; i++)
                {
                    if (!graph.GetNodePosition(path[i].nodeIndex, out Vector3 p)) continue;
                    length += Vector3.Distance(last, p);
                    last = p;
                }
                return length + Vector3.Distance(last, to);
            }
        }

        // straight line, climbing counted heavier (a cliff is rarely walked straight up)
        private static float Rough(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float dy = Mathf.Abs(d.y);
            d.y = 0f;
            return d.magnitude + 2.5f * dy;
        }
    }
}
