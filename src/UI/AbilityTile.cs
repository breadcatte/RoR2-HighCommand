using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HighCommand.UI
{
    /// <summary>
    /// One commander ability on the bottom strip: a coloured well with the ability's picture lit from behind, the name
    /// under it, the hotkey in the corner and a thin charge line along the bottom. While it recharges the well darkens
    /// with a clock sweep and the seconds, the line fills up; when it is ready again the tile flashes once. Each ability
    /// has its own colour (the same one tints the cursor while its target is being chosen).
    /// </summary>
    internal sealed class AbilityTile
    {
        /// <summary>Orbital strike, medic, war cry, mark.</summary>
        internal static readonly Color[] Colors =
        {
            new Color(1f, 0.46f, 0.2f, 1f),
            new Color(0.36f, 0.92f, 0.5f, 1f),
            new Color(1f, 0.8f, 0.24f, 1f),
            new Color(0.76f, 0.48f, 1f, 1f)
        };

        internal static readonly Glyph[] Glyphs = { Glyph.OrbitalStrike, Glyph.Medic, Glyph.WarCry, Glyph.Mark };

        internal static Color ColorOf(int index) => Colors[Mathf.Clamp(index, 0, Colors.Length - 1)];
        internal static Glyph GlyphOf(int index) => Glyphs[Mathf.Clamp(index, 0, Glyphs.Length - 1)];

        private const float Cut = 6f, WellCut = 5f;
        private const float WellInset = 5f, WellH = 48f;
        private const float GlyphSize = 36f;
        private const float FlashTime = 0.8f;

        private static readonly Color Base = new Color(0.07f, 0.09f, 0.12f, 0.96f);
        private static readonly Color WellBase = new Color(0.05f, 0.06f, 0.08f, 1f);
        private static readonly Color Spent = new Color(0.6f, 0.65f, 0.72f, 0.75f);

        internal readonly RectTransform Root;
        internal readonly TextMeshProUGUI Label;
        private readonly Image frame, fill, well, glow, glyph, sweep, barFill;
        private readonly TextMeshProUGUI seconds, hint;
        private readonly Image keyCap;
        private readonly Color color;

        internal bool Hover { get; private set; }

        private bool wasReady = true;
        private float flashUntil;
        private int shownSeconds = -1;

        // what the colours were last set to (a colour written every frame would rebuild the canvas)
        private Color shownFrame, shownFill, shownWell, shownGlow, shownGlyph, shownBar, shownLabel;
        private bool colorsKnown;

        internal AbilityTile(Transform parent, int index, Vector2 size)
        {
            color = ColorOf(index);

            Root = UiKit.NewRect("Ability" + index, parent);
            Root.anchorMin = Root.anchorMax = new Vector2(0f, 1f);
            Root.pivot = new Vector2(0f, 1f);
            Root.sizeDelta = size;

            frame = Root.gameObject.AddComponent<Image>();
            UiKit.StyleOutline(frame, Cut);
            frame.raycastTarget = false;
            fill = UiKit.NewFill("Fill", Root, Base, Cut);

            // the well: a darker, tinted plate the picture sits in
            well = UiKit.NewImage("Well", Root, UiKit.Shape(WellCut), WellBase);
            well.type = Image.Type.Sliced;
            RectTransform w = well.rectTransform;
            w.anchorMin = new Vector2(0f, 1f);
            w.anchorMax = new Vector2(1f, 1f);
            w.pivot = new Vector2(0.5f, 1f);
            w.offsetMin = new Vector2(WellInset, -(WellInset + WellH));
            w.offsetMax = new Vector2(-WellInset, -WellInset);

            // a faint light along its top edge (clear of the cut corner), as if the plate were bevelled
            Image sheen = UiKit.NewImage("Sheen", w, UiKit.White, new Color(1f, 1f, 1f, 0.12f));
            RectTransform sh = sheen.rectTransform;
            sh.anchorMin = new Vector2(0f, 1f);
            sh.anchorMax = new Vector2(1f, 1f);
            sh.pivot = new Vector2(0.5f, 1f);
            sh.offsetMin = new Vector2(WellCut + 1f, -1f);
            sh.offsetMax = Vector2.zero;

            glow = UiKit.NewImage("Glow", w, UiKit.Glow, color);
            UiKit.Place(glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(WellH + 22f, WellH + 22f));

            glyph = UiKit.NewImage("Glyph", w, UiKit.GlyphSprite(GlyphOf(index)), Color.white);
            glyph.preserveAspect = true;
            UiKit.Place(glyph.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(GlyphSize, GlyphSize));

            // recharging: a clock face darkening the picture, the seconds on top of it
            sweep = UiKit.NewImage("Sweep", w, UiKit.Circle, new Color(0f, 0f, 0f, 0.6f));
            UiKit.Place(sweep.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(WellH - 4f, WellH - 4f));
            sweep.type = Image.Type.Filled;
            sweep.fillMethod = Image.FillMethod.Radial360;
            sweep.fillOrigin = (int)Image.Origin360.Top;
            sweep.fillClockwise = false;
            sweep.fillAmount = 0f;
            seconds = UiKit.NewText("Seconds", w, "", 22f, UiKit.Gold, TextAlignmentOptions.Center);
            UiKit.Stretch(seconds.rectTransform, 0, 0, 0, 0);

            // one line, one size for all four (a long name only shrinks when it would not fit at all)
            Label = UiKit.NewText("Label", Root, "", 12f, UiKit.Text, TextAlignmentOptions.Center);
            UiKit.Stretch(Label.rectTransform, 2, 9, 2, WellInset + WellH + 2f);
            Label.enableAutoSizing = true;
            Label.fontSizeMin = 9f;
            Label.fontSizeMax = 12f;

            // the charge line along the bottom edge (kept clear of the cut corner on the right)
            Image barBack = UiKit.NewImage("ChargeBack", Root, UiKit.White, new Color(0f, 0f, 0f, 0.55f));
            RectTransform bb = barBack.rectTransform;
            bb.anchorMin = new Vector2(0f, 0f);
            bb.anchorMax = new Vector2(1f, 0f);
            bb.pivot = new Vector2(0.5f, 0f);
            bb.offsetMin = new Vector2(WellInset, 4f);
            bb.offsetMax = new Vector2(-(WellInset + Cut), 7f);
            barFill = UiKit.NewImage("Charge", bb, UiKit.White, color);
            UiKit.Stretch(barFill.rectTransform, 0, 0, 0, 0);
            barFill.type = Image.Type.Filled;
            barFill.fillMethod = Image.FillMethod.Horizontal;
            barFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            barFill.fillAmount = 1f;

            keyCap = UiKit.NewImage("KeyCap", Root, UiKit.White, new Color(0.02f, 0.03f, 0.05f, 0.85f));
            UiKit.Place(keyCap.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-3f, -3f), new Vector2(18f, 14f));
            hint = UiKit.NewText("Hint", keyCap.rectTransform, "", 11f, UiKit.TextDim, TextAlignmentOptions.Center);
            UiKit.Stretch(hint.rectTransform, 0, 0, 0, 0);
        }

        internal void SetPosition(float x, float y) => Root.anchoredPosition = new Vector2(x, -y);

        /// <summary>The hotkey shown on the key cap in the top right corner.</summary>
        internal void SetHint(string key)
        {
            key = key ?? "";
            UiKit.SetText(hint, key);
            keyCap.enabled = key.Length > 0;
            keyCap.rectTransform.sizeDelta = new Vector2(Mathf.Clamp(key.Length * 7f + 6f, 14f, 46f), 14f);
        }

        /// <summary>
        /// Updates the tile; returns true while the pointer is over it.
        /// </summary>
        /// <param name="left">Seconds until ready (0 = ready).</param>
        /// <param name="total">The full cooldown.</param>
        /// <param name="active">Its target is being chosen right now.</param>
        internal bool Refresh(Vector2 mouse, float left, float total, bool active)
        {
            Hover = UiKit.Contains(Root, mouse);
            bool ready = left <= 0f;
            float now = Time.unscaledTime;
            if (ready && !wasReady) flashUntil = now + FlashTime;
            wasReady = ready;
            float flash = Mathf.Clamp01((flashUntil - now) / FlashTime);
            float pulse = active ? 0.5f + 0.5f * Mathf.Sin(now * 7f) : 0f;

            // the outline: the ability's colour, brighter under the pointer, white for a moment when it is ready again
            Color f = active ? Color.Lerp(color, Color.white, 0.2f + 0.3f * pulse) : Hover ? Color.Lerp(color, Color.white, 0.4f) : color;
            f.a = active || Hover ? 1f : ready ? 0.7f : 0.35f;
            f = Color.Lerp(f, Color.white, flash * 0.85f);

            Color b = Color.Lerp(Base, color, active ? 0.2f : Hover ? 0.14f : 0.07f);
            b.a = Base.a;

            Color wl = Color.Lerp(WellBase, color, !ready ? 0.1f : active ? 0.42f : Hover ? 0.36f : 0.27f);
            wl.a = 1f;

            Color g = color;
            g.a = ready ? Mathf.Clamp01(0.7f + (Hover ? 0.2f : 0f) + 0.3f * pulse + 0.5f * flash) : 0.12f;

            Color gl = ready ? Color.Lerp(color, Color.white, active || Hover ? 0.8f : 0.6f) : Spent;

            Color bar = color;
            bar.a = ready ? 0.95f : 0.45f;

            Color label = ready ? UiKit.Text : UiKit.TextDim;

            if (!colorsKnown || f != shownFrame) frame.color = shownFrame = f;
            if (!colorsKnown || b != shownFill) fill.color = shownFill = b;
            if (!colorsKnown || wl != shownWell) well.color = shownWell = wl;
            if (!colorsKnown || g != shownGlow) glow.color = shownGlow = g;
            if (!colorsKnown || gl != shownGlyph) glyph.color = shownGlyph = gl;
            if (!colorsKnown || bar != shownBar) barFill.color = shownBar = bar;
            if (!colorsKnown || label != shownLabel) Label.color = shownLabel = label;
            colorsKnown = true;

            float part = ready || total <= 0f ? 0f : Mathf.Clamp01(left / total);
            if (sweep.fillAmount != part) sweep.fillAmount = part;
            if (barFill.fillAmount != 1f - part) barFill.fillAmount = 1f - part;
            int s = ready ? 0 : Mathf.CeilToInt(left);
            if (s != shownSeconds)
            {
                shownSeconds = s;
                UiKit.SetText(seconds, s > 0 ? s.ToString() : "");
            }
            return Hover;
        }

        internal bool Clicked(bool mouseDown) => Hover && mouseDown;
    }
}
