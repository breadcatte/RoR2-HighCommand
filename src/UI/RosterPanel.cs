using System.Collections.Generic;
using HighCommand.Army;
using HighCommand.Client;
using HighCommand.Mode;
using HighCommand.Orders;
using RoR2;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HighCommand.UI
{
    /// <summary>
    /// Left strip with the whole army: portrait, name, level, health, current order. Click selects (Shift adds),
    /// double click flies the camera there; a fallen hero's card shows the revival price and a click pays for it.
    /// Every card remembers what it showed last, so a quiet frame touches nothing (a touched text or rect would
    /// rebuild the whole canvas).
    /// </summary>
    internal sealed class RosterPanel
    {
        private const float Width = 244f;
        private const float MaxCardHeight = 58f;
        private const float MinCardHeight = 34f;

        private sealed class Card
        {
            internal RectTransform root;
            internal Image frame;
            internal Image fill;
            internal RawImage portrait;
            internal TextMeshProUGUI name;
            internal TextMeshProUGUI info;
            internal Image barBack;
            internal Image healthFill;
            internal Image shieldFill;

            // what is shown now
            internal Hero hero;
            internal int index = -1;
            internal float height;
            internal bool compact;
            internal float health = -1f, shield = -1f;
            internal int level = -1;
            internal byte order = 255;
            internal bool alive;
            internal bool stateKnown;
            internal int revivePrice = -1;
            internal bool affordable;
            internal string infoText;
            internal string infoKey;
        }

        private static readonly IReadOnlyList<Hero> NoHeroes = new List<Hero>();

        private readonly RectTransform root;
        private readonly List<Card> cards = new List<Card>();
        private readonly List<Hero> sortedFallback = new List<Hero>();
        private float lastClickTime;
        private Hero lastClickHero;

        internal RosterPanel(Transform parent)
        {
            root = UiKit.NewRect("Roster", parent);
            UiKit.Place(root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -92f), new Vector2(Width, 600f));
            ClientInput.RegisterUiHitTest(Contains);
            root.gameObject.SetActive(false);
        }

        internal bool Contains(Vector2 screenPoint)
        {
            if (!root.gameObject.activeSelf) return false;
            foreach (Card c in cards)
                if (c.root.gameObject.activeSelf && UiKit.Contains(c.root, screenPoint)) return true;
            return false;
        }

        internal void Destroy()
        {
            ClientInput.UnregisterUiHitTest(Contains);
            if (root) Object.Destroy(root.gameObject);
        }

        private Card GetCard(int index)
        {
            while (cards.Count <= index)
            {
                var c = new Card();
                c.root = UiKit.NewRect("Card", root);
                c.root.anchorMin = c.root.anchorMax = new Vector2(0f, 1f);
                c.root.pivot = new Vector2(0f, 1f);

                c.frame = c.root.gameObject.AddComponent<Image>();
                c.frame.sprite = UiKit.White;
                c.frame.color = UiKit.Border;
                c.frame.raycastTarget = false;

                c.fill = UiKit.NewImage("Fill", c.root, UiKit.White, new Color(0.05f, 0.06f, 0.08f, 0.9f));
                UiKit.Stretch(c.fill.rectTransform, 2, 2, 2, 2);

                c.portrait = UiKit.NewRawImage("Portrait", c.root, null, Color.white);
                c.name = UiKit.NewText("Name", c.root, "", 16f, UiKit.Text, TextAlignmentOptions.Left);
                c.name.enableAutoSizing = true;
                c.name.fontSizeMin = 9f;
                c.name.fontSizeMax = 16f;
                c.info = UiKit.NewText("Info", c.root, "", 13f, UiKit.TextDim, TextAlignmentOptions.Left);
                c.info.enableAutoSizing = true;
                c.info.fontSizeMin = 8f;
                c.info.fontSizeMax = 13f;

                c.barBack = UiKit.NewImage("BarBack", c.root, UiKit.White, new Color(0.15f, 0.15f, 0.17f, 1f));
                c.healthFill = UiKit.NewImage("Health", c.barBack.rectTransform, UiKit.White, UiKit.Good);
                c.shieldFill = UiKit.NewImage("Shield", c.barBack.rectTransform, UiKit.White, new Color(0.35f, 0.65f, 1f));
                cards.Add(c);
            }
            return cards[index];
        }

        // The army keeps its heroes in join order (the list a client rebuilds follows the same order); a copy is
        // sorted only in the unexpected case that the order is broken.
        private IReadOnlyList<Hero> InJoinOrder(IReadOnlyList<Hero> heroes)
        {
            for (int i = 1; i < heroes.Count; i++)
            {
                if (heroes[i].JoinOrder >= heroes[i - 1].JoinOrder) continue;
                sortedFallback.Clear();
                for (int k = 0; k < heroes.Count; k++) sortedFallback.Add(heroes[k]);
                sortedFallback.Sort((a, b) => a.JoinOrder.CompareTo(b.JoinOrder));
                return sortedFallback;
            }
            return heroes;
        }

        internal void Tick(Selection selection, OrderController orders, bool visible)
        {
            HcState state = HcState.Instance;
            IReadOnlyList<Hero> heroes = state != null ? InJoinOrder(state.Heroes) : NoHeroes;
            bool show = visible && heroes.Count > 0;
            if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
            if (!show) return;

            // the cards shrink so that up to 20 heroes fit above the minimap
            float available = 1080f - 92f - 300f;
            float height = Mathf.Clamp(available / Mathf.Max(1, heroes.Count) - 4f, MinCardHeight, MaxCardHeight);
            bool compact = height < 46f;

            Vector2 mouse = ClientInput.Mouse;
            bool click = ClientInput.MouseDown(0);
            int revivePrice = state != null ? state.RevivePrice() : 0;
            bool affordable = state != null && state.Gold >= (uint)revivePrice;

            for (int i = 0; i < heroes.Count; i++)
            {
                Hero hero = heroes[i];
                Card c = GetCard(i);
                if (!c.root.gameObject.activeSelf) c.root.gameObject.SetActive(true);

                if (c.index != i || c.height != height || c.compact != compact) Layout(c, i, height, compact);

                if (c.hero != hero)
                {
                    c.hero = hero;
                    c.stateKnown = false;
                    UiKit.SetText(c.name, hero.DisplayName);
                    c.portrait.texture = null;
                    c.level = -1;
                    c.order = 255;
                    c.infoKey = null;
                    c.revivePrice = -1;
                    c.health = c.shield = -1f;
                }

                CharacterBody body = hero.Body;
                bool alive = hero.IsAlive;

                // portrait: the live body's, or the survivor's prefab while the hero is down
                if (!c.portrait.texture)
                {
                    Texture icon = null;
                    if (body) icon = body.portraitIcon;
                    else if (hero.Survivor && hero.Survivor.bodyPrefab)
                    {
                        CharacterBody prefab = hero.Survivor.bodyPrefab.GetComponent<CharacterBody>();
                        if (prefab) icon = prefab.portraitIcon;
                    }
                    if (icon) c.portrait.texture = icon;
                }
                if (!c.stateKnown || c.alive != alive)
                {
                    c.stateKnown = true;
                    c.alive = alive;
                    c.portrait.color = alive ? Color.white : new Color(0.35f, 0.35f, 0.35f, 1f);
                    c.info.color = alive ? UiKit.Gold : (affordable ? UiKit.Good : UiKit.Bad);
                    c.infoKey = null;
                }

                // health and shield
                float h = 0f, s = 0f;
                if (alive && body)
                {
                    HealthComponent hc = body.healthComponent;
                    if (hc)
                    {
                        float total = Mathf.Max(1f, hc.fullHealth + hc.fullShield);
                        h = Mathf.Clamp01(hc.health / total);
                        s = Mathf.Clamp01(hc.shield / total);
                    }
                }
                if (Mathf.Abs(c.health - h) > 0.002f || Mathf.Abs(c.shield - s) > 0.002f)
                {
                    c.health = h;
                    c.shield = s;
                    SetFill(c.healthFill.rectTransform, 0f, h);
                    SetFill(c.shieldFill.rectTransform, h, Mathf.Min(1f, h + s));
                    c.healthFill.color = h > 0.5f ? UiKit.Good : h > 0.25f ? new Color(1f, 0.8f, 0.25f, 1f) : UiKit.Bad;
                }

                // info line: level and order, or the revival price
                if (alive)
                {
                    int level = body ? (int)body.level : 0;
                    byte order = hero.NetOrderKind;
                    int levelKey = level * 10 + hero.Rank; // a new veteran rank redraws the line too
                    if (c.level != levelKey || c.order != order || c.infoKey != "alive")
                    {
                        c.level = levelKey;
                        c.order = order;
                        c.infoKey = "alive";
                        string levelText = body ? Loc.Format("HIGHCOMMAND_LEVEL_SHORT", level) : "";
                        if (hero.Rank > 0) levelText += " <color=#FFD24A>" + new string('★', hero.Rank) + "</color>";
                        string orderText = OrderShort((OrderKind)order);
                        c.infoText = compact ? levelText : levelText + (orderText.Length > 0 ? "  <color=#A6B3C7>" + orderText + "</color>" : "");
                        UiKit.SetText(c.info, c.infoText);
                    }
                }
                else if (c.revivePrice != revivePrice || c.affordable != affordable || c.infoKey != "dead")
                {
                    c.revivePrice = revivePrice;
                    c.affordable = affordable;
                    c.infoKey = "dead";
                    c.level = -1;
                    c.order = 255;
                    UiKit.SetText(c.info, Loc.Format("HIGHCOMMAND_ROSTER_REVIVE", revivePrice));
                    c.info.color = affordable ? UiKit.Good : UiKit.Bad;
                }

                bool selected = selection.Contains(hero);
                bool over = UiKit.Contains(c.root, mouse);
                c.frame.color = selected ? UiKit.Good : over ? Color.white : (alive ? UiKit.Border : new Color(0.7f, 0.25f, 0.25f, 0.8f));

                if (over)
                {
                    ShowTooltip(hero, alive, revivePrice);
                    if (click) OnClick(hero, alive, selection, orders);
                }
            }
            for (int i = heroes.Count; i < cards.Count; i++)
                if (cards[i].root.gameObject.activeSelf)
                {
                    cards[i].root.gameObject.SetActive(false);
                    cards[i].hero = null;
                }
        }

        private static void Layout(Card c, int index, float height, bool compact)
        {
            c.index = index;
            c.height = height;
            c.compact = compact;
            c.infoKey = null; // the info line depends on the layout

            c.root.anchoredPosition = new Vector2(0f, -index * (height + 4f));
            c.root.sizeDelta = new Vector2(Width, height);

            float portrait = height - 8f;
            UiKit.Place(c.portrait.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(5f, 0f), new Vector2(portrait, portrait));
            float textX = portrait + 11f;
            float textW = Width - textX - 6f;
            UiKit.Place(c.name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(textX, -3f), new Vector2(textW, 20f));
            UiKit.Place(c.info.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(textX, compact ? -3f : -22f), new Vector2(textW, 16f));
            c.info.alignment = compact ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
            UiKit.Place(c.barBack.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(textX, 5f), new Vector2(textW, 7f));
        }

        private void OnClick(Hero hero, bool alive, Selection selection, OrderController orders)
        {
            if (!alive)
            {
                orders.Revive(hero);
                return;
            }

            float now = Time.unscaledTime;
            bool doubleClick = lastClickHero == hero && now - lastClickTime < 0.4f;
            lastClickHero = hero;
            lastClickTime = now;

            if (ClientInput.Shift) selection.Toggle(hero);
            else selection.SetSingle(hero);

            if (doubleClick && ClientRoot.Instance && ClientRoot.Instance.Camera)
                ClientRoot.Instance.Camera.FocusOn(hero.Position);
        }

        private static void ShowTooltip(Hero hero, bool alive, int revivePrice)
        {
            string role = Loc.Get(HeroRoles.RoleToken(hero.Survivor ? hero.Survivor.cachedName : ""));
            if (!alive)
            {
                Tooltip.Show(hero.DisplayName, role + "\n\n" + Loc.Format("HIGHCOMMAND_ROSTER_FALLEN", revivePrice));
                return;
            }

            var sb = new System.Text.StringBuilder(role);
            sb.Append("\n").Append(Loc.Get("HIGHCOMMAND_STANCE_" + ((Stance)hero.NetStance).ToString().ToUpperInvariant()));
            string order = OrderShort((OrderKind)hero.NetOrderKind);
            if (order.Length > 0) sb.Append("\n").Append(order);
            if (hero.NetQueued > 0) sb.Append(" (+").Append(hero.NetQueued).Append(")");
            sb.Append("\n\n").Append(Loc.Get("HIGHCOMMAND_ROSTER_HINT"));
            Tooltip.Show(hero.DisplayName, sb.ToString());
        }

        private static string OrderShort(OrderKind kind)
        {
            switch (kind)
            {
                case OrderKind.Move: return Loc.Get("HIGHCOMMAND_ORDER_MOVE");
                case OrderKind.AttackMove: return Loc.Get("HIGHCOMMAND_ORDER_ATTACKMOVE");
                case OrderKind.Attack: return Loc.Get("HIGHCOMMAND_ORDER_ATTACK");
                case OrderKind.Hold: return Loc.Get("HIGHCOMMAND_ORDER_HOLD");
                case OrderKind.Follow: return Loc.Get("HIGHCOMMAND_ORDER_FOLLOW");
                case OrderKind.Retreat: return Loc.Get("HIGHCOMMAND_ORDER_RETREAT");
                case OrderKind.Patrol: return Loc.Get("HIGHCOMMAND_ORDER_PATROL");
                case OrderKind.Interact: return Loc.Get("HIGHCOMMAND_ORDER_INTERACT");
                case OrderKind.Cast: return Loc.Get("HIGHCOMMAND_ORDER_CAST");
                default: return "";
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
