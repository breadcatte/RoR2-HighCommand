using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HighCommand.UI
{
    /// <summary>
    /// Immediate-mode tooltip: any panel calls <see cref="Show"/> every frame while the pointer hovers something; the
    /// view draws it next to the cursor and hides it again when nobody asked this frame.
    /// </summary>
    internal static class Tooltip
    {
        internal static bool Requested { get; private set; }
        internal static string Title { get; private set; }
        internal static string Body { get; private set; }
        internal static Color TitleColor { get; private set; }

        internal static void Show(string title, string body, Color? titleColor = null)
        {
            Requested = true;
            Title = title ?? "";
            Body = body ?? "";
            TitleColor = titleColor ?? UiKit.Text;
        }

        internal static void Clear() => Requested = false;
    }

    internal sealed class TooltipView
    {
        private const float Width = 440f;
        private const float Pad = 12f;

        private readonly Canvas canvas;
        private readonly RectTransform root;
        private readonly TextMeshProUGUI title;
        private readonly TextMeshProUGUI body;
        private string shownTitle, shownBody;
        private float height = 80f;

        internal TooltipView(Transform parent)
        {
            canvas = parent.GetComponent<Canvas>();
            root = UiKit.NewPanel("Tooltip", parent, UiKit.PanelDark);
            root.anchorMin = root.anchorMax = Vector2.zero;
            root.pivot = new Vector2(0f, 1f);
            root.sizeDelta = new Vector2(Width, 80f);

            title = UiKit.NewText("Title", root, "", 24f, UiKit.Text, TextAlignmentOptions.TopLeft);
            UiKit.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Pad, -Pad), new Vector2(Width - 2f * Pad, 30f));
            title.enableWordWrapping = true;

            body = UiKit.NewText("Body", root, "", 19f, UiKit.TextDim, TextAlignmentOptions.TopLeft);
            UiKit.Place(body.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Pad, -Pad - 34f), new Vector2(Width - 2f * Pad, 40f));
            body.enableWordWrapping = true;

            root.gameObject.SetActive(false);
        }

        internal void Destroy()
        {
            if (root) Object.Destroy(root.gameObject);
        }

        /// <summary>Call after all panels have ticked.</summary>
        internal void Tick()
        {
            if (!root) return;
            if (!Tooltip.Requested)
            {
                if (root.gameObject.activeSelf) root.gameObject.SetActive(false);
                return;
            }
            Tooltip.Clear();

            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);

            // The panels ask every frame; measuring the text is the costly part, so it is redone only for new text.
            if (!string.Equals(shownTitle, Tooltip.Title, System.StringComparison.Ordinal)
                || !string.Equals(shownBody, Tooltip.Body, System.StringComparison.Ordinal))
            {
                shownTitle = Tooltip.Title;
                shownBody = Tooltip.Body;
                title.text = shownTitle;
                body.text = shownBody;

                float innerW = Width - 2f * Pad;
                float titleH = Mathf.Max(26f, title.GetPreferredValues(shownTitle, innerW, 0f).y);
                float bodyH = string.IsNullOrEmpty(shownBody) ? 0f : body.GetPreferredValues(shownBody, innerW, 0f).y;
                height = Pad * 2f + titleH + (bodyH > 0f ? 6f + bodyH : 0f);

                title.rectTransform.sizeDelta = new Vector2(innerW, titleH);
                body.rectTransform.anchoredPosition = new Vector2(Pad, -Pad - titleH - 6f);
                body.rectTransform.sizeDelta = new Vector2(innerW, bodyH);
                root.sizeDelta = new Vector2(Width, height);
            }
            title.color = Tooltip.TitleColor;

            float scale = canvas ? Mathf.Max(0.01f, canvas.scaleFactor) : 1f;
            Vector2 mouse = Client.ClientInput.Mouse / scale;
            Vector2 screen = new Vector2(Screen.width, Screen.height) / scale;

            float x = mouse.x + 18f;
            float y = mouse.y - 18f;
            if (x + Width > screen.x - 4f) x = mouse.x - 18f - Width;
            if (y - height < 4f) y = mouse.y + 18f + height;
            x = Mathf.Clamp(x, 4f, Mathf.Max(4f, screen.x - Width - 4f));
            y = Mathf.Clamp(y, height + 4f, Mathf.Max(height + 4f, screen.y - 4f));
            root.anchoredPosition = new Vector2(x, y);
            root.SetAsLastSibling();
        }
    }
}
