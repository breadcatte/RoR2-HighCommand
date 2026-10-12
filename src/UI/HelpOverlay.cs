using System.Collections.Generic;
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

        private const float PanelW = 1580f;
        private const float ColumnW = 770f;
        private const float KeysW = 280f;
        private const float WhatW = 480f;
        private const float RowH = 30f;
        private const float HeaderH = 92f;
        private const float FooterH = 34f;

        private readonly RectTransform root;
        private readonly RectTransform rowsHost;
        private readonly TextMeshProUGUI title;

        internal HelpOverlay(Transform parent)
        {
            root = UiKit.NewPanel("Help", parent, UiKit.PanelDark);
            UiKit.Place(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PanelW, 600f));

            title = UiKit.NewText("Title", root, "", 34f, UiKit.Accent, TextAlignmentOptions.Center);
            UiKit.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(PanelW - 40f, 50f));

            rowsHost = UiKit.NewRect("Rows", root);
            UiKit.Place(rowsHost, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -HeaderH), new Vector2(PanelW, 500f));

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

        // Two columns of "keys | what" rows. Every row is its own pair of text objects, so a long description can
        // never push the following rows out of line (it shrinks instead of wrapping).
        private void Rebuild()
        {
            title.text = Loc.Get("HIGHCOMMAND_HELP_TITLE");

            for (int i = rowsHost.childCount - 1; i >= 0; i--)
                Object.Destroy(rowsHost.GetChild(i).gameObject);

            int perColumn = Mathf.Max(1, (Lines.Count + 1) / 2);
            for (int i = 0; i < Lines.Count; i++)
            {
                int column = i / perColumn;
                int row = i % perColumn;
                float x = 28f + column * (ColumnW + 24f);
                float y = -row * RowH;

                TextMeshProUGUI keys = UiKit.NewText("Keys", rowsHost, Lines[i].Key, 20f, UiKit.Gold, TextAlignmentOptions.Left);
                UiKit.Place(keys.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y), new Vector2(KeysW, RowH));
                Fit(keys);

                TextMeshProUGUI what = UiKit.NewText("What", rowsHost, Lines[i].Value, 20f, UiKit.Text, TextAlignmentOptions.Left);
                UiKit.Place(what.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x + KeysW + 10f, y), new Vector2(WhatW, RowH));
                Fit(what);
            }

            float height = HeaderH + perColumn * RowH + FooterH;
            UiKit.Place(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PanelW, height));
            UiKit.Place(rowsHost, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -HeaderH), new Vector2(PanelW, perColumn * RowH));
        }

        private static void Fit(TextMeshProUGUI text)
        {
            text.enableWordWrapping = false;
            text.enableAutoSizing = true;
            text.fontSizeMin = 12f;
            text.fontSizeMax = 20f;
            text.overflowMode = TextOverflowModes.Ellipsis;
        }

        internal void Destroy()
        {
            if (root) Object.Destroy(root.gameObject);
        }
    }
}
