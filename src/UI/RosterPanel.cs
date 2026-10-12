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
            internal Image selectedMark;
        }

        private readonly RectTransform root;
        private readonly List<Card> cards = new List<Card>();
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

        internal void Tick(Selection selection, OrderController orders, bool visible)
        {
            HcState state = HcState.Instance;
            IReadOnlyList<Hero> heroes = state != null ? state.Heroes : new List<Hero>();
            bool show = visible && heroes.Count > 0;
            if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
            if (!show) return;

            // the list is sorted by join order; the cards shrink so that up to 20 heroes fit above the minimap
            var sorted = new List<Hero>(heroes);
            sorted.Sort((a, b) => a.JoinOrder.CompareTo(b.JoinOrder));

            float available = 1080f - 92f - 300f;
            float height = Mathf.Clamp(available / Mathf.Max(1, sorted.Count) - 4f, MinCardHeight, MaxCardHeight);
            bool compact = height < 46f;

            Vector2 mouse = ClientInput.Mouse;
            bool click = ClientInput.MouseDown(0);
            int revivePrice = state != null ? state.RevivePrice() : 0;

            for (int i = 0; i < sorted.Count; i++)
            {
                Hero hero = sorted[i];
                Card c = GetCard(i);
                if (!c.root.gameObject.activeSelf) c.root.gameObject.SetActive(true);

                c.root.anchoredPosition = new Vector2(0f, -i * (height + 4f));
                c.root.sizeDelta = new Vector2(Width, height);

                float portrait = height - 8f;
                UiKit.Place(c.portrait.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(5f, 0f), new Vector2(portrait, portrait));
                float textX = portrait + 11f;
                float textW = Width - textX - 6f;
                UiKit.Place(c.name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(textX, -3f), new Vector2(textW, 20f));
                UiKit.Place(c.info.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(textX, compact ? -3f : -22f), new Vector2(textW, 16f));
                if (compact) c.info.alignment = TextAlignmentOptions.Right;
                else c.info.alignment = TextAlignmentOptions.Left;
                UiKit.Place(c.barBack.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(textX, 5f), new Vector2(textW, 7f));

                CharacterBody body = hero.Body;
                bool alive = hero.IsAlive;
                bool selected = selection.Contains(hero);

                c.name.text = hero.DisplayName;
                if (body)
                {
                    if (c.portrait.texture != body.portraitIcon) c.portrait.texture = body.portraitIcon;
                }
                else if (hero.Survivor && hero.Survivor.bodyPrefab)
                {
                    CharacterBody prefab = hero.Survivor.bodyPrefab.GetComponent<CharacterBody>();
                    if (prefab && c.portrait.texture != prefab.portraitIcon) c.portrait.texture = prefab.portraitIcon;
                }
                c.portrait.color = alive ? Color.white : new Color(0.35f, 0.35f, 0.35f, 1f);

                float h = 0f, s = 0f;
                if (alive && body && body.healthComponent)
                {
                    HealthComponent hc = body.healthComponent;
                    float total = Mathf.Max(1f, hc.fullHealth + hc.fullShield);
                    h = Mathf.Clamp01(hc.health / total);
                    s = Mathf.Clamp01(hc.shield / total);
                }
                SetFill(c.healthFill.rectTransform, 0f, h);
                SetFill(c.shieldFill.rectTransform, h, Mathf.Min(1f, h + s));
                c.healthFill.color = h > 0.5f ? UiKit.Good : h > 0.25f ? new Color(1f, 0.8f, 0.25f, 1f) : UiKit.Bad;

                if (alive)
                {
                    string level = body ? Loc.Format("HIGHCOMMAND_LEVEL_SHORT", (int)body.level) : "";
                    string order = OrderShort((OrderKind)hero.NetOrderKind);
                    c.info.text = compact ? level : level + (order.Length > 0 ? "  <color=#A6B3C7>" + order + "</color>" : "");
                    c.info.color = UiKit.Gold;
                }
                else
                {
                    c.info.text = Loc.Format("HIGHCOMMAND_ROSTER_REVIVE", revivePrice);
                    c.info.color = state != null && state.Gold >= (uint)revivePrice ? UiKit.Good : UiKit.Bad;
                }

                bool over = UiKit.Contains(c.root, mouse);
                c.frame.color = selected ? UiKit.Good : over ? Color.white : (alive ? UiKit.Border : new Color(0.7f, 0.25f, 0.25f, 0.8f));

                if (over)
                {
                    ShowTooltip(hero, alive, revivePrice);
                    if (click) OnClick(hero, alive, selection, orders);
                }
            }
            for (int i = sorted.Count; i < cards.Count; i++)
                if (cards[i].root.gameObject.activeSelf) cards[i].root.gameObject.SetActive(false);
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
