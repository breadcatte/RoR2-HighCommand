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
        internal static readonly Color Border = new Color(0.5f, 0.6f, 0.72f, 0.7f);
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
        private static Sprite arc;
        private static Sprite marker;

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

        /// <summary>
        /// The front (lower) part of a thin ring, its ends fading out: stretched into an ellipse it reads as a mark on
        /// the ground in front of a unit's feet. The back half is left out so the unit itself stays uncovered.
        /// </summary>
        internal static Sprite Arc => arc ? arc : (arc = MakeSprite(128, (x, y, s) =>
        {
            float d = Mathf.Sqrt(x * x + y * y);
            float outer = Mathf.Clamp01((1f - d) * (s * 0.5f));
            float inner = Mathf.Clamp01((d - 0.84f) * (s * 0.5f));
            float front = d > 0.001f ? Mathf.Clamp01((-y / d - 0.12f) / 0.5f) : 0f;
            return outer * inner * front;
        }));

        /// <summary>A small downward pointing triangle with soft edges (a mark floating above a unit).</summary>
        internal static Sprite Marker => marker ? marker : (marker = MakeSprite(48, (x, y, s) =>
        {
            float half = Mathf.Clamp01((y + 0.85f) / 1.5f) * 0.95f;       // widest at the top, a point at the bottom
            float side = Mathf.Clamp01((half - Mathf.Abs(x)) * (s * 0.5f));
            float top = Mathf.Clamp01((0.65f - y) * (s * 0.5f));
            float bottom = Mathf.Clamp01((y + 0.9f) * (s * 0.5f));
            return side * top * bottom;
        }));

        // ---- chamfered panel shapes ----------------------------------------------------------------

        /// <summary>The default corner cut of a panel (top left and bottom right are cut, the other two stay square).</summary>
        internal const float PanelCut = 10f;

        private static readonly Dictionary<int, Sprite> shapes = new Dictionary<int, Sprite>();

        /// <summary>
        /// A nine-sliced rectangle with its top left and bottom right corners cut off at 45 degrees. Used as the outline and,
        /// a little smaller (see <see cref="InnerCut"/>), as the fill inside it. Needs Image.Type.Sliced.
        /// </summary>
        internal static Sprite Shape(float cut)
        {
            int key = Mathf.RoundToInt(cut * 10f);
            if (shapes.TryGetValue(key, out Sprite cached) && cached) return cached;

            float c = Mathf.Max(0f, cut);
            int border = Mathf.CeilToInt(c) + 2;
            int size = border * 2 + 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[size * size];
            const float Root2 = 1.41421356f;
            for (int py = 0; py < size; py++)
                for (int px = 0; px < size; px++)
                {
                    float x = px + 0.5f, y = py + 0.5f;
                    float dTopLeft = (x + (size - y) - c) / Root2;
                    float dBottomRight = ((size - x) + y - c) / Root2;
                    float a = Mathf.Clamp01(Mathf.Min(dTopLeft, dBottomRight) + 0.5f);
                    pixels[py * size + px] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            sprite.hideFlags = HideFlags.HideAndDontSave;
            shapes[key] = sprite;
            return sprite;
        }

        /// <summary>The corner cut of a fill that sits <paramref name="inset"/> pixels inside an outline cut by <paramref name="cut"/>.</summary>
        internal static float InnerCut(float cut, float inset) => Mathf.Max(0f, cut - (2f - 1.41421356f) * inset);

        /// <summary>Turns a plain Image into a chamfered outline (the colour is the outline's).</summary>
        internal static void StyleOutline(Image image, float cut)
        {
            image.sprite = Shape(cut);
            image.type = Image.Type.Sliced;
        }

        /// <summary>A chamfered fill stretched inside its parent by <paramref name="inset"/> pixels.</summary>
        internal static Image NewFill(string name, Transform parent, Color color, float cut, float inset = 2f)
        {
            Image fill = NewImage(name, parent, Shape(InnerCut(cut, inset)), color);
            fill.type = Image.Type.Sliced;
            Stretch(fill.rectTransform, inset, inset, inset, inset);
            return fill;
        }

        // ---- dots and glyphs --------------------------------------------------------------------

        private static Sprite dot;
        private static readonly Sprite[] glyphs = new Sprite[(int)Glyph.Count];

        /// <summary>
        /// A round dot with a dark rim baked in (multiplied by the Image colour it gives a coloured disc with a black
        /// outline), so a mark stays readable on a busy map without a second image behind it.
        /// </summary>
        internal static Sprite Dot
        {
            get
            {
                if (dot) return dot;
                const int size = 48;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
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
                        float d = Mathf.Sqrt(x * x + y * y);
                        float a = Mathf.Clamp01((1f - d) * size * 0.5f);
                        float lum = Mathf.Lerp(1f, 0.06f, Mathf.Clamp01((d - 0.52f) / 0.14f));
                        byte l = (byte)Mathf.RoundToInt(lum * 255f);
                        pixels[py * size + px] = new Color32(l, l, l, (byte)Mathf.RoundToInt(a * 255f));
                    }
                tex.SetPixels32(pixels);
                tex.Apply(true, true);
                dot = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
                dot.hideFlags = HideFlags.HideAndDontSave;
                return dot;
            }
        }

        /// <summary>A white glyph (tinted with the Image colour); drawn once, with mip levels so it stays clean when small.</summary>
        internal static Sprite GlyphSprite(Glyph glyph)
        {
            int index = (int)glyph;
            if (index < 0 || index >= glyphs.Length) return White;
            if (glyphs[index]) return glyphs[index];

            const int size = 64;
            float[] alpha = GlyphArt.Render(glyph, size);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear
            };
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha[i]) * 255f));
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            glyphs[index] = sprite;
            return sprite;
        }

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
            // HGTextMeshProUGUI picks the game's font for the current language (Cyrillic, CJK ...) on Awake.
            var t = rt.gameObject.AddComponent<RoR2.UI.HGTextMeshProUGUI>();
            if (!RoR2.UI.HGTextMeshProUGUI.defaultLanguageFont && Font) t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = alignment;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        /// <summary>Sets a text only when it differs: writing an equal string would still rebuild the text mesh.</summary>
        internal static void SetText(TMP_Text text, string value)
        {
            if (text && !string.Equals(text.text, value, StringComparison.Ordinal)) text.text = value;
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

        /// <summary>
        /// A dark panel with a thin steel outline, two cut corners and a short orange tick on the cut corners' edges.
        /// Returns the panel's rect; children go straight into it.
        /// </summary>
        internal static RectTransform NewPanel(string name, Transform parent, Color? fill = null, bool ticks = true)
        {
            RectTransform outer = NewRect(name, parent);
            var border = outer.gameObject.AddComponent<Image>();
            StyleOutline(border, PanelCut);
            border.color = Border;
            border.raycastTarget = false;

            NewFill("Fill", outer, fill ?? PanelColor, PanelCut);

            if (ticks)
            {
                Image top = NewImage("TickTop", outer, White, Accent);
                Place(top.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(PanelCut + 6f, 0f), new Vector2(34f, 2f));
                Image bottom = NewImage("TickBottom", outer, White, Accent);
                Place(bottom.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-(PanelCut + 6f), 0f), new Vector2(34f, 2f));
            }
            return outer;
        }

        internal static bool Contains(RectTransform rt, Vector2 screenPoint)
        {
            return rt && rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, screenPoint, null);
        }
    }
}
