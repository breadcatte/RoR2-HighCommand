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
        }

        private readonly RectTransform layer;
        private readonly List<Bar> bars = new List<Bar>(128);
        private readonly List<Image> rings = new List<Image>(32);
        private readonly Image dragBox;
        private int barsUsed, ringsUsed;

        /// <summary>Hero under the mouse / hostile under the mouse, set by the input handler each frame.</summary>
        internal Hero HoveredHero;
        internal CharacterBody HoveredEnemy;

        internal Rect? DragRect;

        internal WorldOverlay(Transform parent)
        {
            layer = UiKit.NewRect("WorldOverlay", parent);
            UiKit.Stretch(layer);

            dragBox = UiKit.NewImage("DragBox", layer, UiKit.White, new Color(0.4f, 1f, 0.5f, 0.12f));
            dragBox.enabled = false;
            Image outline = UiKit.NewImage("Outline", dragBox.transform, UiKit.Square, new Color(0.5f, 1f, 0.6f, 0.9f));
            UiKit.Stretch(outline.rectTransform);
        }

        internal void Destroy()
        {
            if (layer) Object.Destroy(layer.gameObject);
        }

        /// <summary>Redraws everything. Call after the camera has been positioned for this frame.</summary>
        internal void Draw(RtsCamera camera, Selection selection, IReadOnlyList<Hero> heroes)
        {
            barsUsed = 0;
            ringsUsed = 0;

            if (camera && camera.SceneCam)
            {
                DrawRings(selection);
                DrawBars(camera, selection);
            }

            LastBars = barsUsed;
            LastRings = ringsUsed;
            HideUnused();
            DrawDragRect();
        }

        // diagnostics (hc_overlay)
        internal static int LastBars, LastRings;

        // ---- rings -------------------------------------------------------------------------------

        private void DrawRings(Selection selection)
        {
            var sel = selection.Heroes;
            for (int i = 0; i < sel.Count; i++)
                DrawRing(sel[i].Body, new Color(0.45f, 1f, 0.5f, 0.95f), 1.35f);

            if (HoveredEnemy) DrawRing(HoveredEnemy, new Color(1f, 0.35f, 0.3f, 0.9f), 1.25f);
            if (HoveredHero != null && !selection.Contains(HoveredHero)) DrawRing(HoveredHero.Body, new Color(1f, 1f, 1f, 0.7f), 1.25f);
        }

        private void DrawRing(CharacterBody body, Color color, float scale)
        {
            if (!body || !body.healthComponent || !body.healthComponent.alive) return;

            Vector3 foot = body.footPosition;
            float radius = Mathf.Max(0.8f, body.radius) * scale;
            if (!WorldPicker.Project(foot, radius, out Vector2 s, out float px)) return;
            if (!WorldPicker.IsOnScreen(s, px)) return;

            Image ring = AcquireRing();
            ring.color = color;
            float pitch = Settings.CameraPitch.Value * Mathf.Deg2Rad;
            float inv = 1f / Mathf.Max(0.0001f, layer.lossyScale.x); // screen pixels -> canvas units
            float w = Mathf.Max(14f, px * 2f) * inv;
            ring.rectTransform.sizeDelta = new Vector2(w, w * Mathf.Clamp(Mathf.Sin(pitch), 0.35f, 1f));
            ring.rectTransform.position = new Vector3(s.x, s.y, 0f);
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
            var bodies = CharacterBody.readOnlyInstancesList;

            for (int i = 0; i < bodies.Count; i++)
            {
                CharacterBody b = bodies[i];
                if (!b || !b.healthComponent || !b.healthComponent.alive || !b.teamComponent) continue;

                TeamIndex team = b.teamComponent.teamIndex;
                bool ally = team == TeamIndex.Player;
                bool enemy = WorldPicker.IsHostileToPlayer(team);
                if (!ally && !enemy) continue;

                HealthComponent hc = b.healthComponent;
                if ((camPos - b.transform.position).sqrMagnitude > maxDistance * maxDistance) continue;

                float topOffset = Mathf.Max(1.2f, b.radius * 1.8f);
                Vector3 anchor = b.corePosition + Vector3.up * (topOffset + 0.6f);
                if (!WorldPicker.Project(anchor, 0.5f, out Vector2 s, out float _)) continue;
                if (!WorldPicker.IsOnScreen(s)) continue;

                bool hero = HeroMarker.Of(b) != null;
                bool boss = b.isChampion || b.isBoss;

                // bar width: heroes and bosses wider, grows slightly with body size
                float width = hero ? 74f : boss ? 110f : 40f + Mathf.Min(24f, b.radius * 10f);
                float height = hero ? 8f : boss ? 9f : 5f;

                Bar bar = AcquireBar();
                float total = Mathf.Max(1f, hc.fullHealth + hc.fullShield);
                float h = Mathf.Clamp01(hc.health / total);
                float sh = Mathf.Clamp01(hc.shield / total);
                float ba = Mathf.Clamp01(hc.barrier / total);

                bar.root.sizeDelta = new Vector2(width, height);
                bar.root.position = new Vector3(s.x, s.y, 0f);

                Color healthColor = ally ? new Color(0.35f, 0.9f, 0.4f) : new Color(0.95f, 0.28f, 0.25f);
                if (!ally && b.isElite) healthColor = new Color(1f, 0.55f, 0.15f);
                bar.health.color = healthColor;

                SetFill(bar.health.rectTransform, 0f, h);
                SetFill(bar.shield.rectTransform, h, Mathf.Min(1f, h + sh));
                bar.shield.enabled = sh > 0.001f;
                bar.barrier.enabled = ba > 0.001f;
                if (ba > 0.001f) SetFill(bar.barrier.rectTransform, 0f, ba);

                bool selected = hero && selection.Contains(HeroMarker.Of(b));
                bar.frame.color = selected ? new Color(0.5f, 1f, 0.55f, 1f) : new Color(0f, 0f, 0f, 0.9f);
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
        }
    }
}
