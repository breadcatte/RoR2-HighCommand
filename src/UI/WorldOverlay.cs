using System.Collections.Generic;
using HighCommand.Army;
using HighCommand.Client;
using RoR2;
using UnityEngine;
using UnityEngine.UI;

namespace HighCommand.UI
{
    /// <summary>
    /// Everything drawn "in the world" but rendered as flat screen-space UI: selection rings, health / shield /
    /// barrier bars over allies and enemies, the hover highlight and the drag-selection rectangle. Pooled images,
    /// positions recomputed every frame after the camera has moved.
    /// </summary>
    internal sealed class WorldOverlay
    {
        private sealed class Bar
        {
            internal RectTransform root;
            internal Image back, trail, health, shield, barrier, frame, gloss;
            internal RawImage ticks;

            // what was written last: the bar is touched only when its picture really changes
            internal Vector2 position;
            internal float width, height, health01 = -1f, shield01 = -1f, barrier01 = -1f, trail01 = -1f, segments = -1f;
            internal Color healthColor, frameColor;
        }

        // the recent damage shown as a pale piece that drains after the hit (per body: the pooled bars move between bodies)
        private sealed class Trail
        {
            internal float value = -1f;
            internal float holdUntil;
            internal float seen;
        }

        private readonly Dictionary<CharacterBody, Trail> trails = new Dictionary<CharacterBody, Trail>(64);
        private readonly List<CharacterBody> trailsGone = new List<CharacterBody>(16);
        private float trailsCleanAt;

        private readonly RectTransform layer;
        private readonly List<Bar> bars = new List<Bar>(128);
        private readonly List<Image> rings = new List<Image>(32);
        private readonly List<Image> arcs = new List<Image>(48);
        private readonly List<Image> markers = new List<Image>(32);
        private readonly List<Hero> boxPreview = new List<Hero>(16);
        private readonly Image dragBox;
        private int barsUsed, ringsUsed, arcsUsed, markersUsed;

        /// <summary>Hero under the mouse / hostile under the mouse, set by the input handler each frame.</summary>
        internal Hero HoveredHero;
        internal CharacterBody HoveredEnemy;

        internal Rect? DragRect;

        internal WorldOverlay(Transform parent)
        {
            layer = UiKit.NewRect("WorldOverlay", parent);
            UiKit.Stretch(layer);
            // Its own canvas: the bars and rings change every frame, and a change rebuilds only the canvas it is on.
            // Without this the static panels would be re-batched together with them each frame.
            layer.gameObject.AddComponent<Canvas>();

            // price tags lie under everything else of the overlay
            tagLayer = UiKit.NewRect("PriceTags", layer);
            UiKit.Stretch(tagLayer);

            // the order routes lie under the rings and the bars
            routeLines = RouteMesh.Create("RouteLines", layer, UiKit.SoftLine.texture);

            // The selection box: a faint mint tint, a hairline border and brighter corner brackets. The children are
            // anchored to the box, so they follow its size without being touched again.
            dragBox = UiKit.NewImage("DragBox", layer, UiKit.White, new Color(0.6f, 1f, 0.8f, 0.08f));
            dragBox.enabled = false;
            Color edge = new Color(0.78f, 1f, 0.9f, 0.5f);
            Color corner = new Color(0.9f, 1f, 0.95f, 0.95f);
            DragEdge("Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 1.5f), edge);
            DragEdge("Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 1.5f), edge);
            DragEdge("Left", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(1.5f, 0f), edge);
            DragEdge("Right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(1.5f, 0f), edge);
            const float length = 14f, thick = 3f;
            for (int cx = 0; cx < 2; cx++)
                for (int cy = 0; cy < 2; cy++)
                {
                    var at = new Vector2(cx, cy);
                    DragEdge("CornerH", at, at, at, new Vector2(length, thick), corner);
                    DragEdge("CornerV", at, at, at, new Vector2(thick, length), corner);
                }
        }

        private void DragEdge(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size, Color color)
        {
            Image img = UiKit.NewImage(name, dragBox.transform, UiKit.White, color);
            img.enabled = true;
            RectTransform rt = img.rectTransform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
            img.gameObject.SetActive(false);
        }

        internal void Destroy()
        {
            if (layer) Object.Destroy(layer.gameObject);
        }

        /// <summary>Redraws everything. Call after the camera has been positioned for this frame.</summary>
        internal void Draw(RtsCamera camera, Selection selection, IReadOnlyList<Hero> heroes, OrderController.CursorInfo cursor = default)
        {
            barsUsed = 0;
            ringsUsed = 0;
            arcsUsed = 0;
            markersUsed = 0;
            ghostsUsed = 0;
            edgeMarksUsed = 0;
            arrowheadsUsed = 0;
            pingsUsed = 0;
            tagsUsed = 0;
            routeLines.Begin();

            if (camera && camera.SceneCam)
            {
                sceneCam = camera.SceneCam;
                camDistance = camera.Distance;
                DrawPriceTags(camera);
                DrawRings(selection, heroes);
                DrawGhosts(camera, selection, heroes);
                DrawBars(camera, selection);
                DrawOrders(selection, cursor);
                DrawPings();
                DrawPopups();
            }
            routeLines.End();
            LastRouteVertices = routeLines.Vertices;

            LastBars = barsUsed;
            LastRings = ringsUsed;
            HideUnused();
            DrawDragRect();
            DrawCursorBadge(cursor);
        }

        // ---- price tags over chests, shrines, shops ... -------------------------------------------
        //
        // The stock game shows a price hologram only next to a player's body; the commander has none. A small tag above
        // every priced object in view says what it costs, gold when the purse can pay, red when it cannot.

        private sealed class PriceTag
        {
            internal TMPro.TextMeshProUGUI text;
            internal int cost = -1;
            internal bool affordable;
            internal CostTypeIndex type;
        }

        private readonly RectTransform tagLayer;
        private readonly List<PriceTag> tags = new List<PriceTag>(32);
        private int tagsUsed;
        private const int TagLimit = 40;
        private const float TagRange = 160f;

        private static readonly Color TagGold = new Color(1f, 0.85f, 0.35f, 1f);
        private static readonly Color TagRed = new Color(1f, 0.45f, 0.4f, 1f);
        private static readonly Color TagOther = new Color(0.8f, 0.85f, 1f, 1f);

        private void DrawPriceTags(RtsCamera camera)
        {
            Camera cam = camera.SceneCam;
            Vector3 eye = cam.transform.position;
            var list = InstanceTracker.GetInstancesList<PurchaseInteraction>();
            for (int i = 0; i < list.Count && tagsUsed < TagLimit; i++)
            {
                PurchaseInteraction p = list[i];
                if (!p || !p.available || p.costType == CostTypeIndex.None) continue;
                if (p.costType == CostTypeIndex.Money && p.cost <= 0) continue;
                GameObject thing = p.gameObject;
                InteractKind kind = Interactables.KindOf(thing);
                if (kind == InteractKind.Unsupported || kind == InteractKind.Teleporter) continue;

                Vector3 at = thing.transform.position + Vector3.up * 1.8f;
                if ((at - eye).sqrMagnitude > TagRange * TagRange) continue;
                if (!WorldPicker.ProjectPoint(cam, at, out Vector2 s) || !WorldPicker.IsOnScreen(s, -10f)) continue;
                if (OverlapsTag(s, p.costType, p.cost)) continue; // the three terminals of a shop: one tag says it

                bool affordable = Interactables.Affordable(thing);
                PriceTag tag = AcquireTag();
                if (tag.cost != p.cost || tag.affordable != affordable || tag.type != p.costType)
                {
                    tag.cost = p.cost;
                    tag.affordable = affordable;
                    tag.type = p.costType;
                    tag.text.text = "<mark=#05080CC8> " + Interactables.PriceText(p.costType, p.cost) + " </mark>";
                    tag.text.color = !affordable ? TagRed : p.costType == CostTypeIndex.Money ? TagGold : TagOther;
                }
                tag.text.rectTransform.position = new Vector3(s.x, s.y, 0f);
            }
        }

        // a tag of the same price already placed close by
        private bool OverlapsTag(Vector2 screen, CostTypeIndex type, int cost)
        {
            float near = 34f * Mathf.Max(0.0001f, layer.lossyScale.x);
            for (int i = 0; i < tagsUsed; i++)
            {
                PriceTag t = tags[i];
                if (t.type != type || t.cost != cost) continue;
                if (((Vector2)t.text.rectTransform.position - screen).sqrMagnitude < near * near) return true;
            }
            return false;
        }

        private PriceTag AcquireTag()
        {
            if (tagsUsed >= tags.Count)
            {
                var t = new PriceTag { text = UiKit.NewText("Price", tagLayer, "", 15f, TagGold, TMPro.TextAlignmentOptions.Center) };
                t.text.rectTransform.sizeDelta = new Vector2(160f, 22f);
                t.text.enableWordWrapping = false;
                tags.Add(t);
            }
            PriceTag tag = tags[tagsUsed++];
            if (!tag.text.enabled) tag.text.enabled = true;
            return tag;
        }

        // ---- orders: destination markers, lines and pings ----------------------------------------

        private readonly List<Image> pings = new List<Image>(16);
        private int pingsUsed;
        // the pointer badge: a small dark plate with a picture and a word ("Attack", "Move (queued)" ...)
        private RectTransform badge;
        private Image badgePlate;
        private Image badgeGlyph;
        private TMPro.TextMeshProUGUI badgeLabel;
        private string badgeShownLabel;
        private float badgeWidth;

        private const int PreviewLimit = 6;

        // the camera can be tilted during the game: ground marks are flattened by the tilt it has now
        private static float CurrentPitch()
        {
            RtsCamera camera = RtsCamera.Instance;
            return camera ? camera.Pitch : Settings.CameraPitch.Value;
        }

        // ---- order routes ----------------------------------------------------------------------
        //
        // An order is drawn along the way the heroes will really walk (the stage's ground graph), not as a straight line
        // through hills. Heroes standing together with one order share one line, from their middle to a ring on the
        // ground as wide as their formation. The line is a smooth ribbon with soft light pulses running to the target;
        // everything is placed in the world (pulses are measured from the target), so moving the camera moves nothing on
        // the line. A long way shows its first and last stretch only, the middle fades out. A target off the screen gets
        // a mark at the screen edge.

        private enum RouteStyle { Preview, Current, Queued }

        // heroes drawn as one: close to each other, the same order to (nearly) the same place
        private sealed class RouteGroup
        {
            internal readonly List<Hero> Members = new List<Hero>(8);
            internal Orders.OrderKind Kind;
            internal Vector3 FeetSum, PointSum, Feet0, Point0;
            internal Vector3 Feet => FeetSum / Members.Count;
            internal Vector3 Point => PointSum / Members.Count;
        }

        private readonly List<RouteGroup> groups = new List<RouteGroup>(8);
        private int groupsUsed;

        private const float GroupStand = 25f;     // heroes this close (m) count as standing together
        private const float GroupTarget = 14f;    // and sent this close to each other

        private readonly RouteCache routes = new RouteCache();
        private readonly RouteMesh routeLines;

        // one route being drawn: world points with their distance from the start
        private readonly List<Vector3> routeWorld = new List<Vector3>(96);
        private readonly List<float> routeAt = new List<float>(96);
        private readonly List<Vector2> runScreen = new List<Vector2>(192);
        private readonly List<Color32> runColor = new List<Color32>(192);
        private readonly List<Color32> runShade = new List<Color32>(192);
        private readonly List<float> runWidth = new List<float>(192);
        private float fadeHead, fadeHeadEnd, fadeTailStart, fadeTail, routeLength;
        private float camDistance = 40f;
        private Camera sceneCam;

        private const float RouteLift = 0.3f;     // above the ground nodes, so the line is not cut by the ground

        private void DrawOrders(Selection selection, OrderController.CursorInfo cursor)
        {
            BuildGroups(selection.Heroes);
            int previews = 0;
            for (int g = 0; g < groupsUsed; g++)
            {
                RouteGroup group = groups[g];
                Hero lead = group.Members[0];
                Vector3 feet = group.Feet;
                Vector3 last = feet;

                if (OrderLook(group.Kind, out Color color, out Glyph _) && group.Point0 != Vector3.zero)
                {
                    Vector3 end = group.Point;
                    float radius = TargetRadius(group, end);
                    float length = DrawRoute(lead, 0, feet, end, color, RouteStyle.Current, radius);
                    DrawGroundRing(end, radius, color, RouteStyle.Current);
                    if (length > 0f) AddEdgeTarget(end, group.Kind, length);
                    last = end;

                    // the rest of the queue (Shift orders), dimmer, leg after leg. The queue lives in the server's
                    // brain: the mode is single-player, so the host reads it directly.
                    Orders.HeroBrain brain = lead.Brain;
                    if (brain != null)
                    {
                        IReadOnlyList<Orders.Order> queue = brain.Queue;
                        for (int q = 0; q < queue.Count && q < 8; q++)
                        {
                            Orders.Order o = queue[q];
                            if (o == null || !OrderLook(o.Kind, out Color qc, out Glyph _)) continue;
                            Vector3 next = o.Destination();
                            if (next == Vector3.zero) continue;
                            float r = IsPlace(o.Kind) ? radius : 1.2f;
                            DrawRoute(lead, q + 1, last, next, qc, RouteStyle.Queued, r);
                            DrawGroundRing(next, r, qc, RouteStyle.Queued);
                            last = next;
                        }
                    }
                }

                // where a click would send them: from their middle, or from the end of the queue with Shift held
                if (cursor.Preview && cursor.HasPoint && cursor.Runner == null && previews < PreviewLimit)
                {
                    previews++;
                    float r = Mathf.Clamp(0.7f + 0.4f * Mathf.Sqrt(group.Members.Count), 1.2f, 3f);
                    DrawRoute(lead, RouteCache.Preview, cursor.Queued ? last : feet, cursor.Point, cursor.Color, RouteStyle.Preview, r);
                    DrawGroundRing(cursor.Point, r, cursor.Color, RouteStyle.Preview);
                }
            }

            // an object under the pointer: one hero would go, the line starts at it (with Shift: where its trips end)
            Hero runner = cursor.Runner;
            if (cursor.Preview && cursor.HasPoint && runner != null && runner.Body)
            {
                Vector3 start = runner.Body.footPosition;
                Orders.HeroBrain brain = runner.Brain;
                if (cursor.Queued && brain != null) start = brain.ErrandsEnd(start);
                DrawRoute(runner, RouteCache.Preview, start, cursor.Point, cursor.Color, RouteStyle.Preview, 1.2f);
                DrawGroundRing(cursor.Point, 1.2f, cursor.Color, RouteStyle.Preview);
            }

            DrawEdgeTargets();
        }

        private void BuildGroups(IReadOnlyList<Hero> sel)
        {
            for (int g = 0; g < groupsUsed; g++) groups[g].Members.Clear();
            groupsUsed = 0;
            for (int i = 0; i < sel.Count; i++)
            {
                Hero hero = sel[i];
                CharacterBody body = hero != null ? hero.Body : null;
                if (!body) continue;
                var kind = (Orders.OrderKind)hero.NetOrderKind;
                Vector3 feet = body.footPosition;
                Vector3 point = hero.NetOrderPoint;
                bool place = OrderLook(kind, out Color _, out Glyph _);

                RouteGroup group = null;
                for (int g = 0; g < groupsUsed && group == null; g++)
                {
                    RouteGroup o = groups[g];
                    if (o.Kind != kind || (o.Feet0 - feet).sqrMagnitude > GroupStand * GroupStand) continue;
                    if (place && (o.Point0 - point).sqrMagnitude > GroupTarget * GroupTarget) continue;
                    group = o;
                }
                if (group == null)
                {
                    if (groupsUsed >= groups.Count) groups.Add(new RouteGroup());
                    group = groups[groupsUsed++];
                    group.Kind = kind;
                    group.Feet0 = feet;
                    group.Point0 = point;
                    group.FeetSum = Vector3.zero;
                    group.PointSum = Vector3.zero;
                }
                group.Members.Add(hero);
                group.FeetSum += feet;
                group.PointSum += point;
            }
        }

        // a place on the ground (the formation spreads round it) or a thing (an enemy, a friend, a chest)
        private static bool IsPlace(Orders.OrderKind kind) =>
            kind == Orders.OrderKind.Move || kind == Orders.OrderKind.AttackMove || kind == Orders.OrderKind.Retreat || kind == Orders.OrderKind.Patrol;

        // the ring round a target: as wide as the formation the group will stand in
        private static float TargetRadius(RouteGroup group, Vector3 end)
        {
            if (!IsPlace(group.Kind)) return 1.2f;
            float spread = 0f;
            for (int i = 0; i < group.Members.Count; i++)
                spread = Mathf.Max(spread, Vector3.Distance(group.Members[i].NetOrderPoint, end));
            return Mathf.Clamp(spread * 0.6f + 0.9f, 1.2f, 3.5f);
        }

        private static bool OrderLook(Orders.OrderKind kind, out Color color, out Glyph glyph)
        {
            color = new Color(0.45f, 1f, 0.55f, 1f);
            glyph = Glyph.Move;
            switch (kind)
            {
                case Orders.OrderKind.Move: return true;
                case Orders.OrderKind.AttackMove: color = new Color(1f, 0.55f, 0.3f, 1f); glyph = Glyph.AttackMove; return true;
                case Orders.OrderKind.Retreat: color = new Color(0.5f, 0.8f, 1f, 1f); glyph = Glyph.Retreat; return true;
                case Orders.OrderKind.Patrol: color = new Color(1f, 0.8f, 0.3f, 1f); glyph = Glyph.Patrol; return true;
                case Orders.OrderKind.Attack: color = new Color(1f, 0.3f, 0.25f, 1f); glyph = Glyph.Attack; return true;
                case Orders.OrderKind.Follow: glyph = Glyph.Follow; return true;
                case Orders.OrderKind.Interact: color = new Color(1f, 0.9f, 0.3f, 1f); glyph = Glyph.Interact; return true;
            }
            return false;
        }

        // the zoom in steps of two: the spacing of the line's points and pulses changes only when the zoom does
        private float ZoomStep() => Mathf.Pow(2f, Mathf.Max(0f, Mathf.Round(Mathf.Log(Mathf.Max(1f, camDistance / 45f), 2f))));

        /// <summary>Draws the way from one point to a ring round another; returns its length in meters.</summary>
        private float DrawRoute(Hero hero, int leg, Vector3 from, Vector3 to, Color color, RouteStyle style, float ringRadius)
        {
            RouteCache.Route route = routes.Get(hero, leg, from, to);
            BuildRouteWorld(route.Points, from, to);
            Camera cam = sceneCam;
            if (!cam || routeLength < 0.5f) return routeLength;

            // what is shown of a long way: its first stretch and its last one (longer when the camera is far out)
            float head = Mathf.Max(30f, camDistance * 0.45f);
            float tail = Mathf.Max(10f, head * 0.35f);
            fadeHead = head;
            fadeHeadEnd = head * 1.45f;
            fadeTail = routeLength - tail;
            fadeTailStart = fadeTail - tail * 0.8f;
            if (fadeTailStart <= fadeHeadEnd) fadeHead = fadeHeadEnd = fadeTailStart = fadeTail = routeLength + 1f;

            float alpha, width, pulse;
            switch (style)
            {
                case RouteStyle.Preview: alpha = 0.9f; width = 7f; pulse = 1f; break;
                case RouteStyle.Queued: alpha = 0.5f; width = 4.5f; pulse = 0f; break;
                default: alpha = 0.85f; width = 6.5f; pulse = 0.8f; break;
            }
            if (!route.Found) alpha *= 0.7f; // a way that could not be found stays straight and fainter

            float zoom = ZoomStep();
            float step = 0.6f * zoom;                       // meters between the points of the ribbon
            float period = 9f * zoom, comet = 3f * zoom;     // a pulse every 9 m, its glowing tail 3 m long
            float shift = Time.unscaledTime * 7f * zoom;     // pulses run 7 m/s towards the target
            float stopAt = ringRadius + 0.35f;              // the ribbon ends where the arrowhead sits, inside the ring

            // the ribbon is sampled from the target backwards: the points stay where they are while the heroes walk on
            runScreen.Clear();
            runColor.Clear();
            runShade.Clear();
            runWidth.Clear();
            int seg = routeWorld.Count - 1;
            bool prevVisible = false;
            for (float d = stopAt; ; d += step)
            {
                bool lastPoint = d >= routeLength;
                if (lastPoint) d = routeLength;
                float at = routeLength - d;
                float a = RouteAlpha(at) * Mathf.Clamp01(at / 2.5f);   // it also grows out of the heroes softly
                bool visible = a > 0.003f;
                if (visible || prevVisible)
                {
                    while (seg > 1 && routeAt[seg - 1] > at) seg--;
                    float a0 = routeAt[seg - 1], a1 = routeAt[seg];
                    Vector3 p = Vector3.Lerp(routeWorld[seg - 1], routeWorld[seg], Mathf.Clamp01((at - a0) / Mathf.Max(0.0001f, a1 - a0)));
                    if (WorldPicker.ProjectPoint(cam, p + Vector3.up * RouteLift, out Vector2 s))
                    {
                        float u = Mathf.Repeat(d + shift, period);
                        float glow = pulse * Mathf.Clamp01(1f - u / comet);
                        Color c = Color.Lerp(color, Color.white, 0.85f * glow);
                        c.a = Mathf.Clamp01(alpha * a * (1f + 0.3f * glow));
                        runScreen.Add(s);
                        runColor.Add(c);
                        runShade.Add(new Color(0f, 0f, 0f, 0.65f * Mathf.Clamp01(alpha * 1.3f) * a));
                        runWidth.Add(1f + 0.6f * glow);   // a pulse also swells the ribbon a little
                    }
                    else FlushRun(width);
                }
                if (!visible && prevVisible) FlushRun(width);
                prevVisible = visible;
                if (lastPoint) break;
            }
            FlushRun(width);

            DrawArrowhead(cam, stopAt, color, alpha * RouteAlpha(routeLength - stopAt));
            return routeLength;
        }

        // the way as world points: the heroes' middle, the cached points not passed yet, the target as it is now
        private void BuildRouteWorld(List<Vector3> points, Vector3 from, Vector3 to)
        {
            routeWorld.Clear();
            routeAt.Clear();
            routeWorld.Add(from);
            int n = points.Count;
            // the heroes walk on between two searches: start from the cached segment nearest to them
            int nearest = 0;
            float best = float.MaxValue;
            for (int i = 0; i < n - 1 && i < 10; i++)
            {
                float d = SqrDistanceToSegment(from, points[i], points[i + 1]);
                if (d < best) { best = d; nearest = i; }
            }
            for (int i = nearest + 1; i < n - 1; i++) routeWorld.Add(points[i]);
            routeWorld.Add(to);

            float at = 0f;
            routeAt.Add(0f);
            for (int i = 1; i < routeWorld.Count; i++)
            {
                at += Vector3.Distance(routeWorld[i - 1], routeWorld[i]);
                routeAt.Add(at);
            }
            routeLength = at;
        }

        private static float SqrDistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float len = ab.sqrMagnitude;
            float t = len > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / len) : 0f;
            return (p - (a + ab * t)).sqrMagnitude;
        }

        private float RouteAlpha(float at)
        {
            float head = 1f - Mathf.Clamp01((at - fadeHead) / Mathf.Max(0.001f, fadeHeadEnd - fadeHead));
            float tail = Mathf.Clamp01((at - fadeTailStart) / Mathf.Max(0.001f, fadeTail - fadeTailStart));
            return Mathf.Max(head, tail);
        }

        // a world point at a distance from the target, and the way's direction there
        private Vector3 PointFromEnd(float d, out Vector3 dir)
        {
            float at = Mathf.Clamp(routeLength - d, 0f, routeLength);
            int seg = routeWorld.Count - 1;
            while (seg > 1 && routeAt[seg - 1] > at) seg--;
            float a0 = routeAt[seg - 1], a1 = routeAt[seg];
            dir = routeWorld[seg] - routeWorld[seg - 1];
            return Vector3.Lerp(routeWorld[seg - 1], routeWorld[seg], Mathf.Clamp01((at - a0) / Mathf.Max(0.0001f, a1 - a0)));
        }

        private void FlushRun(float width)
        {
            int count = runScreen.Count;
            if (count >= 2)
            {
                routeLines.Strip(runScreen, runShade, 0, count, width + 8f, runWidth);
                routeLines.Strip(runScreen, runColor, 0, count, width, runWidth);
            }
            runScreen.Clear();
            runColor.Clear();
            runShade.Clear();
            runWidth.Clear();
        }

        // ---- the ends: an arrowhead and a ring lying on the ground ----

        private readonly List<Image> arrowheads = new List<Image>(16);
        private int arrowheadsUsed;
        private readonly List<Vector2> ringScreen = new List<Vector2>(40);
        private readonly List<Color32> ringColor = new List<Color32>(40);
        private readonly List<Color32> ringShade = new List<Color32>(40);

        private void DrawArrowhead(Camera cam, float atFromEnd, Color color, float alpha)
        {
            if (alpha < 0.05f || routeLength < atFromEnd + 1f) return;
            Vector3 p = PointFromEnd(atFromEnd, out Vector3 dir);
            if (dir.sqrMagnitude < 1e-6f) return;
            p += Vector3.up * RouteLift;
            if (!WorldPicker.ProjectPoint(cam, p, out Vector2 s) || !WorldPicker.IsOnScreen(s, 30f)) return;
            if (!WorldPicker.ProjectPoint(cam, p + dir.normalized * 0.5f, out Vector2 s2)) return;
            Vector2 d = s2 - s;
            if (d.sqrMagnitude < 1e-4f) return;
            // the marker sprite points down: turned by the direction + 90 degrees it points along the way
            Quaternion turn = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg + 90f);

            Image shade = AcquireArrowhead();
            shade.color = new Color(0f, 0f, 0f, 0.6f * alpha);
            shade.rectTransform.sizeDelta = new Vector2(23f, 19f);
            shade.rectTransform.position = new Vector3(s.x, s.y, 0f);
            shade.rectTransform.localRotation = turn;

            Image head = AcquireArrowhead();
            Color c = color;
            c.a = Mathf.Clamp01(alpha * 1.15f);
            head.color = c;
            head.rectTransform.sizeDelta = new Vector2(17f, 14f);
            head.rectTransform.position = new Vector3(s.x, s.y, 0f);
            head.rectTransform.localRotation = turn;
        }

        private Image AcquireArrowhead()
        {
            if (arrowheadsUsed >= arrowheads.Count)
            {
                Image made = UiKit.NewImage("Arrowhead", layer, UiKit.Marker, Color.white);
                made.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                arrowheads.Add(made);
            }
            Image img = arrowheads[arrowheadsUsed++];
            if (!img.enabled) img.enabled = true;
            return img;
        }

        // a circle on the ground round the target (projected, so it lies flat in the view like the ground does)
        private void DrawGroundRing(Vector3 center, float radius, Color color, RouteStyle style)
        {
            Camera cam = sceneCam;
            if (!cam) return;
            float alpha = style == RouteStyle.Queued ? 0.5f : 0.9f;
            float width = style == RouteStyle.Queued ? 2.5f : 3.5f;
            if (style == RouteStyle.Preview) radius *= 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 5f);
            const int sides = 36;
            ringScreen.Clear();
            ringColor.Clear();
            ringShade.Clear();
            Color c = color;
            c.a = alpha;
            Color shade = new Color(0f, 0f, 0f, 0.5f * alpha);
            Vector3 lifted = center + Vector3.up * RouteLift;
            for (int i = 0; i <= sides; i++)
            {
                float angle = i * (Mathf.PI * 2f / sides);
                Vector3 p = lifted + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                if (!WorldPicker.ProjectPoint(cam, p, out Vector2 s)) return;
                ringScreen.Add(s);
                ringColor.Add(c);
                ringShade.Add(shade);
            }
            routeLines.Strip(ringScreen, ringShade, 0, ringScreen.Count, width + 4f);
            routeLines.Strip(ringScreen, ringColor, 0, ringScreen.Count, width);
        }
        // ---- targets off the screen: a mark at the edge with the order's picture, an arrow and the distance ----

        private struct EdgeTarget
        {
            internal Vector3 point;
            internal Orders.OrderKind kind;
            internal float meters;
        }

        // one mark: a dark disc with a coloured rim and the order's picture, an arrow outside it, the distance below
        private sealed class EdgeMark
        {
            internal RectTransform root;
            internal Image plate, rim, icon, arrowShade, arrow;
            internal TMPro.TextMeshProUGUI distance;
            internal RectTransform arrowPivot;
            internal Glyph glyph = Glyph.Count;
            internal int meters = -1;
        }

        private readonly List<EdgeTarget> edgeTargets = new List<EdgeTarget>(8);
        private readonly List<EdgeMark> edgeMarks = new List<EdgeMark>(4);
        private int edgeMarksUsed;

        // a mark sits this far inside the screen edge (canvas units); the text under it needs a little more at the bottom
        private const float EdgeInset = 40f, EdgeInsetBottom = 58f;

        // the mode's panels (canvas units at 1920 x 1080, from the corner each one hangs on): a mark is pushed out of them
        private readonly Rect[] panelRects = new Rect[5];

        private void AddEdgeTarget(Vector3 point, Orders.OrderKind kind, float meters)
        {
            // a group sent to one place: one mark (the nearest hero's distance)
            for (int i = 0; i < edgeTargets.Count; i++)
            {
                EdgeTarget t = edgeTargets[i];
                if (t.kind != kind || (t.point - point).sqrMagnitude > 10f * 10f) continue;
                if (meters < t.meters) { t.meters = meters; edgeTargets[i] = t; }
                return;
            }
            if (edgeTargets.Count < 6) edgeTargets.Add(new EdgeTarget { point = point, kind = kind, meters = meters });
        }

        private void DrawEdgeTargets()
        {
            Camera cam = sceneCam;
            if (!cam) { edgeTargets.Clear(); return; }
            float scale = Mathf.Max(0.0001f, layer.lossyScale.x);
            float left = EdgeInset * scale, right = Screen.width - EdgeInset * scale;
            float bottom = EdgeInsetBottom * scale, top = Screen.height - EdgeInset * scale;
            var center = new Vector2(Screen.width * 0.5f, (bottom + top) * 0.5f);
            float w = Screen.width, h = Screen.height;
            panelRects[0] = new Rect(0f, h - 290f * scale, 280f * scale, 290f * scale);             // the heroes, top left
            panelRects[1] = new Rect(0f, h - 90f * scale, 1620f * scale, 90f * scale);              // the top bar
            panelRects[2] = new Rect(0f, 0f, 290f * scale, 275f * scale);                          // the minimap
            panelRects[3] = new Rect(w * 0.5f - 180f * scale, 0f, 360f * scale, 125f * scale);     // the commander's abilities
            panelRects[4] = new Rect(w - 360f * scale, 0f, 360f * scale, 185f * scale);            // the orders card
            for (int i = 0; i < edgeTargets.Count; i++)
            {
                EdgeTarget t = edgeTargets[i];
                Vector3 p = cam.WorldToScreenPoint(t.point + Vector3.up * RouteLift);
                Vector2 s = new Vector2(p.x, p.y);
                bool behind = p.z <= 0.1f;
                // only a target really off the screen gets a mark (the free area only says where the mark goes)
                if (!behind && WorldPicker.IsOnScreen(s, -12f * scale)) continue;
                Vector2 dir = behind ? center - s : s - center;
                if (dir.sqrMagnitude < 1f) continue;
                dir.Normalize();

                // where the line from the middle of the free area towards the target leaves it
                float hx = (right - left) * 0.5f, hy = (top - bottom) * 0.5f;
                float k = Mathf.Min(Mathf.Abs(dir.x) > 1e-4f ? hx / Mathf.Abs(dir.x) : float.MaxValue,
                                    Mathf.Abs(dir.y) > 1e-4f ? hy / Mathf.Abs(dir.y) : float.MaxValue);
                Vector2 at = ClearOfPanels(center + dir * k, 26f * scale, left, right, bottom, top);
                OrderLook(t.kind, out Color color, out Glyph glyph);

                EdgeMark mark = AcquireEdgeMark();
                mark.root.position = new Vector3(at.x, at.y, 0f);
                if (mark.glyph != glyph)
                {
                    mark.glyph = glyph;
                    mark.icon.sprite = UiKit.GlyphSprite(glyph);
                }
                mark.rim.color = color;
                mark.icon.color = color;
                mark.arrow.color = color;
                // the marker sprite points down: turned by the direction + 90 degrees, it points away from the middle
                mark.arrowPivot.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + 90f);
                int meters = Mathf.RoundToInt(t.meters);
                if (mark.meters != meters)
                {
                    mark.meters = meters;
                    mark.distance.text = Loc.Format("HIGHCOMMAND_DISTANCE", meters);
                }
                mark.distance.color = color;
            }
            edgeTargets.Clear();
        }

        // a point inside a panel (grown by pad) moved out of it the shortest way that stays on the screen
        private Vector2 ClearOfPanels(Vector2 at, float pad, float left, float right, float bottom, float top)
        {
            for (int i = 0; i < panelRects.Length; i++)
            {
                Rect r = panelRects[i];
                if (at.x < r.xMin - pad || at.x > r.xMax + pad || at.y < r.yMin - pad || at.y > r.yMax + pad) continue;
                Vector2 best = at;
                float bestMove = float.MaxValue;
                Consider(new Vector2(at.x, r.yMax + pad), ref best, ref bestMove, at, left, right, bottom, top);
                Consider(new Vector2(at.x, r.yMin - pad), ref best, ref bestMove, at, left, right, bottom, top);
                Consider(new Vector2(r.xMax + pad, at.y), ref best, ref bestMove, at, left, right, bottom, top);
                Consider(new Vector2(r.xMin - pad, at.y), ref best, ref bestMove, at, left, right, bottom, top);
                at = best;
            }
            return at;
        }

        private static void Consider(Vector2 p, ref Vector2 best, ref float bestMove, Vector2 from, float left, float right, float bottom, float top)
        {
            if (p.x < left || p.x > right || p.y < bottom || p.y > top) return;
            float move = (p - from).sqrMagnitude;
            if (move < bestMove) { bestMove = move; best = p; }
        }

        private EdgeMark AcquireEdgeMark()
        {
            if (edgeMarksUsed >= edgeMarks.Count)
            {
                var m = new EdgeMark();
                m.root = UiKit.NewRect("EdgeMark", layer);
                m.root.sizeDelta = new Vector2(36f, 36f);
                m.plate = UiKit.NewImage("Plate", m.root, UiKit.Circle, new Color(0.03f, 0.04f, 0.06f, 0.82f));
                UiKit.Stretch(m.plate.rectTransform);
                m.rim = UiKit.NewImage("Rim", m.root, UiKit.Ring, Color.white);
                UiKit.Stretch(m.rim.rectTransform);
                m.icon = UiKit.NewImage("Icon", m.root, UiKit.White, Color.white);
                m.icon.preserveAspect = true;
                UiKit.Place(m.icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22f, 22f));

                // the arrow sits on a pivot in the middle of the disc and is turned with it, just outside the rim
                m.arrowPivot = UiKit.NewRect("ArrowPivot", m.root);
                UiKit.Place(m.arrowPivot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                m.arrowShade = UiKit.NewImage("ArrowShade", m.arrowPivot, UiKit.Marker, new Color(0f, 0f, 0f, 0.75f));
                UiKit.Place(m.arrowShade.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -27f), new Vector2(22f, 17f));
                m.arrow = UiKit.NewImage("Arrow", m.arrowPivot, UiKit.Marker, Color.white);
                UiKit.Place(m.arrow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -26f), new Vector2(17f, 12f));

                m.distance = UiKit.NewText("Distance", m.root, "", 16f, Color.white, TMPro.TextAlignmentOptions.Center);
                m.distance.enableWordWrapping = false;
                m.distance.text = "";
                UiKit.Place(m.distance.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -2f), new Vector2(100f, 20f));
                m.distance.outlineWidth = 0.25f;
                m.distance.outlineColor = new Color32(0, 0, 0, 230);
                edgeMarks.Add(m);
            }
            EdgeMark mark = edgeMarks[edgeMarksUsed++];
            if (!mark.root.gameObject.activeSelf) mark.root.gameObject.SetActive(true);
            return mark;
        }

        private void DrawPings()
        {
            float now = Time.unscaledTime;
            var list = Feedback.Pings;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var p = list[i];
                float age = now - p.born;
                if (age > p.life) { list.RemoveAt(i); continue; }

                float t = age / p.life;
                if (!WorldPicker.Project(p.position, p.size * (0.5f + t), out Vector2 s, out float px)) continue;

                if (pingsUsed >= pings.Count)
                    pings.Add(UiKit.NewImage("Ping", layer, UiKit.Ring, Color.white));
                Image img = pings[pingsUsed++];
                if (!img.enabled) img.enabled = true;

                Color c = p.color;
                c.a = (1f - t) * 0.95f;
                img.color = c;
                float pitch = CurrentPitch() * Mathf.Deg2Rad;
                float inv = 1f / Mathf.Max(0.0001f, layer.lossyScale.x);
                float w = Mathf.Max(16f, px * 2f) * inv;
                img.rectTransform.sizeDelta = new Vector2(w, w * Mathf.Clamp(Mathf.Sin(pitch), 0.35f, 1f));
                img.rectTransform.position = new Vector3(s.x, s.y, 0f);
            }
        }

        private readonly List<TMPro.TextMeshProUGUI> popupTexts = new List<TMPro.TextMeshProUGUI>(16);
        private int popupsUsed;

        private void DrawPopups()
        {
            popupsUsed = 0;
            float now = Time.unscaledTime;
            var list = Feedback.Popups;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var p = list[i];
                float age = now - p.born;
                if (age > 1.1f) { list.RemoveAt(i); continue; }
                Vector3 world = p.position + Vector3.up * (1.2f + age * 2.2f);
                if (!WorldPicker.Project(world, 0.3f, out Vector2 s, out float _)) continue;

                if (popupsUsed >= popupTexts.Count)
                {
                    var created = UiKit.NewText("Popup", layer, "", 24f, Color.white, TMPro.TextAlignmentOptions.Center);
                    created.rectTransform.sizeDelta = new Vector2(200f, 34f);
                    popupTexts.Add(created);
                }
                var label = popupTexts[popupsUsed++];
                label.enabled = true;
                if (label.text != p.text) label.text = p.text;
                Color c = p.color;
                c.a = Mathf.Clamp01(1.5f - age * 1.3f);
                label.color = c;
                label.rectTransform.position = new Vector3(s.x, s.y, 0f);
            }
            for (int i = popupsUsed; i < popupTexts.Count; i++)
                if (popupTexts[i].enabled) popupTexts[i].enabled = false;
        }

        private void EnsureBadge()
        {
            if (badge) return;
            badge = UiKit.NewRect("CursorBadge", layer);
            badge.anchorMin = badge.anchorMax = Vector2.zero;
            badge.pivot = new Vector2(0f, 1f);
            badge.sizeDelta = new Vector2(80f, 32f);

            badgePlate = badge.gameObject.AddComponent<Image>();
            UiKit.StyleOutline(badgePlate, 8f);
            badgePlate.color = new Color(0.03f, 0.04f, 0.06f, 0.84f);
            badgePlate.raycastTarget = false;

            badgeGlyph = UiKit.NewImage("Glyph", badge, UiKit.White, Color.white);
            badgeGlyph.preserveAspect = true;
            UiKit.Place(badgeGlyph.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(7f, 0f), new Vector2(22f, 22f));

            badgeLabel = UiKit.NewText("Label", badge, "", 18f, UiKit.Text, TMPro.TextAlignmentOptions.Left);
            UiKit.Place(badgeLabel.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(36f, 0f), new Vector2(600f, 28f));
            badge.gameObject.SetActive(false);
        }

        /// <summary>The badge next to the pointer: what a click would do (the picture is the order's, the colour its kind).</summary>
        private void DrawCursorBadge(OrderController.CursorInfo cursor)
        {
            if (!cursor.Show)
            {
                if (badge && badge.gameObject.activeSelf) badge.gameObject.SetActive(false);
                return;
            }

            EnsureBadge();
            if (!badge.gameObject.activeSelf) badge.gameObject.SetActive(true);

            Sprite sprite = UiKit.GlyphSprite(cursor.Glyph);
            if (badgeGlyph.sprite != sprite) badgeGlyph.sprite = sprite;
            badgeGlyph.color = cursor.Color;

            string label = cursor.Label ?? "";
            if (!string.Equals(label, badgeShownLabel, System.StringComparison.Ordinal))
            {
                badgeShownLabel = label;
                badgeLabel.text = label;
                float text = label.Length > 0 ? badgeLabel.GetPreferredValues(label, 600f, 0f).x : 0f;
                badgeWidth = 36f + text + 12f;
                badge.sizeDelta = new Vector2(badgeWidth, 32f);
            }

            // beside the pointer, below its tip; on the left of it near the right edge of the screen
            Vector2 m = ClientInput.Mouse;
            float scale = Mathf.Max(0.0001f, layer.lossyScale.x);
            float x = m.x + 20f;
            if (x + badgeWidth * scale > Screen.width - 4f) x = m.x - 8f - badgeWidth * scale;
            badge.position = new Vector3(x, m.y - 24f, 0f);
        }

        // diagnostics (hc_overlay)
        internal static int LastBars, LastRings, LastRouteVertices;

        // ---- rings -------------------------------------------------------------------------------

        // A mark drawn over the unit would hide it (the overlay is always on top of the world). So a selected hero gets
        // only the front arc of a ring on the ground in front of its feet plus a small triangle above its health bar.
        private void DrawRings(Selection selection, IReadOnlyList<Hero> heroes)
        {
            var sel = selection.Heroes;
            for (int i = 0; i < sel.Count; i++)
            {
                DrawArc(sel[i].Body, new Color(0.45f, 1f, 0.5f, 0.95f), 1.2f);
                DrawMarker(sel[i].Body);
            }

            // while a box is being dragged, whoever it would take lights up
            if (DragRect.HasValue && heroes != null)
            {
                boxPreview.Clear();
                WorldPicker.HeroesInRect(DragRect.Value, heroes, boxPreview);
                for (int i = 0; i < boxPreview.Count; i++)
                    if (!selection.Contains(boxPreview[i])) DrawArc(boxPreview[i].Body, new Color(0.8f, 1f, 0.88f, 0.85f), 1.2f);
            }

            if (HoveredEnemy) DrawArc(HoveredEnemy, new Color(1f, 0.35f, 0.3f, 0.9f), 1.2f);
            if (HoveredHero != null && !selection.Contains(HoveredHero)) DrawArc(HoveredHero.Body, new Color(1f, 1f, 1f, 0.7f), 1.2f);
        }

        private void DrawArc(CharacterBody body, Color color, float scale)
        {
            if (!body || !body.healthComponent || !body.healthComponent.alive) return;

            Vector3 foot = body.footPosition;
            float radius = Mathf.Max(0.8f, body.radius) * scale;
            if (!WorldPicker.Project(foot, radius, out Vector2 s, out float px)) return;
            if (!WorldPicker.IsOnScreen(s, px)) return;

            float pitch = CurrentPitch() * Mathf.Deg2Rad;
            float inv = 1f / Mathf.Max(0.0001f, layer.lossyScale.x); // screen pixels -> canvas units
            float w = Mathf.Max(14f, px * 2f) * inv;
            float h = w * Mathf.Clamp(Mathf.Sin(pitch), 0.35f, 1f);

            // a dark copy a little larger and lower keeps the arc readable on bright ground
            Image shadow = AcquireArc();
            shadow.color = new Color(0f, 0f, 0f, 0.55f * color.a);
            shadow.rectTransform.sizeDelta = new Vector2(w + 3f, h + 3f);
            shadow.rectTransform.position = new Vector3(s.x, s.y - 1f, 0f);

            Image arc = AcquireArc();
            arc.color = color;
            arc.rectTransform.sizeDelta = new Vector2(w, h);
            arc.rectTransform.position = new Vector3(s.x, s.y, 0f);
        }

        private void DrawMarker(CharacterBody body)
        {
            if (!body || !body.healthComponent || !body.healthComponent.alive || !RtsCamera.Instance || !RtsCamera.Instance.SceneCam) return;

            // right above the health bar (the same anchor DrawBars uses)
            Vector3 anchor = body.corePosition + Vector3.up * (Mathf.Max(1.2f, body.radius * 1.8f) + 0.6f);
            if (!WorldPicker.ProjectPoint(RtsCamera.Instance.SceneCam, anchor, out Vector2 s) || !WorldPicker.IsOnScreen(s)) return;

            float scale = Mathf.Max(0.0001f, layer.lossyScale.x);
            Vector3 at = new Vector3(s.x, s.y + 14f * scale, 0f);

            Image shadow = AcquireMarker();
            shadow.color = new Color(0f, 0f, 0f, 0.7f);
            shadow.rectTransform.sizeDelta = new Vector2(17f, 13f);
            shadow.rectTransform.position = at;

            Image fill = AcquireMarker();
            fill.color = new Color(0.55f, 1f, 0.6f, 1f);
            fill.rectTransform.sizeDelta = new Vector2(13f, 10f);
            fill.rectTransform.position = at;
        }

        // ---- heroes behind big ground ----------------------------------------------------------------
        //
        // A rock or a roof in front of a hero is hidden (RtsCamera); a hill or a cliff cannot be. The hero is then
        // drawn as a flat silhouette where it stands, as StarCraft II or Torchlight show units behind walls.

        private readonly List<Image> ghosts = new List<Image>(8);
        private int ghostsUsed;

        private void DrawGhosts(RtsCamera camera, Selection selection, IReadOnlyList<Hero> heroes)
        {
            if (heroes == null) return;
            Camera cam = camera.SceneCam;
            float scale = Mathf.Max(0.0001f, layer.lossyScale.x);
            for (int i = 0; i < heroes.Count; i++)
            {
                Hero hero = heroes[i];
                CharacterBody body = hero != null ? hero.Body : null;
                if (!body || !hero.IsAlive || !camera.IsBehindGround(hero)) continue;
                if (!WorldPicker.ProjectPoint(cam, body.corePosition, out Vector2 s) || !WorldPicker.IsOnScreen(s)) continue;

                if (ghostsUsed >= ghosts.Count)
                {
                    Image made = UiKit.NewImage("Ghost", layer, UiKit.GlyphSprite(Glyph.Person), Color.white);
                    made.preserveAspect = true;
                    ghosts.Add(made);
                }
                Image ghost = ghosts[ghostsUsed++];
                if (!ghost.enabled) ghost.enabled = true;
                bool selected = selection.Contains(hero);
                ghost.color = selected ? new Color(0.5f, 1f, 0.55f, 0.75f) : new Color(0.55f, 0.85f, 1f, 0.6f);
                ghost.rectTransform.sizeDelta = new Vector2(26f, 26f);
                ghost.rectTransform.position = new Vector3(s.x, s.y + 2f * scale, 0f);
            }
        }

        private Image AcquireArc()
        {
            if (arcsUsed >= arcs.Count) arcs.Add(UiKit.NewImage("Arc", layer, UiKit.Arc, Color.white));
            Image img = arcs[arcsUsed++];
            if (!img.enabled) img.enabled = true;
            return img;
        }

        private Image AcquireMarker()
        {
            if (markersUsed >= markers.Count) markers.Add(UiKit.NewImage("Marker", layer, UiKit.Marker, Color.white));
            Image img = markers[markersUsed++];
            if (!img.enabled) img.enabled = true;
            return img;
        }

        private Image AcquireRing()
        {
            if (ringsUsed >= rings.Count)
            {
                Image r = UiKit.NewImage("Ring", layer, UiKit.Ring, Color.white);
                rings.Add(r);
            }
            Image ring = rings[ringsUsed++];
            if (!ring.enabled) ring.enabled = true;
            return ring;
        }

        // ---- health bars -------------------------------------------------------------------------

        // Bars in the mode's own cut-corner style: a dark plate, the fill with a slanted end, a soft gloss on its upper
        // half, notches every 100 / 250 / 1000 ... health on heroes and bosses (how much is left reads at a glance), and
        // a pale piece that shows the last hit and drains after it. A hero's colour runs green -> amber -> red and pulses
        // when it is nearly dead. Ordinary monsters at full health show no bar (a crowd stays readable); a hit, the
        // pointer, an elite or a boss bring it up.
        private static readonly Color HeroHigh = new Color(0.38f, 0.93f, 0.47f);
        private static readonly Color HeroMid = new Color(1f, 0.78f, 0.25f);
        private static readonly Color HeroLow = new Color(0.97f, 0.28f, 0.24f);
        private static readonly Color AllyColor = new Color(0.42f, 0.86f, 0.8f);
        private static readonly Color EnemyColor = new Color(0.93f, 0.26f, 0.24f);
        private static readonly Color EliteColor = new Color(1f, 0.58f, 0.18f);
        private static readonly Color BossColor = new Color(0.9f, 0.18f, 0.36f);
        private static readonly Color ShieldColor = new Color(0.38f, 0.68f, 1f);
        private static readonly Color BarrierColor = new Color(1f, 0.9f, 0.5f, 0.95f);
        private static readonly Color TrailColor = new Color(1f, 0.93f, 0.8f, 0.85f);
        private static readonly Color FrameDark = new Color(0.01f, 0.015f, 0.02f, 0.92f);
        private static readonly Color FrameBoss = new Color(0.95f, 0.72f, 0.3f, 0.95f);
        private static readonly float[] SegmentSteps = { 100f, 250f, 500f, 1000f, 2500f, 5000f, 10000f, 25000f };

        private static Texture2D tickTexture;

        // one dark notch at the end of a repeating tile (a RawImage repeats it once per segment of health, so the notches
        // fall on 100, 200 ... and never on the bar's left edge)
        private static Texture2D TickTexture
        {
            get
            {
                if (tickTexture) return tickTexture;
                const int w = 24;
                var tex = new Texture2D(w, 1, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Point
                };
                var pixels = new Color32[w];
                for (int x = 0; x < w; x++) pixels[x] = new Color32(0, 0, 0, (byte)(x == w - 1 ? 150 : 0));
                tex.SetPixels32(pixels);
                tex.Apply(false, true);
                tickTexture = tex;
                return tex;
            }
        }

        private void DrawBars(RtsCamera camera, Selection selection)
        {
            Camera cam = camera.SceneCam;
            Vector3 camPos = cam.transform.position;
            float maxDistance = Mathf.Max(70f, camera.Distance * 2.4f);
            float maxDistanceSqr = maxDistance * maxDistance;
            var bodies = CharacterBody.readOnlyInstancesList;
            float now = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;

            for (int i = 0; i < bodies.Count; i++)
            {
                CharacterBody b = bodies[i];
                if (!b) continue;
                HealthComponent hc = b.healthComponent;
                TeamComponent teamComponent = b.teamComponent;
                if (!hc || !hc.alive || !teamComponent) continue;

                TeamIndex team = teamComponent.teamIndex;
                bool ally = team == TeamIndex.Player;
                if (!ally && !WorldPicker.IsHostileToPlayer(team)) continue;

                Vector3 core = b.corePosition;
                if ((camPos - core).sqrMagnitude > maxDistanceSqr) continue;

                Hero hero = ally ? HeroMarker.Of(b) : null;
                bool boss = b.isChampion || b.isBoss;
                float total = Mathf.Max(1f, hc.fullHealth + hc.fullShield);
                float h = Mathf.Clamp01(hc.health / total);
                float sh = Mathf.Clamp01(hc.shield / total);
                float ba = Mathf.Clamp01(hc.barrier / total);

                // an untouched ordinary monster: no bar
                bool untouched = hc.health >= hc.fullHealth - 0.5f && hc.shield >= hc.fullShield - 0.5f;
                if (!ally && !boss && !b.isElite && untouched && b != HoveredEnemy) continue;

                float radius = b.radius;
                Vector3 anchor = core + Vector3.up * (Mathf.Max(1.2f, radius * 1.8f) + 0.6f);
                if (!WorldPicker.ProjectPoint(cam, anchor, out Vector2 s)) continue;
                if (!WorldPicker.IsOnScreen(s)) continue;

                // bar size: heroes and bosses wider, grows slightly with body size
                float width = hero != null ? 80f : boss ? 128f : 40f + Mathf.Min(24f, radius * 10f);
                float height = hero != null ? 10f : boss ? 11f : 6f;

                Bar bar = AcquireBar();
                if (bar.width != width || bar.height != height)
                {
                    bar.width = width;
                    bar.height = height;
                    bar.root.sizeDelta = new Vector2(width, height);
                    bar.segments = -1f;
                }
                if (bar.position != s)
                {
                    bar.position = s;
                    bar.root.position = new Vector3(s.x, s.y, 0f);
                }

                // ---- the last hit, draining ----
                float hpFraction = h + sh;
                if (!trails.TryGetValue(b, out Trail trail))
                {
                    trail = new Trail { value = hpFraction };
                    trails[b] = trail;
                }
                trail.seen = now;
                if (hpFraction >= trail.value) trail.value = hpFraction;
                else if (now >= trail.holdUntil) trail.value = Mathf.MoveTowards(trail.value, hpFraction, dt * 0.9f);
                if (hpFraction < trail.value - 0.001f && trail.holdUntil < now - 0.5f) trail.holdUntil = now + 0.4f;
                float shownTrail = trail.value > hpFraction + 0.003f ? trail.value : 0f;

                // ---- colours ----
                Color fill;
                if (hero != null)
                {
                    float frac = hc.fullHealth > 0f ? hc.health / hc.fullHealth : 1f;
                    fill = frac > 0.5f ? HeroHigh
                         : frac > 0.25f ? Color.Lerp(HeroMid, HeroHigh, (frac - 0.25f) / 0.25f)
                         : Color.Lerp(HeroLow, HeroMid, frac / 0.25f);
                    if (frac <= 0.25f) fill *= 0.78f + 0.22f * Mathf.Sin(now * 9f); // nearly dead: it pulses
                    fill.a = 1f;
                }
                else if (ally) fill = AllyColor;
                else fill = boss ? BossColor : b.isElite ? EliteColor : EnemyColor;
                if (bar.healthColor != fill)
                {
                    bar.healthColor = fill;
                    bar.health.color = fill;
                }

                // the selection is shown by the mark above the bar and the arc at the feet: a green frame round a green
                // fill would melt into one block
                Color frame = boss ? FrameBoss : FrameDark;
                if (bar.frameColor != frame)
                {
                    bar.frameColor = frame;
                    bar.frame.color = frame;
                }

                // ---- fills: touched only when the numbers moved (a hair of difference is not visible) ----
                if (Mathf.Abs(bar.health01 - h) > 0.002f || Mathf.Abs(bar.shield01 - sh) > 0.002f)
                {
                    bar.health01 = h;
                    bar.shield01 = sh;
                    SetFill(bar.health.rectTransform, 0f, h);
                    bool hasHealth = h > 0.001f;
                    if (bar.health.enabled != hasHealth) bar.health.enabled = hasHealth;
                    SetFill(bar.shield.rectTransform, h, Mathf.Min(1f, h + sh));
                    bool hasShield = sh > 0.001f;
                    if (bar.shield.enabled != hasShield) bar.shield.enabled = hasShield;
                    // the gloss lies on the filled part only: the empty part stays dark and the level reads at once
                    SetFill(bar.gloss.rectTransform, 0f, Mathf.Min(1f, h + sh), top: 0.45f);
                    bool glossy = h + sh > 0.001f;
                    if (bar.gloss.enabled != glossy) bar.gloss.enabled = glossy;
                }
                if (Mathf.Abs(bar.trail01 - shownTrail) > 0.002f)
                {
                    bar.trail01 = shownTrail;
                    bool hasTrail = shownTrail > 0f;
                    if (bar.trail.enabled != hasTrail) bar.trail.enabled = hasTrail;
                    if (hasTrail) SetFill(bar.trail.rectTransform, 0f, Mathf.Min(1f, shownTrail));
                }
                bool hasBarrier = ba > 0.001f;
                if (bar.barrier.enabled != hasBarrier) bar.barrier.enabled = hasBarrier;
                if (hasBarrier && Mathf.Abs(bar.barrier01 - ba) > 0.002f)
                {
                    bar.barrier01 = ba;
                    SetFill(bar.barrier.rectTransform, 0f, ba, top: 0.4f);
                }

                // ---- notches: one per segment of health, never closer than ~6 px ----
                float segments = 0f;
                if (hero != null || boss)
                {
                    float step = SegmentSteps[SegmentSteps.Length - 1];
                    for (int k = 0; k < SegmentSteps.Length; k++)
                        if (total / SegmentSteps[k] <= width / 6f) { step = SegmentSteps[k]; break; }
                    segments = total / step;
                    if (segments < 1.5f) segments = 0f;
                }
                if (Mathf.Abs(bar.segments - segments) > 0.01f)
                {
                    bar.segments = segments;
                    bool hasTicks = segments > 0f;
                    if (bar.ticks.enabled != hasTicks) bar.ticks.enabled = hasTicks;
                    if (hasTicks) bar.ticks.uvRect = new Rect(0f, 0f, segments, 1f);
                }
            }

            // forget the trails of bodies that are gone or out of sight for a while
            if (now >= trailsCleanAt)
            {
                trailsCleanAt = now + 3f;
                trailsGone.Clear();
                foreach (var pair in trails)
                    if (!pair.Key || now - pair.Value.seen > 2f) trailsGone.Add(pair.Key);
                for (int i = 0; i < trailsGone.Count; i++) trails.Remove(trailsGone[i]);
            }
        }

        private static void SetFill(RectTransform rt, float from, float to, float top = 0f)
        {
            rt.anchorMin = new Vector2(from, top > 0f ? 1f - top : 0f);
            rt.anchorMax = new Vector2(Mathf.Max(from, to), 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private Image BarFill(string name, Transform parent, Color color, float cut)
        {
            Image img = UiKit.NewImage(name, parent, UiKit.Shape(cut), color);
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            return img;
        }

        private Bar AcquireBar()
        {
            if (barsUsed >= bars.Count)
            {
                const float cut = 3f, inset = 1.5f;
                float innerCut = UiKit.InnerCut(cut, inset);
                var bar = new Bar();
                bar.root = UiKit.NewRect("Bar", layer);
                bar.frame = bar.root.gameObject.AddComponent<Image>();
                UiKit.StyleOutline(bar.frame, cut);
                bar.frame.color = FrameDark;
                bar.frame.raycastTarget = false;

                RectTransform inner = UiKit.NewRect("Inner", bar.root);
                UiKit.Stretch(inner, inset, inset, inset, inset);
                bar.back = BarFill("Back", inner, new Color(0.09f, 0.1f, 0.12f, 0.95f), innerCut);
                UiKit.Stretch(bar.back.rectTransform, 0, 0, 0, 0);

                bar.trail = BarFill("Trail", inner, TrailColor, innerCut);
                bar.health = BarFill("Health", inner, HeroHigh, innerCut);
                bar.shield = BarFill("Shield", inner, ShieldColor, innerCut);

                bar.ticks = UiKit.NewRawImage("Ticks", inner, TickTexture, Color.white);
                bar.ticks.raycastTarget = false;
                UiKit.Stretch(bar.ticks.rectTransform, 0, 0, 0, 0);
                bar.ticks.enabled = false;

                // a soft light on the upper half: the fill reads as a rounded tube, not a flat strip
                bar.gloss = BarFill("Gloss", inner, new Color(1f, 1f, 1f, 0.16f), innerCut);
                SetFill(bar.gloss.rectTransform, 0f, 1f, top: 0.45f);

                bar.barrier = BarFill("Barrier", inner, BarrierColor, innerCut);
                bar.trail.enabled = false;
                bar.shield.enabled = false;
                bar.barrier.enabled = false;
                bars.Add(bar);
            }

            Bar b = bars[barsUsed++];
            if (!b.root.gameObject.activeSelf) b.root.gameObject.SetActive(true);
            return b;
        }

        // ---- misc ---------------------------------------------------------------------------------

        private void DrawDragRect()
        {
            if (!DragRect.HasValue)
            {
                if (dragBox.enabled) dragBox.enabled = false;
                // the outline child follows the parent's enabled state through activeSelf
                SetDragChildren(false);
                return;
            }
            Rect r = DragRect.Value;
            dragBox.enabled = true;
            SetDragChildren(true);
            dragBox.rectTransform.anchorMin = dragBox.rectTransform.anchorMax = Vector2.zero;
            dragBox.rectTransform.pivot = Vector2.zero;
            dragBox.rectTransform.position = new Vector3(r.xMin, r.yMin, 0f);
            dragBox.rectTransform.sizeDelta = new Vector2(r.width, r.height);
            // sizeDelta is in canvas units; the overlay canvas scales with the screen, so compensate
            float scale = layer.lossyScale.x;
            if (scale > 0.0001f) dragBox.rectTransform.sizeDelta = new Vector2(r.width / scale, r.height / scale);
        }

        private void SetDragChildren(bool on)
        {
            for (int i = 0; i < dragBox.transform.childCount; i++)
                dragBox.transform.GetChild(i).gameObject.SetActive(on);
        }

        private void HideUnused()
        {
            for (int i = barsUsed; i < bars.Count; i++)
                if (bars[i].root.gameObject.activeSelf) bars[i].root.gameObject.SetActive(false);
            for (int i = ringsUsed; i < rings.Count; i++)
                if (rings[i].enabled) rings[i].enabled = false;
            for (int i = arcsUsed; i < arcs.Count; i++)
                if (arcs[i].enabled) arcs[i].enabled = false;
            for (int i = markersUsed; i < markers.Count; i++)
                if (markers[i].enabled) markers[i].enabled = false;
            for (int i = ghostsUsed; i < ghosts.Count; i++)
                if (ghosts[i].enabled) ghosts[i].enabled = false;
            for (int i = arrowheadsUsed; i < arrowheads.Count; i++)
                if (arrowheads[i].enabled) arrowheads[i].enabled = false;
            for (int i = edgeMarksUsed; i < edgeMarks.Count; i++)
                if (edgeMarks[i].root.gameObject.activeSelf) edgeMarks[i].root.gameObject.SetActive(false);
            for (int i = pingsUsed; i < pings.Count; i++)
                if (pings[i].enabled) pings[i].enabled = false;
            for (int i = tagsUsed; i < tags.Count; i++)
                if (tags[i].text.enabled) tags[i].text.enabled = false;
        }
    }
}
