using System.Collections.Generic;
using HighCommand.Client;
using RoR2;
using UnityEngine;
using UnityEngine.UI;

namespace HighCommand.UI
{
    /// <summary>
    /// Tactical minimap: a stylised picture of the walkable area (see <see cref="MapInfo"/>), dots for heroes and
    /// enemies, and the camera's view box. Left click / drag sends the camera there.
    /// </summary>
    internal sealed class Minimap
    {
        private readonly RectTransform root;
        private readonly RectTransform mapRect;
        private readonly RawImage map;
        private readonly Image viewBox;
        private readonly List<Image> dots = new List<Image>(128);
        private readonly List<Image> bossRings = new List<Image>(8);
        private float refreshTimer;
        private int dotsUsed;

        internal Minimap(Transform parent)
        {
            RectTransform panel = UiKit.NewPanel("Minimap", parent, UiKit.PanelDark);
            root = panel;
            float size = Settings.MinimapSize.Value;
            UiKit.Place(panel, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(16f, 16f), new Vector2(size + 4f, size + 4f));

            map = UiKit.NewRawImage("Map", panel, null, Color.white);
            map.enabled = false;
            mapRect = map.rectTransform;
            UiKit.Stretch(mapRect, 2, 2, 2, 2);

            viewBox = UiKit.NewImage("ViewBox", mapRect, UiKit.Square, new Color(1f, 1f, 1f, 0.85f));
            viewBox.rectTransform.sizeDelta = new Vector2(40f, 30f);

            ClientInput.RegisterUiHitTest(Contains);
        }

        internal void Destroy()
        {
            ClientInput.UnregisterUiHitTest(Contains);
            if (root) Object.Destroy(root.gameObject);
        }

        internal bool Contains(Vector2 screenPoint) => Settings.MinimapEnabled.Value && UiKit.Contains(root, screenPoint);

        internal void SetVisible(bool visible)
        {
            if (root) root.gameObject.SetActive(visible && Settings.MinimapEnabled.Value);
        }

        /// <summary>Called after MapInfo.Rebuild() so the new stage's picture is shown.</summary>
        internal void OnMapChanged()
        {
            map.texture = MapInfo.MinimapTexture;
            map.enabled = MapInfo.MinimapTexture != null;
            boxFocus = new Vector3(float.NaN, 0f, 0f); // the new map has a new scale: place the view box again
        }

        /// <summary>Screen point inside the map -> world position (XZ valid), or null when not over the map.</summary>
        internal Vector3? ScreenToWorld(Vector2 screenPoint)
        {
            if (!MapInfo.Valid || !UiKit.Contains(root, screenPoint)) return null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(mapRect, screenPoint, null, out Vector2 local)) return null;
            Rect r = mapRect.rect;
            var uv = new Vector2((local.x - r.xMin) / r.width, (local.y - r.yMin) / r.height);
            return MapInfo.Minimap01ToWorld(uv);
        }

        internal void Tick(RtsCamera camera, float unscaledDeltaTime)
        {
            if (!root || !root.gameObject.activeInHierarchy) return;

            if (MapInfo.Valid && camera)
                PlaceViewBox(camera);

            refreshTimer -= unscaledDeltaTime;
            if (refreshTimer > 0f) return;
            refreshTimer = 0.15f;
            RefreshDots();
        }

        // the view box is written only when the camera really moved (a rect touched every frame rebuilds the canvas)
        private Vector3 boxFocus = new Vector3(float.NaN, 0f, 0f);
        private float boxDistance = float.NaN, boxYaw = float.NaN;

        private void PlaceViewBox(RtsCamera camera)
        {
            Vector3 focus = camera.Focus;
            float distance = camera.Distance, yaw = camera.Yaw;
            if (boxFocus == focus && boxDistance == distance && boxYaw == yaw) return;
            boxFocus = focus;
            boxDistance = distance;
            boxYaw = yaw;

            Rect r = mapRect.rect;
            Vector2 uv = MapInfo.WorldToMinimap01(focus);
            viewBox.rectTransform.anchorMin = viewBox.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            viewBox.rectTransform.anchoredPosition = new Vector2((uv.x - 0.5f) * r.width, (uv.y - 0.5f) * r.height);

            float groundHalfHeight = distance * 0.85f;
            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 1.78f;
            float w = groundHalfHeight * 2f * aspect;
            float h = groundHalfHeight * 2f;
            float unit = MapInfo.MetersToMinimap01(1f);
            viewBox.rectTransform.sizeDelta = new Vector2(Mathf.Max(8f, w * unit * r.width), Mathf.Max(8f, h * unit * r.height));
            viewBox.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -yaw);
        }

        private void RefreshDots()
        {
            dotsUsed = 0;
            if (!MapInfo.Valid) { HideUnused(); return; }

            var bodies = CharacterBody.readOnlyInstancesList;
            Rect r = mapRect.rect;
            for (int i = 0; i < bodies.Count; i++)
            {
                CharacterBody b = bodies[i];
                if (!b || !b.healthComponent || !b.healthComponent.alive || !b.teamComponent) continue;

                TeamIndex team = b.teamComponent.teamIndex;
                Color color;
                float size;
                if (team == TeamIndex.Player)
                {
                    bool hero = Army.HeroMarker.Of(b) != null;
                    color = hero ? new Color(0.35f, 1f, 0.45f, 1f) : new Color(0.3f, 0.65f, 1f, 0.9f);
                    size = hero ? 9f : 5f;
                }
                else if (team == TeamIndex.Monster || team == TeamIndex.Lunar || team == TeamIndex.Void)
                {
                    bool boss = b.isChampion || b.isBoss;
                    color = boss ? new Color(1f, 0.55f, 0.1f, 1f) : new Color(1f, 0.25f, 0.2f, 0.95f);
                    size = boss ? 10f : 5f;
                }
                else continue;

                Vector2 uv = MapInfo.WorldToMinimap01(b.corePosition);
                if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f) continue;

                Image dot = AcquireDot();
                dot.color = color;
                dot.rectTransform.sizeDelta = new Vector2(size, size);
                dot.rectTransform.anchoredPosition = new Vector2((uv.x - 0.5f) * r.width, (uv.y - 0.5f) * r.height);
            }
            HideUnused();
        }

        private Image AcquireDot()
        {
            if (dotsUsed >= dots.Count)
            {
                Image dot = UiKit.NewImage("Dot", mapRect, UiKit.Circle, Color.white);
                dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                dots.Add(dot);
            }
            Image d = dots[dotsUsed++];
            if (!d.enabled) d.enabled = true;
            return d;
        }

        private void HideUnused()
        {
            for (int i = dotsUsed; i < dots.Count; i++)
                if (dots[i].enabled) dots[i].enabled = false;
        }
    }
}
