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

        /// <summary>
        /// The way a hero would walk, as points on the ground from <paramref name="from"/> to <paramref name="to"/>
        /// (corners softened). False when the stage has no usable graph or the target cannot be reached: the list then
        /// holds just the two ends.
        /// </summary>
        internal static bool Route(Vector3 from, Vector3 to, HullClassification hull, float maxJump, System.Collections.Generic.List<Vector3> points)
        {
            points.Clear();
            points.Add(from);
            SceneInfo info = SceneInfo.instance;
            NodeGraph graph = info ? info.groundNodes : null;
            bool found = false;
            if (graph && graph.GetNodeCount() >= 20)
            {
                using (var path = new Path(graph))
                {
                    var request = new NodeGraph.PathRequest
                    {
                        startPos = from,
                        endPos = to,
                        hullClassification = hull,
                        maxJumpHeight = Mathf.Max(maxJump, 4f),
                        maxSpeed = 8f,
                        path = path
                    };
                    PathTask task = graph.ComputePath(request);
                    if (task != null && task.wasReachable && path.waypointsCount > 0)
                    {
                        found = true;
                        for (int i = 0; i < path.waypointsCount; i++)
                            if (graph.GetNodePosition(path[i].nodeIndex, out Vector3 p)) points.Add(p);
                    }
                }
            }
            points.Add(to);
            if (!found) return false;

            // the path starts at the node nearest to the hero and ends at the one nearest to the target, which may lie
            // behind the hero or past the target: such nodes would make a hook at the ends. Cut to the closest ones.
            int n0 = points.Count;
            int first = 1;
            for (int i = 2; i < n0 - 1 && i <= 8; i++)
                if ((points[i] - from).sqrMagnitude < (points[first] - from).sqrMagnitude) first = i;
            if (first > 1) points.RemoveRange(1, first - 1);
            n0 = points.Count;
            int lastNode = n0 - 2;
            for (int i = n0 - 3; i >= 1 && i >= n0 - 10; i--)
                if ((points[i] - to).sqrMagnitude < (points[lastNode] - to).sqrMagnitude) lastNode = i;
            if (lastNode < n0 - 2) points.RemoveRange(lastNode + 1, n0 - 2 - lastNode);

            while (points.Count > 3 && (points[2] - points[0]).sqrMagnitude < (points[2] - points[1]).sqrMagnitude)
                points.RemoveAt(1);
            int n = points.Count;
            while (n > 3 && (points[n - 3] - points[n - 1]).sqrMagnitude < (points[n - 3] - points[n - 2]).sqrMagnitude)
            {
                points.RemoveAt(n - 2);
                n--;
            }
            Soften(points);
            Soften(points);
            return true;
        }

        private static readonly System.Collections.Generic.List<Vector3> SoftBuffer = new System.Collections.Generic.List<Vector3>(128);

        // one pass of corner cutting (Chaikin; two passes make the corners round): the ends stay, each inner corner becomes two points a quarter in
        private static void Soften(System.Collections.Generic.List<Vector3> points)
        {
            if (points.Count < 3) return;
            SoftBuffer.Clear();
            SoftBuffer.Add(points[0]);
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector3 a = points[i], b = points[i + 1];
                if (i > 0) SoftBuffer.Add(Vector3.Lerp(a, b, 0.25f));
                if (i < points.Count - 2) SoftBuffer.Add(Vector3.Lerp(a, b, 0.75f));
            }
            SoftBuffer.Add(points[points.Count - 1]);
            points.Clear();
            points.AddRange(SoftBuffer);
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
