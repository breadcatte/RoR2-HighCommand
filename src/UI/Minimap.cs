using System.Collections.Generic;
using HighCommand.Client;
using RoR2;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HighCommand.UI
{
    /// <summary>
    /// Tactical minimap: a stylised picture of the walkable area (see <see cref="MapInfo"/>) in a cut-corner frame, dots
    /// for heroes and enemies, and the camera's view finder. Left click / drag sends the camera there.
    /// </summary>
    internal sealed class Minimap
    {
        private static readonly Color ViewEdge = new Color(1f, 0.96f, 0.82f, 0.55f);
        private static readonly Color ViewCorner = new Color(1f, 0.86f, 0.5f, 1f);

        private readonly RectTransform root;
        private readonly RectTransform mapRect;
        private readonly RawImage map;
        private readonly RectTransform viewBox;
        private readonly TextMeshProUGUI nameText;
        private readonly List<Image> dots = new List<Image>(128);
        private readonly List<Image> selectionRings = new List<Image>(16);
        private float refreshTimer;
        private int dotsUsed, ringsUsed, marksUsed;
        private readonly RectTransform markLayer;
        private readonly List<Image> marks = new List<Image>(64);

        internal Minimap(Transform parent)
        {
            RectTransform panel = UiKit.NewPanel("Minimap", parent, UiKit.PanelDark);
            root = panel;
            float size = Settings.MinimapSize.Value;
            UiKit.Place(panel, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(16f, 16f), new Vector2(size + 4f, size + 4f));

            // everything on the map is clipped to the cut-corner shape of the frame
            Image clip = UiKit.NewImage("Clip", panel, UiKit.Shape(UiKit.InnerCut(UiKit.PanelCut, 2f)), Color.white);
            clip.type = Image.Type.Sliced;
            UiKit.Stretch(clip.rectTransform, 2, 2, 2, 2);
            var mask = clip.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            map = UiKit.NewRawImage("Map", clip.rectTransform, null, Color.white);
            map.enabled = false;
            mapRect = map.rectTransform;
            UiKit.Stretch(mapRect, 0, 0, 0, 0);

            // objects found on the map (chests, shrines, the teleporter ...): under the view finder and the unit dots
            markLayer = UiKit.NewRect("Marks", mapRect);
            UiKit.Stretch(markLayer, 0, 0, 0, 0);

            // the camera's view finder: a faint tint, hairline edges and bright corner brackets
            viewBox = UiKit.NewRect("ViewBox", mapRect);
            viewBox.sizeDelta = new Vector2(40f, 30f);
            Image tint = UiKit.NewImage("Tint", viewBox, UiKit.White, new Color(1f, 0.96f, 0.82f, 0.08f));
            UiKit.Stretch(tint.rectTransform, 0, 0, 0, 0);
            AddEdges(viewBox);

            nameText = UiKit.NewText("Name", clip.rectTransform, "", 12f, UiKit.Text, TextAlignmentOptions.TopLeft);
            UiKit.Place(nameText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -5f), new Vector2(Mathf.Max(60f, size - 40f), 16f));
            nameText.enableAutoSizing = true;
            nameText.fontSizeMin = 8f;
            nameText.fontSizeMax = 12f;
            nameText.color = new Color(0.82f, 0.9f, 0.96f, 0.85f);

            TextMeshProUGUI north = UiKit.NewText("North", clip.rectTransform, "N", 11f, new Color(0.82f, 0.9f, 0.96f, 0.6f), TextAlignmentOptions.TopRight);
            UiKit.Place(north.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-6f, -5f), new Vector2(20f, 14f));

            ClientInput.RegisterUiHitTest(Contains);
        }

        private static void AddEdges(RectTransform box)
        {
            // hairlines along the four sides
            Line(box, "EdgeL", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(1.5f, 0f), ViewEdge);
            Line(box, "EdgeR", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(1.5f, 0f), ViewEdge);
            Line(box, "EdgeB", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 1.5f), ViewEdge);
            Line(box, "EdgeT", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 1.5f), ViewEdge);

            // brackets in the four corners (a horizontal and a vertical stroke each)
            const float Len = 10f, Thick = 2.5f;
            for (int cx = 0; cx < 2; cx++)
                for (int cy = 0; cy < 2; cy++)
                {
                    var corner = new Vector2(cx, cy);
                    Line(box, "CornerH", corner, corner, corner, new Vector2(Len, Thick), ViewCorner);
                    Line(box, "CornerV", corner, corner, corner, new Vector2(Thick, Len), ViewCorner);
                }
        }

        private static void Line(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size, Color color)
        {
            Image img = UiKit.NewImage(name, parent, UiKit.White, color);
            RectTransform rt = img.rectTransform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
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

            SceneInfo info = SceneInfo.instance;
            SceneDef scene = info && info.sceneDef ? info.sceneDef : null;
            UiKit.SetText(nameText, scene ? Language.GetString(scene.nameToken) : "");
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

        internal void Tick(RtsCamera camera, Selection selection, float unscaledDeltaTime)
        {
            if (!root || !root.gameObject.activeInHierarchy) return;

            if (MapInfo.Valid && camera)
                PlaceViewBox(camera);

            refreshTimer -= unscaledDeltaTime;
            if (refreshTimer > 0f) return;
            refreshTimer = 0.15f;
            RefreshDots(selection);
        }

        // the view box is written only when the camera really moved (a rect touched every frame rebuilds the canvas)
        private Vector3 boxFocus = new Vector3(float.NaN, 0f, 0f);
        private float boxDistance = float.NaN, boxYaw = float.NaN, boxPitch = float.NaN;

        private void PlaceViewBox(RtsCamera camera)
        {
            Vector3 focus = camera.Focus;
            float distance = camera.Distance, yaw = camera.Yaw, pitch = camera.Pitch;
            if (boxFocus == focus && boxDistance == distance && boxYaw == yaw && boxPitch == pitch) return;
            boxFocus = focus;
            boxDistance = distance;
            boxYaw = yaw;
            boxPitch = pitch;

            Rect r = mapRect.rect;
            Vector2 uv = MapInfo.WorldToMinimap01(focus);
            viewBox.anchorMin = viewBox.anchorMax = new Vector2(0.5f, 0.5f);
            viewBox.anchoredPosition = new Vector2((uv.x - 0.5f) * r.width, (uv.y - 0.5f) * r.height);

            // the depth of the ground in view grows as the camera looks flatter (0.85 is right at the default 58 degrees)
            float tilt = Mathf.Sin(Mathf.Clamp(pitch, 20f, 89f) * Mathf.Deg2Rad);
            float groundHalfHeight = distance * 0.85f * (Mathf.Sin(58f * Mathf.Deg2Rad) / tilt);
            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 1.78f;
            float w = groundHalfHeight * 2f * aspect;
            float h = groundHalfHeight * 2f;
            float unit = MapInfo.MetersToMinimap01(1f);
            viewBox.sizeDelta = new Vector2(Mathf.Max(24f, w * unit * r.width), Mathf.Max(20f, h * unit * r.height));
            viewBox.localRotation = Quaternion.Euler(0f, 0f, -yaw);
        }

        private static readonly Color MarkChest = new Color(1f, 0.82f, 0.3f, 1f);
        private static readonly Color MarkChestPoor = new Color(0.62f, 0.5f, 0.3f, 0.9f);
        private static readonly Color MarkShop = new Color(1f, 0.6f, 0.25f, 1f);
        private static readonly Color MarkLunar = new Color(0.55f, 0.75f, 1f, 1f);
        private static readonly Color MarkShrine = new Color(0.8f, 0.55f, 1f, 1f);
        private static readonly Color MarkDrone = new Color(0.35f, 0.95f, 0.95f, 1f);
        private static readonly Color MarkPrinter = new Color(0.92f, 0.92f, 0.92f, 1f);
        private static readonly Color MarkPortal = new Color(0.75f, 0.5f, 1f, 1f);
        private static readonly Color MarkTeleporter = new Color(1f, 0.45f, 0.35f, 1f);

        /// <summary>Marks for the objects the army has found and not used up yet (see <see cref="Scouting"/>).</summary>
        private void RefreshMarks(Rect r)
        {
            List<IInteractable> all = InterfaceInstanceTracker<IInteractable>.GetInstancesList();
            for (int i = 0; i < all.Count; i++)
            {
                var mb = all[i] as MonoBehaviour;
                if (!mb || !mb.isActiveAndEnabled) continue;
                GameObject thing = mb.gameObject;
                Army.InteractKind kind = Army.Interactables.KindOf(thing);

                Sprite sprite;
                Color color;
                float size;
                bool diamond = false;
                switch (kind)
                {
                    case Army.InteractKind.Chest:
                        sprite = UiKit.White; diamond = true; size = 7f;
                        color = Army.Interactables.Affordable(thing) ? MarkChest : MarkChestPoor;
                        break;
                    case Army.InteractKind.Shop: sprite = UiKit.White; diamond = true; size = 7f; color = MarkShop; break;
                    case Army.InteractKind.Lunar: sprite = UiKit.White; diamond = true; size = 7f; color = MarkLunar; break;
                    case Army.InteractKind.Shrine: sprite = UiKit.Dot; size = 8f; color = MarkShrine; break;
                    case Army.InteractKind.Drone: sprite = UiKit.Dot; size = 8f; color = MarkDrone; break;
                    case Army.InteractKind.Printer: sprite = UiKit.Dot; size = 8f; color = MarkPrinter; break;
                    case Army.InteractKind.Portal: sprite = UiKit.Ring; size = 13f; color = MarkPortal; break;
                    case Army.InteractKind.Teleporter: sprite = UiKit.Ring; size = 17f; color = MarkTeleporter; break;
                    default: continue; // barrels and the rest: not worth a mark
                }
                if (kind != Army.InteractKind.Teleporter && Army.Interactables.IsUsed(thing)) continue;
                if (!Scouting.IsKnown(thing)) continue;

                Vector2 uv = MapInfo.WorldToMinimap01(thing.transform.position);
                if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f) continue;

                Image mark = AcquireMark();
                if (mark.sprite != sprite) mark.sprite = sprite;
                mark.color = color;
                mark.rectTransform.sizeDelta = new Vector2(size, size);
                mark.rectTransform.localRotation = diamond ? Quaternion.Euler(0f, 0f, 45f) : Quaternion.identity;
                mark.rectTransform.anchoredPosition = new Vector2((uv.x - 0.5f) * r.width, (uv.y - 0.5f) * r.height);
            }
        }

        private Image AcquireMark()
        {
            if (marksUsed >= marks.Count)
            {
                Image mark = UiKit.NewImage("Mark", markLayer, UiKit.Dot, Color.white);
                mark.rectTransform.anchorMin = mark.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                mark.raycastTarget = false;
                marks.Add(mark);
            }
            Image m = marks[marksUsed++];
            if (!m.enabled) m.enabled = true;
            return m;
        }

        private void RefreshDots(Selection selection)
        {
            dotsUsed = 0;
            ringsUsed = 0;
            marksUsed = 0;
            if (!MapInfo.Valid) { HideUnused(); return; }

            var bodies = CharacterBody.readOnlyInstancesList;
            Rect r = mapRect.rect;
            RefreshMarks(r);
            for (int i = 0; i < bodies.Count; i++)
            {
                CharacterBody b = bodies[i];
                if (!b || !b.healthComponent || !b.healthComponent.alive || !b.teamComponent) continue;

                TeamIndex team = b.teamComponent.teamIndex;
                Color color;
                float size;
                bool selected = false;
                if (team == TeamIndex.Player)
                {
                    Army.Hero hero = Army.HeroMarker.Of(b);
                    bool isHero = hero != null;
                    selected = isHero && selection != null && selection.Contains(hero);
                    color = isHero ? new Color(0.4f, 1f, 0.5f, 1f) : new Color(0.35f, 0.7f, 1f, 0.95f);
                    size = isHero ? 11f : 6f;
                }
                else if (team == TeamIndex.Monster || team == TeamIndex.Lunar || team == TeamIndex.Void)
                {
                    bool boss = b.isChampion || b.isBoss;
                    color = boss ? new Color(1f, 0.6f, 0.15f, 1f) : new Color(1f, 0.28f, 0.24f, 0.95f);
                    size = boss ? 13f : 6f;
                }
                else continue;

                Vector2 uv = MapInfo.WorldToMinimap01(b.corePosition);
                if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f) continue;
                Vector2 at = new Vector2((uv.x - 0.5f) * r.width, (uv.y - 0.5f) * r.height);

                if (selected)
                {
                    Image ring = AcquireRing();
                    ring.rectTransform.sizeDelta = new Vector2(19f, 19f);
                    ring.rectTransform.anchoredPosition = at;
                }

                Image dot = AcquireDot();
                dot.color = color;
                dot.rectTransform.sizeDelta = new Vector2(size, size);
                dot.rectTransform.anchoredPosition = at;
            }
            HideUnused();
        }

        private Image AcquireDot()
        {
            if (dotsUsed >= dots.Count)
            {
                Image dot = UiKit.NewImage("Dot", mapRect, UiKit.Dot, Color.white);
                dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                dots.Add(dot);
            }
            Image d = dots[dotsUsed++];
            if (!d.enabled) d.enabled = true;
            return d;
        }

        private Image AcquireRing()
        {
            if (ringsUsed >= selectionRings.Count)
            {
                Image ring = UiKit.NewImage("Selected", mapRect, UiKit.Ring, new Color(1f, 1f, 1f, 0.95f));
                ring.rectTransform.anchorMin = ring.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                selectionRings.Add(ring);
            }
            Image g = selectionRings[ringsUsed++];
            if (!g.enabled) g.enabled = true;
            return g;
        }

        private void HideUnused()
        {
            for (int i = dotsUsed; i < dots.Count; i++)
                if (dots[i].enabled) dots[i].enabled = false;
            for (int i = ringsUsed; i < selectionRings.Count; i++)
                if (selectionRings[i].enabled) selectionRings[i].enabled = false;
            for (int i = marksUsed; i < marks.Count; i++)
                if (marks[i].enabled) marks[i].enabled = false;
        }
    }
}
