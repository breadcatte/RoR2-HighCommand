using System.Collections.Generic;
using HighCommand.Army;
using HighCommand.Client;
using RoR2;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HighCommand.UI
{
    /// <summary>Bottom-centre strip of cards for the selected heroes: portrait, name, health, level.</summary>
    internal sealed class SelectionPanel
    {
        private const int MaxCards = 12;
        private const float CardWidth = 96f;
        private const float CardHeight = 118f;

        private sealed class Card
        {
            internal RectTransform root;
            internal RawImage portrait;
            internal TextMeshProUGUI name;
            internal TextMeshProUGUI level;
            internal Image healthFill;
            internal Image shieldFill;
            internal Image frame;

            // what is shown now (written only when it changes)
            internal int index = -1;
            internal Hero hero;
            internal float health = -1f, shield = -1f;
            internal int shownLevel = -2;
            internal bool grey;
        }

        private int shownCount = -1;

        private readonly RectTransform root;
        private readonly RectTransform content;
        private readonly TextMeshProUGUI summary;
        private readonly List<Card> cards = new List<Card>();

        internal SelectionPanel(Transform parent)
        {
            root = UiKit.NewPanel("SelectionPanel", parent, UiKit.PanelColor);
            UiKit.Place(root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(CardWidth * MaxCards + 24f, CardHeight + 16f));

            content = UiKit.NewRect("Content", root);
            UiKit.Stretch(content, 10, 8, 10, 8);

            summary = UiKit.NewText("Summary", root, "", 18f, UiKit.TextDim, TextAlignmentOptions.Left);
            UiKit.Place(summary.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(4f, 2f), new Vector2(600f, 22f));

            ClientInput.RegisterUiHitTest(Contains);
            root.gameObject.SetActive(false);
        }

        internal bool Contains(Vector2 screenPoint) => UiKit.Contains(root, screenPoint);

        internal void Destroy()
        {
            ClientInput.UnregisterUiHitTest(Contains);
            if (root) Object.Destroy(root.gameObject);
        }

        internal void Tick(Selection selection, bool visible)
        {
            var heroes = selection.Heroes;
            bool show = visible && heroes.Count > 0;
            if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
            if (!show) return;

            int shown = Mathf.Min(MaxCards, heroes.Count);
            if (shownCount != heroes.Count)
            {
                shownCount = heroes.Count;
                UiKit.SetText(summary, Loc.Format("HIGHCOMMAND_SELECTED_COUNT", heroes.Count));

                // keep the card strip centred: fit the panel to the card count
                UiKit.Place(root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f),
                    new Vector2(Mathf.Max(120f, CardWidth * shown + 24f), CardHeight + 16f));
            }

            for (int i = 0; i < shown; i++)
            {
                Card card = GetCard(i);
                if (!card.root.gameObject.activeSelf) card.root.gameObject.SetActive(true);
                if (card.index != i)
                {
                    card.index = i;
                    card.root.anchoredPosition = new Vector2(i * CardWidth, 0f);
                }
                Fill(card, heroes[i]);
            }
            for (int i = shown; i < cards.Count; i++)
                if (cards[i].root.gameObject.activeSelf) cards[i].root.gameObject.SetActive(false);
        }

        private Card GetCard(int index)
        {
            while (cards.Count <= index)
            {
                var c = new Card();
                c.root = UiKit.NewRect("Card", content);
                c.root.anchorMin = c.root.anchorMax = new Vector2(0f, 0f);
                c.root.pivot = new Vector2(0f, 0f);
                c.root.sizeDelta = new Vector2(CardWidth - 6f, CardHeight);

                c.frame = c.root.gameObject.AddComponent<Image>();
                UiKit.StyleOutline(c.frame, 7f);
                c.frame.color = new Color(0.4f, 1f, 0.5f, 0.9f);
                c.frame.raycastTarget = false;

                UiKit.NewFill("Back", c.root, new Color(0.05f, 0.06f, 0.08f, 0.95f), 7f);

                c.portrait = UiKit.NewRawImage("Portrait", c.root, null, Color.white);
                UiKit.Place(c.portrait.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -19f), new Vector2(56f, 56f));

                c.name = UiKit.NewText("Name", c.root, "", 15f, UiKit.Text, TextAlignmentOptions.Center);
                UiKit.Place(c.name.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(CardWidth - 8f, 20f));
                c.name.enableAutoSizing = true;
                c.name.fontSizeMin = 10f;
                c.name.fontSizeMax = 15f;

                c.level = UiKit.NewText("Level", c.root, "", 14f, UiKit.Gold, TextAlignmentOptions.TopLeft);
                UiKit.Place(c.level.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(9f, -4f), new Vector2(CardWidth - 20f, 16f));

                Image barBack = UiKit.NewImage("BarBack", c.root, UiKit.White, new Color(0.15f, 0.15f, 0.17f, 1f));
                UiKit.Place(barBack.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(CardWidth - 18f, 9f));
                c.healthFill = UiKit.NewImage("Health", barBack.rectTransform, UiKit.White, UiKit.Good);
                c.shieldFill = UiKit.NewImage("Shield", barBack.rectTransform, UiKit.White, new Color(0.35f, 0.65f, 1f));
                cards.Add(c);
            }
            return cards[index];
        }

        private static void Fill(Card card, Hero hero)
        {
            CharacterBody body = hero.Body;
            if (card.hero != hero)
            {
                card.hero = hero;
                card.shownLevel = -2;
                card.health = card.shield = -1f;
                UiKit.SetText(card.name, hero.DisplayName);
            }

            float h = 0f, s = 0f;
            bool grey = true;
            int level = -1;
            if (body)
            {
                if (card.portrait.texture != body.portraitIcon) card.portrait.texture = body.portraitIcon;
                level = (int)body.level;
                HealthComponent hc = body.healthComponent;
                float total = hc ? Mathf.Max(1f, hc.fullHealth + hc.fullShield) : 1f;
                h = hc ? Mathf.Clamp01(hc.health / total) : 0f;
                s = hc ? Mathf.Clamp01(hc.shield / total) : 0f;
                grey = !hero.IsAlive;
            }

            if (card.shownLevel != level)
            {
                card.shownLevel = level;
                UiKit.SetText(card.level, level >= 0 ? Loc.Format("HIGHCOMMAND_LEVEL_SHORT", level) : "");
            }
            if (Mathf.Abs(card.health - h) > 0.002f || Mathf.Abs(card.shield - s) > 0.002f)
            {
                card.health = h;
                card.shield = s;
                SetFill(card.healthFill.rectTransform, 0f, h);
                SetFill(card.shieldFill.rectTransform, h, Mathf.Min(1f, h + s));
            }
            if (card.grey != grey)
            {
                card.grey = grey;
                card.portrait.color = grey ? new Color(0.4f, 0.4f, 0.4f, 1f) : Color.white;
            }
        }

        private static void SetFill(RectTransform rt, float from, float to)
        {
            rt.anchorMin = new Vector2(from, 0f);
            rt.anchorMax = new Vector2(Mathf.Max(from, to), 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
