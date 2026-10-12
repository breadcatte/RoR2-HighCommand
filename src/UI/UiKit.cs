using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HighCommand.UI
{
    /// <summary>
    /// Small helpers for building the mode's UI in code. Everything is plain uGUI + TextMeshPro on one
    /// screen-space-overlay canvas; hit testing is done by the mode itself (see ClientInput), so the stock
    /// event system and the HUD never see these elements.
    /// </summary>
    internal static class UiKit
    {
        // RoR2 look: dark blue-grey panels, light text, orange accents.
        internal static readonly Color PanelColor = new Color(0.07f, 0.09f, 0.12f, 0.82f);
        internal static readonly Color PanelDark = new Color(0.04f, 0.05f, 0.07f, 0.92f);
        internal static readonly Color Border = new Color(0.55f, 0.62f, 0.72f, 0.55f);
        internal static readonly Color Accent = new Color(1f, 0.64f, 0.18f, 1f);
        internal static readonly Color Text = new Color(0.9f, 0.93f, 0.97f, 1f);
        internal static readonly Color TextDim = new Color(0.65f, 0.7f, 0.78f, 1f);
        internal static readonly Color Good = new Color(0.4f, 0.9f, 0.45f, 1f);
        internal static readonly Color Bad = new Color(0.95f, 0.3f, 0.28f, 1f);
        internal static readonly Color Gold = new Color(1f, 0.85f, 0.25f, 1f);

        private static TMP_FontAsset font;
        private static bool fontSearched;

        private static Sprite white;
        private static Sprite circle;
        private static Sprite ring;
        private static Sprite square;

        /// <summary>The game's own TextMeshPro font (falls back to the first one found).</summary>
        internal static TMP_FontAsset Font
        {
            get
            {
                if (font || fontSearched) return font;
                fontSearched = true;

                TMP_FontAsset[] all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                string[] preferred = { "tmpBombDropshadow", "tmpBombDropShadow", "Bombardier", "tmpRiskOfRainBombardier", "Noto" };
                foreach (string want in preferred)
                {
                    foreach (TMP_FontAsset f in all)
                        if (f && f.name.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0) { font = f; break; }
                    if (font) break;
                }
                if (!font && all.Length > 0) font = all[0];
                if (font) Plugin.Log.LogInfo($"UiKit: font '{font.name}' (of {all.Length} available).");
                else Plugin.Log.LogWarning("UiKit: no TextMeshPro font found; text will not render.");
                return font;
            }
        }

        // ---- procedural sprites -------------------------------------------------------------------

        internal static Sprite White => white ? white : (white = MakeSprite(4, (x, y, s) => 1f));

        internal static Sprite Circle => circle ? circle : (circle = MakeSprite(64, (x, y, s) =>
        {
            float d = Mathf.Sqrt(x * x + y * y);
            return Mathf.Clamp01((1f - d) * (s * 0.5f));
        }));

        internal static Sprite Ring => ring ? ring : (ring = MakeSprite(96, (x, y, s) =>
        {
            float d = Mathf.Sqrt(x * x + y * y);
            float outer = Mathf.Clamp01((1f - d) * (s * 0.5f));
            float inner = Mathf.Clamp01((d - 0.78f) * (s * 0.5f));
            return outer * inner;
        }));

        internal static Sprite Square => square ? square : (square = MakeSprite(32, (x, y, s) =>
        {
            float m = Mathf.Max(Mathf.Abs(x), Mathf.Abs(y));
            float outer = Mathf.Clamp01((1f - m) * (s * 0.5f));
            float inner = Mathf.Clamp01((m - 0.88f) * (s * 0.5f));
            return outer * inner;
        }));

        /// <summary>Builds a white sprite whose alpha comes from f(x, y, size) with x,y in -1..1.</summary>
        private static Sprite MakeSprite(int size, Func<float, float, int, float> alpha)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[size * size];
            for (int py = 0; py < size; py++)
                for (int px = 0; px < size; px++)
                {
                    float x = (px + 0.5f) / size * 2f - 1f;
                    float y = (py + 0.5f) / size * 2f - 1f;
                    byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(x, y, size)) * 255f);
                    pixels[py * size + px] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        // ---- element builders ---------------------------------------------------------------------

        internal static Canvas CreateCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        internal static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        internal static Image NewImage(string name, Transform parent, Sprite sprite, Color color, bool raycast = false)
        {
            RectTransform rt = NewRect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite ? sprite : White;
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        internal static RawImage NewRawImage(string name, Transform parent, Texture texture, Color color)
        {
            RectTransform rt = NewRect(name, parent);
            var img = rt.gameObject.AddComponent<RawImage>();
            img.texture = texture;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        internal static TextMeshProUGUI NewText(string name, Transform parent, string text, float size, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.Left)
        {
            RectTransform rt = NewRect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (Font) t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = alignment;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        internal static void Stretch(RectTransform rt, float left = 0, float bottom = 0, float right = 0, float top = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        internal static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        /// <summary>A bordered dark panel. Returns the inner content rect.</summary>
        internal static RectTransform NewPanel(string name, Transform parent, Color? fill = null)
        {
            RectTransform outer = NewRect(name, parent);
            var border = outer.gameObject.AddComponent<Image>();
            border.sprite = White;
            border.color = Border;
            border.raycastTarget = false;

            Image inner = NewImage("Fill", outer, White, fill ?? PanelColor);
            Stretch(inner.rectTransform, 2, 2, 2, 2);
            return outer;
        }

        internal static bool Contains(RectTransform rt, Vector2 screenPoint)
        {
            return rt && rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, screenPoint, null);
        }
    }
}
