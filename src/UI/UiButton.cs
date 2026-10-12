using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HighCommand.UI
{
    /// <summary>
    /// A flat button built from plain uGUI images: frame, fill, label, small hotkey hint and an optional icon with a
    /// cooldown sweep. Hit testing is done by the caller (see ClientInput), so it needs no event system.
    /// </summary>
    internal sealed class UiButton
    {
        internal readonly RectTransform Root;
        internal readonly Image Frame;
        internal readonly Image Fill;
        internal readonly RawImage Icon;
        internal readonly Image Cooldown;
        internal readonly TextMeshProUGUI Label;
        internal readonly TextMeshProUGUI Hint;
        internal readonly TextMeshProUGUI Badge;

        internal bool Hover { get; private set; }
        internal bool Enabled = true;
        internal bool Active;
        internal Color Accent = UiKit.Border;

        internal UiButton(Transform parent, string name, Vector2 size, bool withIcon = false)
        {
            Root = UiKit.NewRect(name, parent);
            Root.anchorMin = Root.anchorMax = new Vector2(0f, 1f);
            Root.pivot = new Vector2(0f, 1f);
            Root.sizeDelta = size;

            Frame = Root.gameObject.AddComponent<Image>();
            Frame.sprite = UiKit.White;
            Frame.color = UiKit.Border;
            Frame.raycastTarget = false;

            Fill = UiKit.NewImage("Fill", Root, UiKit.White, new Color(0.08f, 0.1f, 0.14f, 0.96f));
            UiKit.Stretch(Fill.rectTransform, 2, 2, 2, 2);

            if (withIcon)
            {
                Icon = UiKit.NewRawImage("Icon", Root, null, Color.white);
                UiKit.Stretch(Icon.rectTransform, 4, 4, 4, 4);

                Cooldown = UiKit.NewImage("Cooldown", Root, UiKit.White, new Color(0f, 0f, 0f, 0.7f));
                UiKit.Stretch(Cooldown.rectTransform, 2, 2, 2, 2);
                Cooldown.type = Image.Type.Filled;
                Cooldown.fillMethod = Image.FillMethod.Vertical;
                Cooldown.fillOrigin = (int)Image.OriginVertical.Top;
                Cooldown.fillAmount = 0f;
            }

            Label = UiKit.NewText("Label", Root, "", 17f, UiKit.Text, TextAlignmentOptions.Center);
            // the top strip belongs to the hotkey hint: a long caption must not run under it
            UiKit.Stretch(Label.rectTransform, 3, 3, 3, 11);
            Label.enableAutoSizing = true;
            Label.fontSizeMin = 10f;
            Label.fontSizeMax = 17f;
            Label.enableWordWrapping = true;
            Label.overflowMode = TextOverflowModes.Truncate;

            Hint = UiKit.NewText("Hint", Root, "", 12f, UiKit.TextDim, TextAlignmentOptions.TopRight);
            UiKit.Stretch(Hint.rectTransform, 2, 2, 5, 2);

            Badge = UiKit.NewText("Badge", Root, "", 13f, UiKit.Gold, TextAlignmentOptions.BottomRight);
            UiKit.Stretch(Badge.rectTransform, 2, 2, 5, 2);
        }

        internal void SetPosition(float x, float y) => Root.anchoredPosition = new Vector2(x, -y);

        /// <summary>Updates hover state and colours; returns true while the pointer is over the button.</summary>
        internal bool Refresh(Vector2 mouse)
        {
            Hover = Root.gameObject.activeInHierarchy && UiKit.Contains(Root, mouse);

            Color frame = Accent;
            if (!Enabled) frame = new Color(Accent.r, Accent.g, Accent.b, 0.25f);
            else if (Active) frame = UiKit.Accent;
            else if (Hover) frame = Color.Lerp(Accent, Color.white, 0.55f);
            Frame.color = frame;

            Color fill = new Color(0.08f, 0.1f, 0.14f, 0.96f);
            if (!Enabled) fill = new Color(0.06f, 0.07f, 0.09f, 0.9f);
            else if (Active) fill = new Color(0.22f, 0.15f, 0.06f, 0.96f);
            else if (Hover) fill = new Color(0.16f, 0.2f, 0.28f, 0.96f);
            Fill.color = fill;

            Label.color = Enabled ? UiKit.Text : UiKit.TextDim;
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
