using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HighCommand.UI
{
    /// <summary>Hotkey cheat sheet (F1). Lines are registered by each module so the sheet always matches the config.</summary>
    internal sealed class HelpOverlay
    {
        private enum Kind { Line, Section, Column }

        private struct Entry
        {
            internal Kind kind;
            internal string keys, what;
        }

        private static readonly List<Entry> Entries = new List<Entry>();

        /// <summary>Registers a help line: the keys and what they do (both already localised).</summary>
        internal static void Add(string keys, string what) => Entries.Add(new Entry { kind = Kind.Line, keys = keys, what = what });

        /// <summary>A heading over the lines that follow (Camera, Selection ...).</summary>
        internal static void Section(string title) => Entries.Add(new Entry { kind = Kind.Section, what = title });

        /// <summary>What follows goes into the right column.</summary>
        internal static void NextColumn() => Entries.Add(new Entry { kind = Kind.Column });

        internal static void Clear() => Entries.Clear();

        // two columns inside the panel: 36 + 700 + 48 + 700 + 36 = 1520
        private const float PanelW = 1520f;
        private const float Side = 36f, ColumnGap = 48f;
        private const float ColumnW = (PanelW - 2 * Side - ColumnGap) / 2f;
        private const float KeysW = 250f;
        private const float RowH = 29f;
        private const float SectionH = 40f;
        private const float HeaderH = 84f;
        private const float FooterH = 30f;

        private static readonly Color Stripe = new Color(1f, 1f, 1f, 0.035f);
        private static readonly Color KeyColor = new Color(1f, 0.82f, 0.4f, 1f);

        private readonly RectTransform root;
        private readonly RectTransform rowsHost;
        private readonly TextMeshProUGUI title;
        private readonly TextMeshProUGUI closeHint;

        internal HelpOverlay(Transform parent)
        {
            // fully opaque: the sheet lies over the command card and must not let it shine through
            root = UiKit.NewPanel("Help", parent, new Color(0.04f, 0.05f, 0.07f, 0.98f));
            UiKit.Place(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PanelW, 600f));

            Image bar = UiKit.NewImage("HeadBar", root, UiKit.White, UiKit.Accent);
            UiKit.Place(bar.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Side, -30f), new Vector2(4f, 26f));
            title = UiKit.NewText("Title", root, "", 28f, UiKit.Text, TextAlignmentOptions.Left);
            UiKit.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Side + 16f, -24f), new Vector2(PanelW - 2 * Side - 300f, 38f));
            closeHint = UiKit.NewText("Close", root, "", 16f, UiKit.TextDim, TextAlignmentOptions.Right);
            UiKit.Place(closeHint.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-Side, -30f), new Vector2(280f, 26f));
            Image line = UiKit.NewImage("HeadLine", root, UiKit.White, new Color(0.6f, 0.7f, 0.82f, 0.22f));
            UiKit.Place(line.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Side, -HeaderH + 14f), new Vector2(PanelW - 2 * Side, 1f));

            rowsHost = UiKit.NewRect("Rows", root);
            UiKit.Place(rowsHost, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -HeaderH), new Vector2(PanelW, 500f));

            root.gameObject.SetActive(false);
        }

        internal bool Visible => root && root.gameObject.activeSelf;

        internal void Toggle()
        {
            if (!root) return;
            bool show = !root.gameObject.activeSelf;
            if (show)
            {
                Rebuild();
                root.SetAsLastSibling(); // above the roster and the command card, not behind them
            }
            root.gameObject.SetActive(show);
        }

        internal void Hide()
        {
            if (root) root.gameObject.SetActive(false);
        }

        // Two columns of sections with "keys | what" rows. Every row is its own pair of text objects, so a long
        // description can never push the following rows out of line (it shrinks instead of wrapping).
        private void Rebuild()
        {
            title.text = Loc.Get("HIGHCOMMAND_HELP_TITLE");
            closeHint.text = Loc.Get("HIGHCOMMAND_HELP_CLOSE");

            for (int i = rowsHost.childCount - 1; i >= 0; i--)
                Object.Destroy(rowsHost.GetChild(i).gameObject);

            int column = 0;
            float y = 0f, tallest = 0f;
            int stripe = 0;
            for (int i = 0; i < Entries.Count; i++)
            {
                Entry e = Entries[i];
                float x = Side + column * (ColumnW + ColumnGap);
                switch (e.kind)
                {
                    case Kind.Column:
                        tallest = Mathf.Max(tallest, y);
                        column = Mathf.Min(1, column + 1);
                        y = 0f;
                        break;

                    case Kind.Section:
                    {
                        float top = y > 0f ? y + 10f : y;
                        TextMeshProUGUI head = UiKit.NewText("Section", rowsHost, e.what, 16f, UiKit.Accent, TextAlignmentOptions.BottomLeft);
                        head.fontStyle = FontStyles.UpperCase;
                        head.characterSpacing = 4f;
                        UiKit.Place(head.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -top), new Vector2(ColumnW, SectionH - 10f));
                        Image rule = UiKit.NewImage("Rule", rowsHost, UiKit.White, new Color(1f, 0.64f, 0.18f, 0.28f));
                        UiKit.Place(rule.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -(top + SectionH - 6f)), new Vector2(ColumnW, 1f));
                        y = top + SectionH;
                        stripe = 0;
                        break;
                    }

                    default:
                    {
                        if (stripe++ % 2 == 1)
                        {
                            Image band = UiKit.NewImage("Stripe", rowsHost, UiKit.White, Stripe);
                            UiKit.Place(band.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x - 8f, -y), new Vector2(ColumnW + 16f, RowH));
                        }
                        TextMeshProUGUI keys = UiKit.NewText("Keys", rowsHost, e.keys, 18f, KeyColor, TextAlignmentOptions.Left);
                        UiKit.Place(keys.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(KeysW - 12f, RowH));
                        Fit(keys);

                        TextMeshProUGUI what = UiKit.NewText("What", rowsHost, e.what, 18f, UiKit.Text, TextAlignmentOptions.Left);
                        UiKit.Place(what.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x + KeysW, -y), new Vector2(ColumnW - KeysW, RowH));
                        Fit(what);
                        y += RowH;
                        break;
                    }
                }
            }
            tallest = Mathf.Max(tallest, y);

            float height = HeaderH + tallest + FooterH;
            UiKit.Place(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PanelW, height));
            UiKit.Place(rowsHost, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -HeaderH), new Vector2(PanelW, tallest));

            // a big interface scale or a narrow screen: the whole sheet shrinks to fit instead of running off the sides
            var parent = root.parent as RectTransform;
            float room = parent ? parent.rect.width - 40f : PanelW;
            float roomH = parent ? parent.rect.height - 40f : height;
            float fit = Mathf.Min(1f, room / PanelW, roomH / height);
            root.localScale = new Vector3(fit, fit, 1f);
        }

        private static void Fit(TextMeshProUGUI text)
        {
            text.enableWordWrapping = false;
            text.enableAutoSizing = true;
            text.fontSizeMin = 13f;
            text.fontSizeMax = 18f;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = TextAlignmentOptions.MidlineLeft;
        }

        internal void Destroy()
        {
            if (root) Object.Destroy(root.gameObject);
        }
    }
}
