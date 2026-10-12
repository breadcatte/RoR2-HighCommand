using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace HighCommand.UI
{
    /// <summary>Hotkey cheat sheet (F1). Lines are registered by each module so the sheet always matches the config.</summary>
    internal sealed class HelpOverlay
    {
        private static readonly List<KeyValuePair<string, string>> Lines = new List<KeyValuePair<string, string>>();

        /// <summary>Registers a help line: the keys and what they do (both already localised).</summary>
        internal static void Add(string keys, string what) => Lines.Add(new KeyValuePair<string, string>(keys, what));

        internal static void Clear() => Lines.Clear();

        private readonly RectTransform root;
        private readonly TextMeshProUGUI keysText;
        private readonly TextMeshProUGUI whatText;
        private readonly TextMeshProUGUI title;

        internal HelpOverlay(Transform parent)
        {
            root = UiKit.NewPanel("Help", parent, UiKit.PanelDark);
            UiKit.Place(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(780f, 560f));

            title = UiKit.NewText("Title", root, "", 34f, UiKit.Accent, TextAlignmentOptions.Center);
            UiKit.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(740f, 50f));

            keysText = UiKit.NewText("Keys", root, "", 22f, UiKit.Gold);
            UiKit.Place(keysText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(26f, -84f), new Vector2(300f, 450f));
            keysText.enableWordWrapping = false;

            whatText = UiKit.NewText("What", root, "", 22f, UiKit.Text);
            UiKit.Place(whatText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(340f, -84f), new Vector2(420f, 450f));

            root.gameObject.SetActive(false);
        }

        internal bool Visible => root && root.gameObject.activeSelf;

        internal void Toggle()
        {
            if (!root) return;
            bool show = !root.gameObject.activeSelf;
            if (show) Rebuild();
            root.gameObject.SetActive(show);
        }

        internal void Hide()
        {
            if (root) root.gameObject.SetActive(false);
        }

        private void Rebuild()
        {
            title.text = Loc.Get("HIGHCOMMAND_HELP_TITLE");
            var keys = new StringBuilder();
            var what = new StringBuilder();
            foreach (var line in Lines)
            {
                keys.AppendLine(line.Key);
                what.AppendLine(line.Value);
            }
            keysText.text = keys.ToString();
            whatText.text = what.ToString();
        }

        internal void Destroy()
        {
            if (root) Object.Destroy(root.gameObject);
        }
    }
}
