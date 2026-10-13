using System.Collections.Generic;
using System.Diagnostics;
using HighCommand.Army;
using RoR2;
using UnityEngine;

namespace HighCommand.UI
{
    /// <summary>
    /// The ways the order lines are drawn along: one per hero and leg, found on the stage's ground graph and kept for a
    /// while. Finding a way costs a little, so a frame spends at most about a millisecond on it; until a way is found
    /// the line is straight.
    /// </summary>
    internal sealed class RouteCache
    {
        internal sealed class Route
        {
            internal readonly List<Vector3> Points = new List<Vector3>(64);
            internal Vector3 From, To;
            internal bool Found;          // false: a straight line (no graph, not reachable, or not looked for yet)
            internal float MadeAt = -100f;
            internal float UsedAt;
        }

        // the legs of a hero: 0 = its current order, 1.. = the queue, Preview = where a click would send it
        internal const int Preview = 100;

        private const float FrameBudgetMs = 1.1f;
        private const float RefreshAfter = 0.9f;     // a moving hero: the way is looked for again now and then
        private const float TargetMoved = 1.5f;      // the end moved (a new click, a followed friend): look again at once
        private const float StartMoved = 5f;

        private readonly Dictionary<(Hero, int), Route> routes = new Dictionary<(Hero, int), Route>(32);
        private readonly List<(Hero, int)> gone = new List<(Hero, int)>(16);
        private readonly Stopwatch watch = new Stopwatch();
        private int frame = -1;
        private int computed, found;
        private float cleanAt;

        // diagnostics (hc_routes)
        internal static int LastComputed, LastFound, LastCount;
        internal static double LastMs;

        internal Route Get(Hero hero, int leg, Vector3 from, Vector3 to)
        {
            BeginFrame();
            float now = Time.unscaledTime;
            if (!routes.TryGetValue((hero, leg), out Route route))
            {
                route = new Route();
                routes[(hero, leg)] = route;
            }
            route.UsedAt = now;

            bool urgent = route.MadeAt < 0f || (route.To - to).sqrMagnitude > TargetMoved * TargetMoved;
            bool stale = now - route.MadeAt > RefreshAfter || (route.From - from).sqrMagnitude > StartMoved * StartMoved;
            // one urgent search a frame is always allowed (the line under a fresh click must bend at once)
            bool allowed = watch.Elapsed.TotalMilliseconds < FrameBudgetMs || (urgent && computed == 0);
            if ((urgent || stale) && allowed)
            {
                watch.Start();
                CharacterBody body = hero != null ? hero.Body : null;
                HullClassification hull = body ? body.hullClassification : HullClassification.Human;
                float jump = body ? body.maxJumpHeight : 6f;
                route.Found = Orders.PathCost.Route(from, to, hull, jump, route.Points);
                watch.Stop();
                route.From = from;
                route.To = to;
                route.MadeAt = now;
                computed++;
                if (route.Found) found++;
            }
            else if (urgent)
            {
                // not looked for yet: a straight line for a frame or two
                route.Points.Clear();
                route.Points.Add(from);
                route.Points.Add(to);
                route.Found = false;
            }
            return route;
        }

        private void BeginFrame()
        {
            int f = Time.frameCount;
            if (f == frame) return;
            frame = f;
            LastMs = watch.Elapsed.TotalMilliseconds;
            watch.Reset();
            LastComputed = computed;
            LastFound = found;
            computed = 0;
            found = 0;
            LastCount = routes.Count;

            float now = Time.unscaledTime;
            if (now < cleanAt) return;
            cleanAt = now + 2f;
            gone.Clear();
            foreach (var pair in routes)
                if (pair.Key.Item1 == null || now - pair.Value.UsedAt > 2f) gone.Add(pair.Key);
            for (int i = 0; i < gone.Count; i++) routes.Remove(gone[i]);
        }

        internal void Clear() => routes.Clear();
    }
}
