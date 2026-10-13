using System.Collections.Generic;
using System.Text.RegularExpressions;
using HighCommand.Army;
using HighCommand.Client;
using HighCommand.Mode;
using RoR2;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HighCommand.UI
{
    /// <summary>Recruiting panel: every survivor the game knows, its price and whether it can be bought now.</summary>
    internal sealed class BarracksPanel
    {
        private const int Columns = 7;
        private const float CardW = 152f;
        private const float CardH = 164f;
        private const float Gap = 8f;

        private sealed class Card
        {
            internal SurvivorDef def;
            internal RectTransform root;
            internal Image back;
            internal Image frame;
            internal RawImage portrait;
            internal TextMeshProUGUI name;
            internal TextMeshProUGUI role;
            internal TextMeshProUGUI status;
            internal Image band;
            internal RecruitStatus shown = (RecruitStatus)255;
            internal bool shownOver;
        }

        private const float HeadH = 62f;      // the title strip
        private const float DetailH = 62f;    // the description under everything

        private static readonly Color InArmy = new Color(0.55f, 0.8f, 1f, 1f);
        private static readonly Color BandBuy = new Color(0.2f, 0.45f, 0.24f, 0.9f);
        private static readonly Color BandPrice = new Color(0.28f, 0.22f, 0.08f, 0.85f);
        private static readonly Color BandArmy = new Color(0.12f, 0.24f, 0.36f, 0.85f);
        private static readonly Color BandNone = new Color(0.03f, 0.04f, 0.06f, 0.75f);

        private readonly RectTransform root;
        private readonly RectTransform grid;
        private readonly TextMeshProUGUI title;
        private readonly TextMeshProUGUI info;
        private readonly Image detailRule;
        private readonly TextMeshProUGUI detail;
        private static readonly IReadOnlyList<Hero> NoHeroes = new List<Hero>();

        private readonly List<Card> cards = new List<Card>();
        private int builtForCount = -1;
        private float panelWidth;
        private SurvivorDef hovered;

        // army upgrades: a strip of buttons under the survivor cards
        private sealed class UpgradeButton
        {
            internal RectTransform root;
            internal Image frame;
            internal TextMeshProUGUI text;
        }

        private const float UpgradeH = 58f;
        private readonly TextMeshProUGUI upgradeHeader;
        private readonly UpgradeButton[] upgrades = new UpgradeButton[ArmyUpgrades.Count];
        private int hoveredUpgrade = -1;

        internal bool Visible => root && root.gameObject.activeSelf;

        internal BarracksPanel(Transform parent)
        {
            root = UiKit.NewPanel("Barracks", parent, UiKit.PanelDark);
            float width = Columns * CardW + (Columns - 1) * Gap + 40f;
            UiKit.Place(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(width, 700f));
            panelWidth = width;

            // the title strip: the same accent bar and hairline as the cards of the interface, the purse on the right
            Image bar = UiKit.NewImage("HeadBar", root, UiKit.White, UiKit.Accent);
            UiKit.Place(bar.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -22f), new Vector2(4f, 24f));
            title = UiKit.NewText("Title", root, "", 26f, UiKit.Text, TextAlignmentOptions.Left);
            UiKit.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, -16f), new Vector2(400f, 36f));
            info = UiKit.NewText("Info", root, "", 19f, UiKit.TextDim, TextAlignmentOptions.Right);
            UiKit.Place(info.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-20f, -18f), new Vector2(width - 460f, 32f));
            info.enableWordWrapping = false;
            Image rule = UiKit.NewImage("HeadLine", root, UiKit.White, new Color(0.6f, 0.7f, 0.82f, 0.22f));
            UiKit.Place(rule.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -HeadH + 8f), new Vector2(width - 40f, 1f));

            grid = UiKit.NewRect("Grid", root);
            UiKit.Place(grid, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -(HeadH + 6f)), new Vector2(width - 40f, 520f));

            detailRule = UiKit.NewImage("DetailLine", root, UiKit.White, new Color(0.6f, 0.7f, 0.82f, 0.16f));
            UiKit.Place(detailRule.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, DetailH + 10f), new Vector2(width - 40f, 1f));
            detail = UiKit.NewText("Detail", root, "", 17f, UiKit.TextDim, TextAlignmentOptions.MidlineLeft);
            UiKit.Place(detail.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, 8f), new Vector2(width - 40f, DetailH));
            detail.enableWordWrapping = true;
            // long survivor descriptions shrink to fit instead of spilling below the panel
            detail.enableAutoSizing = true;
            detail.fontSizeMin = 11f;
            detail.fontSizeMax = 17f;

            upgradeHeader = UiKit.NewText("UpgradeHeader", root, Loc.Get("HIGHCOMMAND_UPGRADE_HEADER"), 15f, UiKit.Accent, TextAlignmentOptions.BottomLeft);
            upgradeHeader.fontStyle = FontStyles.UpperCase;
            upgradeHeader.characterSpacing = 3f;
            float buttonW = (width - 40f - (ArmyUpgrades.Count - 1) * Gap) / ArmyUpgrades.Count;
            for (int i = 0; i < ArmyUpgrades.Count; i++)
            {
                var b = new UpgradeButton { root = UiKit.NewRect("Upgrade_" + i, root) };
                b.root.sizeDelta = new Vector2(buttonW, UpgradeH);
                b.frame = b.root.gameObject.AddComponent<Image>();
                UiKit.StyleOutline(b.frame, 7f);
                b.frame.color = UiKit.Border;
                b.frame.raycastTarget = false;
                UiKit.NewFill("Back", b.root, new Color(0.06f, 0.08f, 0.11f, 0.95f), 7f);
                b.text = UiKit.NewText("Text", b.root, "", 18f, UiKit.Text, TextAlignmentOptions.Center);
                UiKit.Stretch(b.text.rectTransform, 4, 4, 2, 2);
                b.text.enableAutoSizing = true;
                b.text.fontSizeMin = 11f;
                b.text.fontSizeMax = 18f;
                upgrades[i] = b;
            }

            ClientInput.RegisterUiHitTest(Contains);
            root.gameObject.SetActive(false);
        }

        internal bool Contains(Vector2 screenPoint) => Visible && UiKit.Contains(root, screenPoint);

        internal void Toggle()
        {
            if (!root) return;
            bool show = !root.gameObject.activeSelf;
            if (show)
            {
                Build();
                root.SetAsLastSibling();
            }
            root.gameObject.SetActive(show);
        }

        internal void Hide()
        {
            if (root) root.gameObject.SetActive(false);
        }

        internal void Destroy()
        {
            ClientInput.UnregisterUiHitTest(Contains);
            if (root) Object.Destroy(root.gameObject);
        }

        private void Build()
        {
            foreach (Card c in cards) if (c.root) Object.Destroy(c.root.gameObject);
            cards.Clear();

            int index = 0;
            foreach (SurvivorDef def in ArmyService.Candidates())
            {
                var c = new Card { def = def };
                int col = index % Columns, row = index / Columns;
                c.root = UiKit.NewRect("Card_" + def.cachedName, grid);
                UiKit.Place(c.root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(col * (CardW + Gap), -row * (CardH + Gap)), new Vector2(CardW, CardH));

                c.frame = c.root.gameObject.AddComponent<Image>();
                UiKit.StyleOutline(c.frame, 9f);
                c.frame.color = UiKit.Border;
                c.frame.raycastTarget = false;

                c.back = UiKit.NewFill("Back", c.root, new Color(0.06f, 0.08f, 0.11f, 0.95f), 9f);

                c.portrait = UiKit.NewRawImage("Portrait", c.root, null, Color.white);
                UiKit.Place(c.portrait.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(78f, 78f));
                CharacterBody body = def.bodyPrefab.GetComponent<CharacterBody>();
                if (body) c.portrait.texture = body.portraitIcon;

                c.name = UiKit.NewText("Name", c.root, HeroName(def), 18f, UiKit.Text, TextAlignmentOptions.Center);
                UiKit.Place(c.name.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -88f), new Vector2(CardW - 12f, 24f));
                c.name.enableAutoSizing = true;
                c.name.fontSizeMin = 12f;
                c.name.fontSizeMax = 18f;
                c.name.enableWordWrapping = false;

                c.role = UiKit.NewText("Role", c.root, Loc.Get(HeroRoles.RoleToken(def.cachedName)), 14f, UiKit.TextDim, TextAlignmentOptions.Center);
                UiKit.Place(c.role.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -111f), new Vector2(CardW - 12f, 20f));

                // the status on a band along the bottom of the card (its colour says buy / too dear / in the army / no)
                c.band = UiKit.NewFill("Band", c.root, BandNone, UiKit.InnerCut(9f, 2f));
                c.band.rectTransform.anchorMax = new Vector2(1f, 0f);
                c.band.rectTransform.offsetMax = new Vector2(-2f, 30f);
                c.status = UiKit.NewText("Status", c.band.rectTransform, "", 16f, UiKit.Gold, TextAlignmentOptions.Center);
                UiKit.Stretch(c.status.rectTransform, 4, 1, 4, 1);
                c.status.enableAutoSizing = true;
                c.status.fontSizeMin = 11f;
                c.status.fontSizeMax = 16f;
                c.status.enableWordWrapping = false;

                cards.Add(c);
                index++;
            }
            builtForCount = index;

            // fit the panel to the number of rows
            int rows = Mathf.Max(1, (index + Columns - 1) / Columns);
            float gridHeight = rows * CardH + (rows - 1) * Gap;
            float top = HeadH + 6f;
            UiKit.Place(grid, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -top), new Vector2(panelWidth - 40f, gridHeight));

            // the upgrade strip right under the cards
            float y = -top - gridHeight - 16f;
            UiKit.Place(upgradeHeader.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, y), new Vector2(panelWidth - 40f, 24f));
            y -= 30f;
            for (int i = 0; i < upgrades.Length; i++)
            {
                RectTransform r = upgrades[i].root;
                UiKit.Place(r, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f + i * (r.sizeDelta.x + Gap), y), r.sizeDelta);
            }
            float upgradeBlock = 16f + 30f + UpgradeH;
            // the description sits right under the upgrades: no empty band at the bottom of the panel
            UiKit.Place(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f),
                new Vector2(panelWidth, top + gridHeight + upgradeBlock + 18f + DetailH + 18f));
        }

        private void TickUpgrades(Vector2 mouse)
        {
            hoveredUpgrade = -1;
            uint gold = HcState.Instance != null ? HcState.Instance.Gold : 0u;
            for (int i = 0; i < upgrades.Length; i++)
            {
                UpgradeButton b = upgrades[i];
                int price = ArmyUpgrades.Price(i);
                int level = ArmyUpgrades.Levels[i];
                int max = ArmyUpgrades.MaxLevel[i];
                string name = Loc.Get(ArmyUpgrades.NameToken(i));
                bool over = UiKit.Contains(b.root, mouse);
                if (over) hoveredUpgrade = i;

                bool affordable = price > 0 && gold >= (uint)price;
                // the name on top, the level and the price under it (the price green when it can be paid)
                string sub = price < 0
                    ? $"<color=#8C97A8>{Loc.Format("HIGHCOMMAND_UPGRADE_LEVEL", max, max)}   {Loc.Get("HIGHCOMMAND_UPGRADE_MAXSHORT")}</color>"
                    : $"<color=#8C97A8>{Loc.Format("HIGHCOMMAND_UPGRADE_LEVEL", level, max)}</color>   <color={(affordable ? "#7EE08A" : "#FFD94A")}>${price}</color>";
                UiKit.SetText(b.text, $"{name}\n<size=82%>{sub}</size>");
                b.text.color = price < 0 ? UiKit.TextDim : UiKit.Text;
                b.frame.color = affordable ? (over ? UiKit.Good : new Color(0.3f, 0.7f, 0.35f, 0.9f))
                    : over ? Color.Lerp(UiKit.Border, Color.white, 0.4f) : new Color(0.5f, 0.6f, 0.72f, 0.45f);

                if (over && ClientInput.MouseDown(0))
                {
                    // single player: the host buys it right here
                    string error = ArmyUpgrades.Buy(i);
                    if (error != null) Toast.Error(error);
                    else
                    {
                        Toast.Show(Loc.Format("HIGHCOMMAND_UPGRADE_DONE", name, ArmyUpgrades.Levels[i]));
                        Feedback.Confirm();
                    }
                }
            }
        }

        private static string HeroName(SurvivorDef def)
        {
            string token = def.displayNameToken;
            if (string.IsNullOrEmpty(token))
            {
                CharacterBody b = def.bodyPrefab ? def.bodyPrefab.GetComponent<CharacterBody>() : null;
                token = b ? b.baseNameToken : "";
            }
            return string.IsNullOrEmpty(token) ? def.cachedName : Hero.Plain(Language.GetString(token));
        }

        private static readonly Regex Tags = new Regex("<[^>]+>", RegexOptions.Compiled);

        internal void Tick()
        {
            if (!Visible) return;

            HcState state = HcState.Instance;
            IReadOnlyList<Hero> heroes = state != null ? state.Heroes : NoHeroes;
            Vector2 mouse = ClientInput.Mouse;

            UiKit.SetText(title, Loc.Get("HIGHCOMMAND_BARRACKS_NAME"));
            UiKit.SetText(info, Loc.Format("HIGHCOMMAND_BARRACKS_INFO", state != null ? state.NextHeroPrice() : 0, state != null ? state.Gold : 0u));

            hovered = null;
            foreach (Card c in cards)
            {
                if (!c.root) continue;
                RecruitStatus status = ArmyService.Check(c.def, heroes, state);
                bool over = UiKit.Contains(c.root, mouse);
                if (over) hovered = c.def;

                Color frame = over ? Color.Lerp(UiKit.Border, Color.white, 0.4f) : new Color(0.5f, 0.6f, 0.72f, 0.45f);
                Color band = BandNone;
                string text;
                switch (status)
                {
                    case RecruitStatus.Available:
                        frame = over ? UiKit.Good : new Color(0.3f, 0.7f, 0.35f, 0.9f);
                        band = BandBuy;
                        text = Loc.Format("HIGHCOMMAND_RECRUIT_BUY", state.NextHeroPrice());
                        c.status.color = Color.Lerp(UiKit.Good, Color.white, 0.35f);
                        break;
                    case RecruitStatus.NeedGold:
                        band = BandPrice;
                        text = Loc.Format("HIGHCOMMAND_RECRUIT_PRICE", state.NextHeroPrice());
                        c.status.color = UiKit.Gold;
                        break;
                    case RecruitStatus.AlreadyOwned:
                        frame = over ? InArmy : new Color(InArmy.r, InArmy.g, InArmy.b, 0.55f);
                        band = BandArmy;
                        text = ArmyService.StatusText(status, state);
                        c.status.color = InArmy;
                        break;
                    default:
                        text = ArmyService.StatusText(status, state);
                        c.status.color = UiKit.TextDim;
                        break;
                }
                if (c.shown != status || c.shownOver != over)
                {
                    c.shown = status;
                    c.shownOver = over;
                    c.frame.color = frame;
                    c.band.color = band;
                    bool closed = status == RecruitStatus.Locked || status == RecruitStatus.NotAllowed;
                    c.portrait.color = closed ? new Color(0.25f, 0.25f, 0.25f, 1f) : Color.white;
                    c.name.color = closed ? UiKit.TextDim : UiKit.Text;
                }
                UiKit.SetText(c.status, text);

                if (over && ClientInput.MouseDown(0) && status == RecruitStatus.Available)
                    Request(c.def);
                else if (over && ClientInput.MouseDown(0))
                    Toast.Error(ArmyService.StatusText(status, state));
            }

            TickUpgrades(mouse);

            if (hoveredUpgrade >= 0)
                UiKit.SetText(detail, Loc.Get(ArmyUpgrades.DescToken(hoveredUpgrade)));
            else if (hovered)
            {
                string desc = hovered.descriptionToken != null ? Language.GetString(hovered.descriptionToken) : "";
                desc = Tags.Replace(desc, "");
                if (desc.Length > 330) desc = desc.Substring(0, 330) + "...";
                UiKit.SetText(detail, desc);
            }
            else UiKit.SetText(detail, Loc.Get("HIGHCOMMAND_BARRACKS_HINT"));
        }

        private static void Request(SurvivorDef def)
        {
            // Host: recruited right here. Client: the request goes to the server, which answers with a toast.
            string error = ArmyService.TryRecruit(def, ArmyService.LoadoutFor(def));
            if (error == Net.CommandChannel.Relayed) return;
            if (error != null) Toast.Error(error);
            else
            {
                Toast.Show(Loc.Format("HIGHCOMMAND_RECRUITED", HeroName(def)));
                Feedback.Confirm();
            }
        }
    }
}
