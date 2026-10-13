using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
    /// Bottom-right command card. The mouse already does most of the commanding (right click: move / attack / follow /
    /// use), so the card is short: the selected hero's skills (with cooldowns and the automatic-use switch), one
    /// "Orders" button that opens a list (attack-move, focus, stop, hold, retreat, patrol, rally, formation), a
    /// three-way stance switch; the commander's own abilities sit in a strip at the bottom centre. Every entry does
    /// what its hotkey does.
    /// </summary>
    internal sealed class CommandCard
    {
        private const float BtnW = 74f, Gap = 6f, Pad = 12f;
        private const float Head = 32f;      // the title strip above the buttons
        private const float RowH = 34f;      // the orders button / stance switch row
        private const float SkillH = 74f;
        private const float AbilityH = 60f;
        private const float CardW = 4 * BtnW + 3 * Gap + 2 * Pad;

        private const float MenuW = 236f, MenuRowH = 32f, MenuPad = 8f;
        private const float CancelW = 92f;

        private static readonly Regex Tags = new Regex("<[^>]+>", RegexOptions.Compiled);

        private sealed class OrderButton
        {
            internal UiButton button;
            internal OrderController.Act act;
            internal Func<string> hotkey;
            internal string nameToken;
            internal string descToken;
            internal bool inMenu;             // a row of the orders list (else a stance switch segment)
            internal byte shownFormation = 255; // formation row only: which name its label shows
            internal bool labelSet;
        }

        private sealed class SkillButton
        {
            internal UiButton button;
            internal Image autoDot;
            internal int slot;           // 1 secondary, 2 utility, 3 special, 4 equipment
            internal OrderController.Act act;
            internal Func<string> hotkey;

            // what the button shows now: the icon, name and description are looked up when the skill changes only
            internal object key;
            internal bool hasIcon;
            internal string name = "", desc = "";
            internal int badge = -1;
        }

        private sealed class AbilityButton
        {
            internal UiButton button;
            internal int index;
            internal int badge = -1;
        }

        // title line: rebuilt when the selection changes, not every frame
        private Hero titleHero;
        private int titleCount = -1;

        private readonly RectTransform root;
        private readonly TextMeshProUGUI title;
        private readonly RectTransform commanderRoot;
        private readonly TextMeshProUGUI commanderTitle;
        private readonly RectTransform menuRoot;
        private readonly UiButton menuButton;
        private readonly UiButton cancelButton;
        private readonly List<OrderButton> orderButtons = new List<OrderButton>();
        private readonly List<SkillButton> skillButtons = new List<SkillButton>();
        private readonly List<AbilityButton> abilityButtons = new List<AbilityButton>();
        private bool menuOpen;

        internal CommandCard(Transform parent)
        {
            // --- orders + skills ---
            float height = Head + RowH + Gap + SkillH + Pad + 4f;
            root = UiKit.NewPanel("CommandCard", parent, UiKit.PanelColor);
            UiKit.Place(root, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-14f, 14f), new Vector2(CardW, height));
            title = AddHeader(root);

            var inner = UiKit.NewRect("Buttons", root);
            UiKit.Stretch(inner, 0, 0, 0, 0);

            // the cancel button sits at the right end of the title strip (the title gives it room)
            cancelButton = new UiButton(inner, "Cancel", new Vector2(CancelW, 22f));
            cancelButton.SetPosition(CardW - Pad - CancelW, 5f);
            cancelButton.SetGlyph(Glyph.Cancel, 12f);
            cancelButton.SetRowLayout(12f, keyCap: false);
            cancelButton.Accent = new Color(1f, 0.45f, 0.4f, 0.8f);
            cancelButton.Label.text = Loc.Get("HIGHCOMMAND_CMD_CANCEL");
            title.rectTransform.sizeDelta = new Vector2(CardW - 2 * Pad - 10f - CancelW - 6f, 22f);

            // the orders button and the stance switch share one row
            float menuButtonW = 2 * BtnW + Gap;
            menuButton = new UiButton(inner, "OrdersMenu", new Vector2(menuButtonW, RowH));
            menuButton.SetPosition(Pad, Head);
            menuButton.SetGlyph(Glyph.ChevronDown, 14f);
            menuButton.SetRowLayout(14f, glyphAtRight: true, keyCap: false);
            menuButton.Accent = new Color(1f, 0.7f, 0.35f, 0.85f);
            menuButton.Label.text = Loc.Get("HIGHCOMMAND_MENU_ORDERS");

            float stanceX = Pad + menuButtonW + Gap;
            float stanceW = (CardW - Pad - stanceX - 2 * 5f) / 3f;
            AddStance(inner, stanceX, stanceW, 0, OrderController.Act.StanceAggressive, Glyph.Aggressive, "HIGHCOMMAND_CMD_STANCE_AGGRESSIVE", () => Key(Settings.KeyStanceAggressive));
            AddStance(inner, stanceX, stanceW, 1, OrderController.Act.StanceDefensive, Glyph.Defensive, "HIGHCOMMAND_CMD_STANCE_DEFENSIVE", () => Key(Settings.KeyStanceDefensive));
            AddStance(inner, stanceX, stanceW, 2, OrderController.Act.StancePassive, Glyph.Passive, "HIGHCOMMAND_CMD_STANCE_PASSIVE", () => Key(Settings.KeyStancePassive));

            float skillY = Head + RowH + Gap;
            AddSkill(inner, 0, skillY, 1, OrderController.Act.CastSecondary, () => Key(Settings.KeyCastSecondary));
            AddSkill(inner, 1, skillY, 2, OrderController.Act.CastUtility, () => Key(Settings.KeyCastUtility));
            AddSkill(inner, 2, skillY, 3, OrderController.Act.CastSpecial, () => Key(Settings.KeyCastSpecial));
            AddSkill(inner, 3, skillY, 4, OrderController.Act.CastEquipment, () => Key(Settings.KeyCastEquipment));

            // --- the orders list: opens above the button ---
            float menuH = MenuPad * 2 + 8 * MenuRowH;
            menuRoot = UiKit.NewPanel("OrdersList", parent, UiKit.PanelDark);
            UiKit.Place(menuRoot, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-(14f + CardW - MenuW), 14f + height - Head + 6f), new Vector2(MenuW, menuH));
            AddMenuRow(0, OrderController.Act.AttackMove, Glyph.AttackMove, "HIGHCOMMAND_CMD_ATTACKMOVE", () => Key(Settings.KeyAttackMove));
            AddMenuRow(1, OrderController.Act.Focus, Glyph.Focus, "HIGHCOMMAND_CMD_FOCUS", () => Key(Settings.KeyFocusFire));
            AddMenuRow(2, OrderController.Act.Stop, Glyph.Stop, "HIGHCOMMAND_CMD_STOP", () => Key(Settings.KeyStop));
            AddMenuRow(3, OrderController.Act.Hold, Glyph.Hold, "HIGHCOMMAND_CMD_HOLD", () => Key(Settings.KeyHold));
            AddMenuRow(4, OrderController.Act.Retreat, Glyph.Retreat, "HIGHCOMMAND_CMD_RETREAT", () => Key(Settings.KeyRetreat));
            AddMenuRow(5, OrderController.Act.Patrol, Glyph.Patrol, "HIGHCOMMAND_CMD_PATROL", () => Key(Settings.KeyPatrol));
            AddMenuRow(6, OrderController.Act.Rally, Glyph.Rally, "HIGHCOMMAND_CMD_RALLY", () => Key(Settings.KeyRally));
            AddMenuRow(7, OrderController.Act.Formation, Glyph.Formation, "HIGHCOMMAND_CMD_FORMATION", () => Key(Settings.KeyFormationCycle));

            // --- commander abilities: their own strip at the bottom centre (the selected heroes are already shown by
            // the roster on the left and the card's title) ---
            commanderRoot = UiKit.NewPanel("Commander", parent, UiKit.PanelColor);
            UiKit.Place(commanderRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(CardW, Head + AbilityH + Pad));
            commanderTitle = AddHeader(commanderRoot);

            Color[] colors =
            {
                new Color(1f, 0.5f, 0.25f, 0.9f),
                new Color(0.4f, 0.9f, 0.5f, 0.9f),
                new Color(1f, 0.85f, 0.3f, 0.9f),
                new Color(0.8f, 0.5f, 1f, 0.9f)
            };
            Glyph[] glyphs = { Glyph.OrbitalStrike, Glyph.Medic, Glyph.WarCry, Glyph.Mark };
            for (int i = 0; i < CommanderAbilities.Count; i++)
            {
                var b = new UiButton(commanderRoot, "Ability" + i, new Vector2(BtnW, AbilityH), withIcon: true);
                b.SetPosition(Pad + i * (BtnW + Gap), Head);
                b.Accent = colors[i];
                b.Icon.enabled = false;
                b.SetGlyph(glyphs[Mathf.Min(i, glyphs.Length - 1)], 18f);
                // a smaller picture leaves two full lines for names like "Orbital strike" / "Удар с орбиты"
                UiKit.Stretch(b.Label.rectTransform, 1, 2, 1, 24);
                b.Label.fontSizeMin = 10f;
                b.Label.fontSizeMax = 13f;
                b.Label.lineSpacing = -12f;
                b.Label.text = Loc.Get(CommanderAbilities.NameToken(i));
                b.SetHint(Key(Settings.KeyAbility[i]));
                abilityButtons.Add(new AbilityButton { button = b, index = i });
            }

            ClientInput.RegisterUiHitTest(Contains);
            root.gameObject.SetActive(false);
            commanderRoot.gameObject.SetActive(false);
            menuRoot.gameObject.SetActive(false);
        }

        private static string Key(BepInEx.Configuration.ConfigEntry<KeyCode> entry)
        {
            string s = entry.Value.ToString();
            if (s.StartsWith("Alpha")) s = s.Substring(5);
            return s;
        }

        /// <summary>The title strip of a card: a small orange bar, the text and a hairline under it.</summary>
        private static TextMeshProUGUI AddHeader(RectTransform panel)
        {
            Image bar = UiKit.NewImage("HeadBar", panel, UiKit.White, UiKit.Accent);
            UiKit.Place(bar.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Pad, -10f), new Vector2(3f, 15f));
            TextMeshProUGUI text = UiKit.NewText("Title", panel, "", 17f, UiKit.Text, TextAlignmentOptions.Left);
            UiKit.Place(text.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Pad + 10f, -7f), new Vector2(CardW - 2 * Pad - 10f, 22f));
            text.enableAutoSizing = true;
            text.fontSizeMin = 11f;
            text.fontSizeMax = 17f;
            Image line = UiKit.NewImage("HeadLine", panel, UiKit.White, new Color(0.6f, 0.7f, 0.82f, 0.22f));
            UiKit.Place(line.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Pad, -Head + 3f), new Vector2(CardW - 2 * Pad, 1f));
            return text;
        }

        private void AddStance(Transform parent, float x, float width, int index, OrderController.Act act, Glyph glyph, string nameToken, Func<string> hotkey)
        {
            var b = new UiButton(parent, "Stance_" + act, new Vector2(width, RowH));
            b.SetPosition(x + index * (width + 5f), Head);
            b.SetGlyph(glyph, 22f);
            // a picture only: re-centre it (no caption under it)
            UiKit.Place(b.GlyphImage.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22f, 22f));
            b.Accent = new Color(0.7f, 0.9f, 0.5f, 0.75f);
            orderButtons.Add(new OrderButton
            {
                button = b,
                act = act,
                hotkey = hotkey,
                nameToken = nameToken,
                descToken = nameToken + "_DESC",
                inMenu = false,
                labelSet = true
            });
        }

        private void AddMenuRow(int index, OrderController.Act act, Glyph glyph, string nameToken, Func<string> hotkey)
        {
            var b = new UiButton(menuRoot, "Order_" + act, new Vector2(MenuW - 2 * MenuPad, MenuRowH - 2f));
            b.SetPosition(MenuPad, MenuPad + index * MenuRowH);
            b.SetGlyph(glyph, 20f);
            b.SetRowLayout(20f);
            b.SetHint(hotkey());
            b.Accent = AccentOf(act);
            orderButtons.Add(new OrderButton
            {
                button = b,
                act = act,
                hotkey = hotkey,
                nameToken = nameToken,
                descToken = nameToken + "_DESC",
                inMenu = true
            });
        }

        private void AddSkill(Transform parent, int col, float y, int slot, OrderController.Act act, Func<string> hotkey)
        {
            var b = new UiButton(parent, "Skill_" + slot, new Vector2(BtnW, SkillH), withIcon: true);
            b.SetPosition(Pad + col * (BtnW + Gap), y);
            b.SetHint(hotkey());
            b.Label.text = "";

            Image dot = UiKit.NewImage("Auto", b.Root, UiKit.Dot, UiKit.Good);
            UiKit.Place(dot.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(4f, -4f), new Vector2(16f, 16f));
            skillButtons.Add(new SkillButton { button = b, autoDot = dot, slot = slot, act = act, hotkey = hotkey });
        }

        internal bool Contains(Vector2 screenPoint)
        {
            return (root.gameObject.activeSelf && UiKit.Contains(root, screenPoint))
                   || (commanderRoot.gameObject.activeSelf && UiKit.Contains(commanderRoot, screenPoint))
                   || (menuRoot.gameObject.activeSelf && UiKit.Contains(menuRoot, screenPoint));
        }

        internal bool MenuOpen => menuOpen;

        /// <summary>Closes the orders list when it is open (Esc). Returns true when it was.</summary>
        internal bool CloseMenu()
        {
            if (!menuOpen) return false;
            menuOpen = false;
            if (menuRoot) menuRoot.gameObject.SetActive(false);
            return true;
        }

        internal void Destroy()
        {
            ClientInput.UnregisterUiHitTest(Contains);
            if (root) UnityEngine.Object.Destroy(root.gameObject);
            if (commanderRoot) UnityEngine.Object.Destroy(commanderRoot.gameObject);
            if (menuRoot) UnityEngine.Object.Destroy(menuRoot.gameObject);
        }

        // ---- per frame -------------------------------------------------------------------------------------

        internal void Tick(Selection selection, OrderController orders, bool visible)
        {
            if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
            if (commanderRoot.gameObject.activeSelf != visible) commanderRoot.gameObject.SetActive(visible);
            if (!visible) menuOpen = false;
            bool menuShown = visible && menuOpen;
            if (menuRoot.gameObject.activeSelf != menuShown)
            {
                menuRoot.gameObject.SetActive(menuShown);
                if (menuShown) menuRoot.SetAsLastSibling();
            }
            if (!visible) return;

            Vector2 mouse = ClientInput.Mouse;
            bool click = ClientInput.MouseDown(0);
            bool toggleAuto = ClientInput.Alt;
            Hero first = selection.Heroes.Count > 0 ? selection.Heroes[0] : null;
            bool any = first != null;
            HcState state = HcState.Instance;

            int selected = selection.Count;
            if (titleHero != first || titleCount != selected)
            {
                titleHero = first;
                titleCount = selected;
                UiKit.SetText(title, any
                    ? Loc.Format("HIGHCOMMAND_CARD_TITLE", first.DisplayName) + (selected > 1 ? "  +" + (selected - 1) : "")
                    : Loc.Get("HIGHCOMMAND_CARD_NOSELECTION"));
            }

            // ---- the orders button ----
            menuButton.Enabled = true;
            menuButton.Active = menuOpen;
            bool menuHover = menuButton.Refresh(mouse);
            if (menuHover && !menuOpen) Tooltip.Show(Loc.Get("HIGHCOMMAND_MENU_ORDERS"), Loc.Get("HIGHCOMMAND_MENU_ORDERS_DESC"));
            bool pressedMenuButton = menuButton.Clicked(click);
            if (pressedMenuButton) menuOpen = !menuOpen;

            // ---- cancel: a target being chosen first (like Esc), else what the selected heroes were told to do ----
            bool choosing = orders.Mode != OrderController.Pending.None;
            cancelButton.Enabled = choosing || any;
            cancelButton.Active = false;
            if (cancelButton.Refresh(mouse))
                Tooltip.Show(Loc.Get("HIGHCOMMAND_CMD_CANCEL"), Loc.Get("HIGHCOMMAND_CMD_CANCEL_DESC") + "\n\n[Esc]  [" + Key(Settings.KeyStop) + "]");
            if (cancelButton.Clicked(click))
            {
                if (choosing) orders.CancelMode();
                else orders.Perform(OrderController.Act.Stop);
            }

            // ---- list rows and stance switch ----
            bool clickedRow = false;
            foreach (OrderButton o in orderButtons)
            {
                if (o.inMenu && !menuShown) continue; // a closed list is not touched

                UiButton b = o.button;
                bool global = o.act == OrderController.Act.Rally || o.act == OrderController.Act.Formation;
                b.Enabled = any || global;
                b.Active = IsActive(o.act, orders, first);
                if (o.act == OrderController.Act.Formation)
                {
                    if (o.shownFormation != orders.Formation)
                    {
                        o.shownFormation = orders.Formation;
                        UiKit.SetText(b.Label, Loc.Get(o.nameToken) + ": " + orders.FormationName);
                    }
                }
                else if (!o.labelSet)
                {
                    o.labelSet = true;
                    UiKit.SetText(b.Label, Loc.Get(o.nameToken));
                }
                bool hover = b.Refresh(mouse);
                if (hover) Tooltip.Show(Loc.Get(o.nameToken), Loc.Get(o.descToken) + "\n\n[" + o.hotkey() + "]");
                if (b.Clicked(click))
                {
                    orders.Perform(o.act);
                    if (o.inMenu) clickedRow = true;
                }
            }

            // a click on a row, or anywhere outside the list, closes it
            if (menuOpen && (clickedRow || (click && !pressedMenuButton && !menuHover && !UiKit.Contains(menuRoot, mouse))))
                CloseMenu();

            // ---- skills of the first selected hero ----
            CharacterBody body = first != null ? first.Body : null;
            foreach (SkillButton s in skillButtons) TickSkill(s, first, body, mouse, click, toggleAuto, orders);

            // ---- commander abilities ----
            UiKit.SetText(commanderTitle, Loc.Get("HIGHCOMMAND_COMMANDER_TITLE"));
            foreach (AbilityButton a in abilityButtons)
            {
                UiButton b = a.button;
                float left = CommanderAbilities.Remaining(state, a.index);
                float total = CommanderAbilities.Cooldown[a.index];
                b.Enabled = true;
                b.Active = orders.Mode == (CommanderAbilities.NeedsPoint(a.index) ? OrderController.Pending.AbilityPoint : OrderController.Pending.AbilityTarget)
                           && orders.AbilityIndex == a.index;
                b.Cooldown.fillAmount = left > 0f ? Mathf.Clamp01(left / total) : 0f;
                int badge = left > 0f ? Mathf.CeilToInt(left) : 0;
                if (a.badge != badge)
                {
                    a.badge = badge;
                    UiKit.SetText(b.Badge, badge > 0 ? badge.ToString() : "");
                }
                bool hover = b.Refresh(mouse);
                if (hover)
                {
                    string desc = Loc.Get(CommanderAbilities.DescToken(a.index));
                    Tooltip.Show(Loc.Get(CommanderAbilities.NameToken(a.index)),
                        desc + "\n\n" + Loc.Format("HIGHCOMMAND_ABILITY_COOLDOWN_INFO", Mathf.RoundToInt(total)) + "  [" + Key(Settings.KeyAbility[a.index]) + "]");
                }
                if (b.Clicked(click)) orders.UseAbility(a.index);
            }
        }

        private static bool IsActive(OrderController.Act act, OrderController orders, Hero first)
        {
            switch (act)
            {
                case OrderController.Act.AttackMove: return orders.Mode == OrderController.Pending.AttackMove;
                case OrderController.Act.Patrol: return orders.Mode == OrderController.Pending.Patrol;
                case OrderController.Act.Rally: return orders.Mode == OrderController.Pending.Rally;
                case OrderController.Act.Focus: return orders.Mode == OrderController.Pending.Focus;
                case OrderController.Act.StanceAggressive: return first != null && first.NetStance == (byte)Stance.Aggressive;
                case OrderController.Act.StanceDefensive: return first != null && first.NetStance == (byte)Stance.Defensive;
                case OrderController.Act.StancePassive: return first != null && first.NetStance == (byte)Stance.Passive;
                case OrderController.Act.Hold: return first != null && first.NetOrderKind == (byte)OrderKind.Hold;
                default: return false;
            }
        }

        private static Color AccentOf(OrderController.Act act)
        {
            switch (act)
            {
                case OrderController.Act.AttackMove:
                case OrderController.Act.Focus:
                case OrderController.Act.Patrol:
                    return new Color(1f, 0.55f, 0.3f, 0.7f);
                case OrderController.Act.Retreat:
                case OrderController.Act.Rally:
                    return new Color(0.5f, 0.8f, 1f, 0.7f);
                case OrderController.Act.StanceAggressive:
                case OrderController.Act.StanceDefensive:
                case OrderController.Act.StancePassive:
                    return new Color(0.7f, 0.9f, 0.5f, 0.7f);
                default:
                    return UiKit.Border;
            }
        }

        // ---- skill button ----------------------------------------------------------------------------------------

        private void TickSkill(SkillButton s, Hero hero, CharacterBody body, Vector2 mouse, bool click, bool toggleAuto, OrderController orders)
        {
            UiButton b = s.button;
            bool hasIcon = false;
            float cooldown = 0f, interval = 1f;
            object key = null;

            // The icon, name and description are looked up when the skill (or equipment) changes, not every frame.
            if (body)
            {
                if (s.slot == 4)
                {
                    Inventory inv = body.inventory;
                    EquipmentIndex eq = inv ? inv.currentEquipmentIndex : EquipmentIndex.None;
                    if (eq != EquipmentIndex.None)
                    {
                        EquipmentDef def = EquipmentCatalog.GetEquipmentDef(eq);
                        if (def != null)
                        {
                            key = def;
                            if (!ReferenceEquals(s.key, key))
                            {
                                s.key = key;
                                s.hasIcon = PickupIcons.Apply(b.Icon, PickupIcons.DefOf(eq));
                                s.name = Language.GetString(def.nameToken);
                                s.desc = Language.GetString(def.pickupToken);
                            }
                            else if (!s.hasIcon) s.hasIcon = PickupIcons.Apply(b.Icon, PickupIcons.DefOf(eq)); // not there yet: try again
                            hasIcon = s.hasIcon;
                            EquipmentState st = inv.currentEquipmentState;
                            if (st.isPerfomingRecharge) cooldown = st.chargeFinishTime.timeUntilClamped;
                            interval = Mathf.Max(1f, def.cooldown);
                        }
                    }
                }
                else if (body.skillLocator)
                {
                    GenericSkill skill = s.slot == 1 ? body.skillLocator.secondary : s.slot == 2 ? body.skillLocator.utility : body.skillLocator.special;
                    if (skill && skill.skillDef)
                    {
                        key = skill.skillDef;
                        if (!ReferenceEquals(s.key, key))
                        {
                            s.key = key;
                            s.hasIcon = PickupIcons.ApplySprite(b.Icon, skill.icon);
                            s.name = Language.GetString(skill.skillNameToken);
                            s.desc = Tags.Replace(Language.GetString(skill.skillDescriptionToken), "");
                        }
                        else if (!s.hasIcon) s.hasIcon = PickupIcons.ApplySprite(b.Icon, skill.icon);
                        hasIcon = s.hasIcon;
                        cooldown = hero.NetSkillCd[s.slot - 1];
                        interval = Mathf.Max(1f, skill.baseRechargeInterval);
                    }
                }
            }
            if (key == null) s.key = null;

            if (b.Icon.enabled != hasIcon) b.Icon.enabled = hasIcon;
            b.Icon.color = cooldown > 0f ? new Color(0.65f, 0.65f, 0.65f, 1f) : Color.white;
            b.Enabled = hasIcon;
            b.Cooldown.fillAmount = cooldown > 0f ? Mathf.Clamp01(cooldown / interval) : 0f;
            int badge = cooldown > 0f ? Mathf.CeilToInt(cooldown) : 0;
            if (s.badge != badge)
            {
                s.badge = badge;
                UiKit.SetText(b.Badge, badge > 0 ? badge.ToString() : "");
            }

            bool auto = hero == null || (hero.NetAutoMask & (1 << s.slot)) != 0;
            if (s.autoDot.enabled != hasIcon) s.autoDot.enabled = hasIcon;
            s.autoDot.color = auto ? UiKit.Good : UiKit.Bad;

            bool hover = b.Refresh(mouse);
            if (hover && hasIcon)
            {
                string autoText = Loc.Get(auto ? "HIGHCOMMAND_CARD_AUTO_ON" : "HIGHCOMMAND_CARD_AUTO_OFF");
                Tooltip.Show(s.name, s.desc + "\n\n" + autoText + "\n[" + s.hotkey() + "]  Alt+[" + s.hotkey() + "]", UiKit.Text);
            }
            if (b.Clicked(click))
            {
                // a click on the small dot (or Alt+click) flips the automatic use instead of casting
                bool onDot = UiKit.Contains(s.autoDot.rectTransform, mouse);
                orders.Perform(s.act, toggleAuto || onDot);
            }
        }
    }
}
