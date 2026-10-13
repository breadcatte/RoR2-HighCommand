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
            internal Image back, health, shield, barrier, frame;

            // what was written last: the bar is touched only when its picture really changes
            internal Vector2 position;
            internal float width, height, health01 = -1f, shield01 = -1f, barrier01 = -1f;
            internal bool selected;
        }

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
            linesUsed = 0;
            flowsUsed = 0;
            arrowsUsed = 0;
            pingsUsed = 0;
            tagsUsed = 0;

            if (camera && camera.SceneCam)
            {
                DrawPriceTags(camera);
                DrawRings(selection, heroes);
                DrawBars(camera, selection);
                DrawOrders(selection, cursor);
                DrawPings();
                DrawPopups();
            }

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

        private readonly List<Image> lines = new List<Image>(32);
        private readonly List<Image> pings = new List<Image>(16);
        private int linesUsed, pingsUsed;
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

        private void DrawOrders(Selection selection, OrderController.CursorInfo cursor)
        {
            var sel = selection.Heroes;
            int previews = 0;
            for (int i = 0; i < sel.Count; i++)
            {
                Hero hero = sel[i];
                CharacterBody body = hero.Body;
                if (!body) continue;

                // the replicated order of the hero: the same on the host and on every client
                Vector3 from = body.footPosition + Vector3.up * 0.2f;
                DrawOrderLeg(from, (Orders.OrderKind)hero.NetOrderKind, hero.NetOrderPoint, out Vector3 last);

                // the rest of the queue (Shift orders), dimmer, leg after leg. The queue lives in the server's brain:
                // the mode is single-player, so the host reads it directly.
                Orders.HeroBrain brain = hero.Brain;
                if (brain != null)
                {
                    IReadOnlyList<Orders.Order> queue = brain.Queue;
                    for (int q = 0; q < queue.Count && q < 8; q++)
                    {
                        Orders.Order o = queue[q];
                        if (o == null) continue;
                        DrawOrderLeg(last, o.Kind, o.Destination(), out last, 0.55f);
                    }
                }

                // where a click would send this hero: from its feet, or from the end of its queue with Shift held
                if (cursor.Preview && cursor.HasPoint && cursor.Runner == null && previews < PreviewLimit)
                {
                    previews++;
                    DrawPreview(cursor.Queued ? last : from, cursor.Point, cursor.Color);
                }
            }

            // an object under the pointer: one hero would go, the line starts at it (with Shift: where its trips end)
            Hero runner = cursor.Runner;
            if (cursor.Preview && cursor.HasPoint && runner != null && runner.Body)
            {
                Vector3 start = runner.Body.footPosition + Vector3.up * 0.2f;
                Orders.HeroBrain brain = runner.Brain;
                if (cursor.Queued && brain != null) start = brain.ErrandsEnd(start);
                DrawPreview(start, cursor.Point, cursor.Color);
            }
        }

        // the preview of an order: a soft glow, dashes that flow from the hero to the target, an arrowhead, a ring
        private readonly List<RawImage> flows = new List<RawImage>(8);
        private readonly List<Image> arrows = new List<Image>(8);
        private int flowsUsed, arrowsUsed;

        private const float FlowSpeed = 46f;   // layer units per second
        private const float FlowPeriodUnits = 24f;

        private void DrawPreview(Vector3 from, Vector3 to, Color color)
        {
            if (!WorldPicker.Project(from, 0.1f, out Vector2 a, out float _)) return;
            if (!WorldPicker.Project(to, 0.1f, out Vector2 b, out float _)) return;

            float scale = Mathf.Max(0.0001f, layer.lossyScale.x);
            Vector2 d = b - a;
            float length = d.magnitude;
            if (length < 8f * scale) return;
            Vector2 dir = d / length;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;

            // the line stops short of the arrowhead
            float head = 10f * scale;
            Vector2 end = b - dir * head;
            float lineLength = Mathf.Max(0f, (end - a).magnitude);

            Color glow = color;
            glow.a = 0.22f;
            DrawLine(a, end, glow, 7f);

            if (flowsUsed >= flows.Count)
            {
                RawImage raw = UiKit.NewRawImage("Flow", layer, UiKit.FlowTexture, Color.white);
                raw.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                flows.Add(raw);
            }
            RawImage flow = flows[flowsUsed++];
            if (!flow.enabled) flow.enabled = true;
            Color line = color;
            line.a = 0.9f;
            flow.color = line;
            float units = lineLength / scale;
            flow.rectTransform.sizeDelta = new Vector2(units, 7f);
            flow.rectTransform.position = new Vector3((a.x + end.x) * 0.5f, (a.y + end.y) * 0.5f, 0f);
            flow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
            // the dashes slide towards the target; the pattern is anchored at the target so it never jitters at the end
            float phase = -Time.unscaledTime * FlowSpeed / FlowPeriodUnits;
            flow.uvRect = new Rect(phase - units / FlowPeriodUnits, 0f, units / FlowPeriodUnits, 1f);

            // arrowhead: the marker sprite points down, so it is turned by the direction + 90 degrees
            if (arrowsUsed >= arrows.Count)
            {
                Image arrow = UiKit.NewImage("Arrow", layer, UiKit.Marker, Color.white);
                arrow.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                arrows.Add(arrow);
            }
            Image head2 = arrows[arrowsUsed++];
            if (!head2.enabled) head2.enabled = true;
            Color headColor = color;
            headColor.a = 1f;
            head2.color = headColor;
            head2.rectTransform.sizeDelta = new Vector2(17f, 13f);
            head2.rectTransform.position = new Vector3(b.x - dir.x * head * 0.5f, b.y - dir.y * head * 0.5f, 0f);
            head2.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle + 90f);

            // the ring on the ground, breathing a little
            Image marker = AcquireRing();
            Color m = color;
            m.a = 0.85f;
            marker.color = m;
            float pulse = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 5f);
            marker.rectTransform.sizeDelta = new Vector2(26f * pulse, 14f * pulse);
            marker.rectTransform.position = new Vector3(b.x, b.y, 0f);
        }

        private void DrawOrderLeg(Vector3 from, Orders.OrderKind kind, Vector3 point, out Vector3 end, float alpha = 1f)
        {
            end = from;
            if (kind == Orders.OrderKind.None) return;

            Vector3? destination = null;
            Color color = new Color(0.4f, 1f, 0.5f, 0.7f);
            switch (kind)
            {
                case Orders.OrderKind.Move: destination = point; break;
                case Orders.OrderKind.AttackMove: destination = point; color = new Color(1f, 0.55f, 0.3f, 0.7f); break;
                case Orders.OrderKind.Retreat: destination = point; color = new Color(0.5f, 0.8f, 1f, 0.7f); break;
                case Orders.OrderKind.Patrol: destination = point; color = new Color(1f, 0.8f, 0.3f, 0.7f); break;
                case Orders.OrderKind.Attack: destination = point; color = new Color(1f, 0.3f, 0.25f, 0.8f); break;
                case Orders.OrderKind.Follow: destination = point; break;
                case Orders.OrderKind.Interact: destination = point; color = new Color(1f, 0.9f, 0.3f, 0.8f); break;
            }
            if (!destination.HasValue || destination.Value == Vector3.zero) return;
            end = destination.Value;
            color.a *= alpha;

            if (!WorldPicker.Project(from, 0.1f, out Vector2 a, out float _)) return;
            if (!WorldPicker.Project(end, 0.1f, out Vector2 b, out float _)) return;
            DrawLine(a, b, color, 2f);

            // small marker at the destination
            Image marker = AcquireRing();
            marker.color = color;
            float inv = 1f / Mathf.Max(0.0001f, layer.lossyScale.x);
            marker.rectTransform.sizeDelta = new Vector2(20f, 12f);
            marker.rectTransform.position = new Vector3(b.x, b.y, 0f);
        }

        private void DrawLine(Vector2 a, Vector2 b, Color color, float thickness)
        {
            if (linesUsed >= lines.Count)
            {
                Image img = UiKit.NewImage("Line", layer, UiKit.SoftLine, Color.white);
                img.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                lines.Add(img);
            }
            Image line = lines[linesUsed++];
            if (!line.enabled) line.enabled = true;
            float scale = Mathf.Max(0.0001f, layer.lossyScale.x);
            Vector2 d = b - a;
            float length = d.magnitude;
            line.color = color;
            // the soft sprite fades towards its edges: a thin slanted line has no stair steps
            line.rectTransform.sizeDelta = new Vector2(length / scale, thickness + 2.5f);
            line.rectTransform.position = new Vector3((a.x + b.x) * 0.5f, (a.y + b.y) * 0.5f, 0f);
            line.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
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
        internal static int LastBars, LastRings;

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

        private void DrawBars(RtsCamera camera, Selection selection)
        {
            Camera cam = camera.SceneCam;
            Vector3 camPos = cam.transform.position;
            float maxDistance = Mathf.Max(70f, camera.Distance * 2.4f);
            float maxDistanceSqr = maxDistance * maxDistance;
            var bodies = CharacterBody.readOnlyInstancesList;

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

                float radius = b.radius;
                Vector3 anchor = core + Vector3.up * (Mathf.Max(1.2f, radius * 1.8f) + 0.6f);
                if (!WorldPicker.ProjectPoint(cam, anchor, out Vector2 s)) continue;
                if (!WorldPicker.IsOnScreen(s)) continue;

                Hero hero = ally ? HeroMarker.Of(b) : null;
                bool boss = b.isChampion || b.isBoss;

                // bar width: heroes and bosses wider, grows slightly with body size
                float width = hero != null ? 74f : boss ? 110f : 40f + Mathf.Min(24f, radius * 10f);
                float height = hero != null ? 8f : boss ? 9f : 5f;

                Bar bar = AcquireBar();
                float total = Mathf.Max(1f, hc.fullHealth + hc.fullShield);
                float h = Mathf.Clamp01(hc.health / total);
                float sh = Mathf.Clamp01(hc.shield / total);
                float ba = Mathf.Clamp01(hc.barrier / total);

                if (bar.width != width || bar.height != height)
                {
                    bar.width = width;
                    bar.height = height;
                    bar.root.sizeDelta = new Vector2(width, height);
                }
                if (bar.position != s)
                {
                    bar.position = s;
                    bar.root.position = new Vector3(s.x, s.y, 0f);
                }

                Color healthColor = ally ? new Color(0.35f, 0.9f, 0.4f) : new Color(0.95f, 0.28f, 0.25f);
                if (!ally && b.isElite) healthColor = new Color(1f, 0.55f, 0.15f);
                bar.health.color = healthColor;

                // the fills move only when the numbers did (a hair of difference is not visible)
                if (Mathf.Abs(bar.health01 - h) > 0.002f || Mathf.Abs(bar.shield01 - sh) > 0.002f)
                {
                    bar.health01 = h;
                    bar.shield01 = sh;
                    SetFill(bar.health.rectTransform, 0f, h);
                    SetFill(bar.shield.rectTransform, h, Mathf.Min(1f, h + sh));
                    bool hasShield = sh > 0.001f;
                    if (bar.shield.enabled != hasShield) bar.shield.enabled = hasShield;
                }
                bool hasBarrier = ba > 0.001f;
                if (bar.barrier.enabled != hasBarrier) bar.barrier.enabled = hasBarrier;
                if (hasBarrier && Mathf.Abs(bar.barrier01 - ba) > 0.002f)
                {
                    bar.barrier01 = ba;
                    SetFill(bar.barrier.rectTransform, 0f, ba);
                }

                bool selected = hero != null && selection.Contains(hero);
                if (bar.selected != selected)
                {
                    bar.selected = selected;
                    bar.frame.color = selected ? new Color(0.5f, 1f, 0.55f, 1f) : new Color(0f, 0f, 0f, 0.9f);
                }
            }
        }

        private static void SetFill(RectTransform rt, float from, float to)
        {
            rt.anchorMin = new Vector2(from, 0f);
            rt.anchorMax = new Vector2(Mathf.Max(from, to), 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private Bar AcquireBar()
        {
            if (barsUsed >= bars.Count)
            {
                var bar = new Bar();
                bar.root = UiKit.NewRect("Bar", layer);
                bar.frame = bar.root.gameObject.AddComponent<Image>();
                bar.frame.sprite = UiKit.White;
                bar.frame.color = new Color(0f, 0f, 0f, 0.9f);
                bar.frame.raycastTarget = false;

                RectTransform inner = UiKit.NewRect("Inner", bar.root);
                UiKit.Stretch(inner, 1, 1, 1, 1);
                bar.back = inner.gameObject.AddComponent<Image>();
                bar.back.sprite = UiKit.White;
                bar.back.color = new Color(0.12f, 0.12f, 0.14f, 0.95f);
                bar.back.raycastTarget = false;

                bar.health = UiKit.NewImage("Health", inner, UiKit.White, Color.green);
                bar.shield = UiKit.NewImage("Shield", inner, UiKit.White, new Color(0.35f, 0.65f, 1f));
                bar.barrier = UiKit.NewImage("Barrier", inner, UiKit.White, new Color(1f, 0.95f, 0.6f, 0.95f));
                // barrier: a thin line along the top
                bar.barrier.rectTransform.pivot = new Vector2(0f, 1f);
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
            for (int i = linesUsed; i < lines.Count; i++)
                if (lines[i].enabled) lines[i].enabled = false;
            for (int i = flowsUsed; i < flows.Count; i++)
                if (flows[i].enabled) flows[i].enabled = false;
            for (int i = arrowsUsed; i < arrows.Count; i++)
                if (arrows[i].enabled) arrows[i].enabled = false;
            for (int i = pingsUsed; i < pings.Count; i++)
                if (pings[i].enabled) pings[i].enabled = false;
            for (int i = tagsUsed; i < tags.Count; i++)
                if (tags[i].text.enabled) tags[i].text.enabled = false;
        }
    }
}
