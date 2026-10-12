using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HighCommand.UI
{
    /// <summary>
    /// A button built from plain uGUI images: a cut-corner outline and fill, a small glyph or a game icon with a cooldown
    /// sweep, a caption, and the hotkey on a little key cap. Hit testing is done by the caller (see ClientInput), so it
    /// needs no event system.
    /// </summary>
    internal sealed class UiButton
    {
        private const float Cut = 5f;

        internal readonly RectTransform Root;
        internal readonly Image Frame;
        internal readonly Image Fill;
        internal readonly Image GlyphImage;
        internal readonly RawImage Icon;
        internal readonly Image Cooldown;
        internal readonly TextMeshProUGUI Label;
        internal readonly TextMeshProUGUI Hint;
        internal readonly TextMeshProUGUI Badge;
        private readonly Image keyCap;

        internal bool Hover { get; private set; }
        internal bool Enabled = true;
        internal bool Active;
        internal Color Accent = UiKit.Border;

        // what the colours were last set to (a colour written every frame would rebuild the canvas)
        private Color shownFrame, shownFill, shownGlyph, shownLabel;
        private bool colorsKnown;

        internal UiButton(Transform parent, string name, Vector2 size, bool withIcon = false)
        {
            Root = UiKit.NewRect(name, parent);
            Root.anchorMin = Root.anchorMax = new Vector2(0f, 1f);
            Root.pivot = new Vector2(0f, 1f);
            Root.sizeDelta = size;

            Frame = Root.gameObject.AddComponent<Image>();
            UiKit.StyleOutline(Frame, Cut);
            Frame.color = UiKit.Border;
            Frame.raycastTarget = false;

            Fill = UiKit.NewFill("Fill", Root, new Color(0.08f, 0.1f, 0.14f, 0.96f), Cut);

            if (withIcon)
            {
                Icon = UiKit.NewRawImage("Icon", Root, null, Color.white);
                UiKit.Stretch(Icon.rectTransform, 4, 4, 4, 4);
            }

            GlyphImage = UiKit.NewImage("Glyph", Root, UiKit.White, Color.white);
            GlyphImage.preserveAspect = true;
            GlyphImage.enabled = false;

            if (withIcon)
            {
                Cooldown = UiKit.NewImage("Cooldown", Root, UiKit.White, new Color(0f, 0f, 0f, 0.68f));
                UiKit.Stretch(Cooldown.rectTransform, 2, 2, 2, 2);
                Cooldown.type = Image.Type.Filled;
                Cooldown.fillMethod = Image.FillMethod.Radial360;
                Cooldown.fillOrigin = (int)Image.Origin360.Top;
                Cooldown.fillClockwise = false;
                Cooldown.fillAmount = 0f;
            }

            Label = UiKit.NewText("Label", Root, "", 17f, UiKit.Text, TextAlignmentOptions.Center);
            // the top strip belongs to the hotkey hint: a long caption must not run under it
            UiKit.Stretch(Label.rectTransform, 3, 3, 3, 11);
            Label.enableAutoSizing = true;
            Label.fontSizeMin = 9f;
            Label.fontSizeMax = 17f;
            Label.enableWordWrapping = true;
            Label.overflowMode = TextOverflowModes.Truncate;

            keyCap = UiKit.NewImage("KeyCap", Root, UiKit.White, new Color(0.02f, 0.03f, 0.05f, 0.85f));
            UiKit.Place(keyCap.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-3f, -3f), new Vector2(14f, 14f));
            keyCap.enabled = false;
            Hint = UiKit.NewText("Hint", keyCap.rectTransform, "", 11f, UiKit.TextDim, TextAlignmentOptions.Center);
            UiKit.Stretch(Hint.rectTransform, 0, 0, 0, 0);

            Badge = UiKit.NewText("Badge", Root, "", 22f, UiKit.Gold, TextAlignmentOptions.Center);
            UiKit.Stretch(Badge.rectTransform, 2, 2, 2, 2);
        }

        internal void SetPosition(float x, float y) => Root.anchoredPosition = new Vector2(x, -y);

        /// <summary>The hotkey shown on the key cap in the top right corner (empty: no cap).</summary>
        internal void SetHint(string key)
        {
            key = key ?? "";
            bool show = key.Length > 0;
            if (keyCap.enabled != show) keyCap.enabled = show;
            if (!string.Equals(Hint.text, key, System.StringComparison.Ordinal)) Hint.text = key;
            if (show)
                keyCap.rectTransform.sizeDelta = new Vector2(Mathf.Clamp(key.Length * 7f + 6f, 14f, 46f), 14f);
        }

        /// <summary>A drawn picture above the caption instead of a game icon.</summary>
        internal void SetGlyph(Glyph glyph, float size = 24f)
        {
            GlyphImage.sprite = UiKit.GlyphSprite(glyph);
            GlyphImage.enabled = true;
            UiKit.Place(GlyphImage.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -5f), new Vector2(size, size));
            // the caption moves under the picture
            UiKit.Stretch(Label.rectTransform, 2, 2, 2, size + 7f);
            Label.fontSizeMax = 13f;
            Label.fontSizeMin = 8f;
            Label.alignment = TextAlignmentOptions.Center;
        }

        /// <summary>Updates hover state and colours; returns true while the pointer is over the button.</summary>
        internal bool Refresh(Vector2 mouse)
        {
            Hover = Root.gameObject.activeInHierarchy && UiKit.Contains(Root, mouse);

            Color frame = Accent;
            if (!Enabled) frame = new Color(Accent.r, Accent.g, Accent.b, 0.25f);
            else if (Active) frame = UiKit.Accent;
            else if (Hover) frame = Color.Lerp(Accent, Color.white, 0.55f);

            Color fill = new Color(0.08f, 0.1f, 0.14f, 0.96f);
            if (!Enabled) fill = new Color(0.06f, 0.07f, 0.09f, 0.9f);
            else if (Active) fill = new Color(0.24f, 0.16f, 0.06f, 0.96f);
            else if (Hover) fill = new Color(0.16f, 0.2f, 0.28f, 0.96f);

            Color glyph;
            if (!Enabled) glyph = new Color(0.55f, 0.6f, 0.68f, 0.35f);
            else if (Active) glyph = UiKit.Accent;
            else
            {
                Color baseColor = new Color(Accent.r, Accent.g, Accent.b, 1f);
                glyph = Color.Lerp(baseColor, Color.white, Hover ? 0.85f : 0.45f);
            }

            Color label = Enabled ? UiKit.Text : UiKit.TextDim;

            if (!colorsKnown || frame != shownFrame) Frame.color = shownFrame = frame;
            if (!colorsKnown || fill != shownFill) Fill.color = shownFill = fill;
            if (!colorsKnown || glyph != shownGlyph) GlyphImage.color = shownGlyph = glyph;
            if (!colorsKnown || label != shownLabel) Label.color = shownLabel = label;
            colorsKnown = true;
            return Hover;
        }

        /// <summary>True when the left mouse button went down on the button this frame.</summary>
        internal bool Clicked(bool mouseDown) => Enabled && Hover && mouseDown;

        internal void SetVisible(bool visible)
        {
            if (Root.gameObject.activeSelf != visible) Root.gameObject.SetActive(visible);
        }
    }
}
