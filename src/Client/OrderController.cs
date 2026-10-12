using System.Collections.Generic;
using HighCommand.Army;
using HighCommand.Mode;
using HighCommand.Orders;
using HighCommand.UI;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace HighCommand.Client
{
    /// <summary>
    /// Turns commander input into orders: right click (context order), hotkeys, targeting modes (attack-move, focus
    /// fire, patrol, rally point, manual skill use) and stances. Confirms every order with a marker and a sound.
    /// </summary>
    internal sealed class OrderController
    {
        internal enum Pending { None, AttackMove, Focus, Patrol, Rally, Cast, AbilityPoint, AbilityTarget }

        private readonly Selection selection;
        private readonly Minimap minimap;
        private readonly WorldOverlay overlay;

        internal Pending Mode { get; private set; }
        private int castSlot;
        private int abilityIndex;

        /// <summary>The commander ability waiting for its target (valid while Mode is an ability mode).</summary>
        internal int AbilityIndex => abilityIndex;
        private byte formation;

        /// <summary>Where "retreat" runs to. Set at stage start and with the rally hotkey.</summary>
        internal Vector3? RallyPoint;

        internal OrderController(Selection selection, Minimap minimap, WorldOverlay overlay)
        {
            this.selection = selection;
            this.minimap = minimap;
            this.overlay = overlay;
        }

        internal byte Formation => formation;

        internal string FormationName
        {
            get
            {
                switch (formation)
                {
                    case Orders.Formation.Line: return Loc.Get("HIGHCOMMAND_FORMATION_LINE");
                    case Orders.Formation.Column: return Loc.Get("HIGHCOMMAND_FORMATION_COLUMN");
                    default: return Loc.Get("HIGHCOMMAND_FORMATION_BLOB");
                }
            }
        }

        /// <summary>Text for the targeting-mode hint next to the cursor ("" when there is no mode).</summary>
        internal string ModeHint
        {
            get
            {
                switch (Mode)
                {
                    case Pending.AttackMove: return Loc.Get("HIGHCOMMAND_HINT_ATTACKMOVE");
                    case Pending.Focus: return Loc.Get("HIGHCOMMAND_HINT_FOCUS");
                    case Pending.Patrol: return Loc.Get("HIGHCOMMAND_HINT_PATROL");
                    case Pending.Rally: return Loc.Get("HIGHCOMMAND_HINT_RALLY");
                    case Pending.Cast: return Loc.Get("HIGHCOMMAND_HINT_CAST");
                    case Pending.AbilityPoint: return Loc.Get("HIGHCOMMAND_HINT_ABILITY_POINT");
                    case Pending.AbilityTarget: return Loc.Get("HIGHCOMMAND_HINT_ABILITY_TARGET");
                    default: return "";
                }
            }
        }

        internal void CancelMode()
        {
            Mode = Pending.None;
            OrderState.WantsLeftClick = false;
        }

        internal void Tick(IReadOnlyList<Hero> heroes, bool inputAllowed)
        {
            OrderState.WantsLeftClick = false;
            if (!inputAllowed) { Mode = Pending.None; return; }

            if (!ClientInput.KeyboardBlocked) HandleHotkeys(heroes);

            // while a targeting mode waits for a click, the selection must not react to it
            OrderState.WantsLeftClick = Mode != Pending.None && !ClientInput.PointerOverUi;

            if (Mode != Pending.None)
            {
                // (Esc is handled by ClientRoot.ConsumeEscape, together with the panels)
                if (ClientInput.MouseDown(1)) { CancelMode(); return; }
                if (ClientInput.MouseDown(0) && OrderState.WantsLeftClick) { ConfirmMode(heroes); return; }
            }
            else if (ClientInput.MouseDown(1))
            {
                ContextOrder(heroes);
            }
        }

        // ---- hotkeys -------------------------------------------------------------------------------

        /// <summary>Every command the commander can give with a hotkey or a button of the command card.</summary>
        internal enum Act
        {
            Stop, Hold, Retreat, AttackMove, Patrol, Rally, Focus,
            StanceAggressive, StanceDefensive, StancePassive, Formation,
            CastSecondary, CastUtility, CastSpecial, CastEquipment
        }

        private void HandleHotkeys(IReadOnlyList<Hero> heroes)
        {
            // the configured key (LeftAlt by default); the synthetic modifier of the test commands counts as well
            bool toggleAuto = ClientInput.Key(Settings.KeyToggleAuto.Value)
                              || (ClientInput.Alt && (Settings.KeyToggleAuto.Value == KeyCode.LeftAlt || Settings.KeyToggleAuto.Value == KeyCode.RightAlt));

            for (int a = 0; a < CommanderAbilities.Count; a++)
            {
                if (ClientInput.KeyDown(Settings.KeyAbility[a].Value)) { UseAbility(a); return; }
            }
            if (ClientInput.KeyDown(Settings.KeyRevive.Value)) { Revive(null); return; }
            if (ClientInput.KeyDown(Settings.KeyTeleporter.Value)) { TeleporterAction(); return; }

            if (ClientInput.KeyDown(Settings.KeyFormationCycle.Value)) { Perform(Act.Formation); return; }
            if (ClientInput.KeyDown(Settings.KeyRally.Value)) { Perform(Act.Rally); return; }
            if (selection.Count == 0) return;

            if (ClientInput.KeyDown(Settings.KeyStop.Value)) Perform(Act.Stop);
            else if (ClientInput.KeyDown(Settings.KeyHold.Value)) Perform(Act.Hold);
            else if (ClientInput.KeyDown(Settings.KeyRetreat.Value)) Perform(Act.Retreat);
            else if (ClientInput.KeyDown(Settings.KeyAttackMove.Value)) Perform(Act.AttackMove);
            else if (ClientInput.KeyDown(Settings.KeyPatrol.Value)) Perform(Act.Patrol);
            else if (ClientInput.KeyDown(Settings.KeyFocusFire.Value)) Perform(Act.Focus);
            else if (ClientInput.KeyDown(Settings.KeyStanceAggressive.Value)) Perform(Act.StanceAggressive);
            else if (ClientInput.KeyDown(Settings.KeyStanceDefensive.Value)) Perform(Act.StanceDefensive);
            else if (ClientInput.KeyDown(Settings.KeyStancePassive.Value)) Perform(Act.StancePassive);
            else if (ClientInput.KeyDown(Settings.KeyCastSecondary.Value)) Perform(Act.CastSecondary, toggleAuto);
            else if (ClientInput.KeyDown(Settings.KeyCastUtility.Value)) Perform(Act.CastUtility, toggleAuto);
            else if (ClientInput.KeyDown(Settings.KeyCastSpecial.Value)) Perform(Act.CastSpecial, toggleAuto);
            else if (ClientInput.KeyDown(Settings.KeyCastEquipment.Value)) Perform(Act.CastEquipment, toggleAuto);
        }

        /// <summary>
        /// Does what the hotkey does (also called by the command card's buttons). <paramref name="toggleAuto"/> on a
        /// skill action flips that skill's automatic use instead of casting it.
        /// </summary>
        internal void Perform(Act act, bool toggleAuto = false)
        {
            switch (act)
            {
                case Act.Formation:
                    formation = (byte)((formation + 1) % 3);
                    Toast.Show(Loc.Format("HIGHCOMMAND_FORMATION_SET", FormationName));
                    return;
                case Act.Rally:
                    Mode = Pending.Rally;
                    return;
            }

            if (selection.Count == 0) return;

            switch (act)
            {
                case Act.Stop: SendSimple(OrderKind.Stop, false); CancelMode(); break;
                case Act.Hold: SendSimple(OrderKind.Hold, ClientInput.Shift); CancelMode(); break;
                case Act.Retreat: RetreatToRally(); break;
                case Act.AttackMove: Mode = Pending.AttackMove; break;
                case Act.Patrol: Mode = Pending.Patrol; break;
                case Act.Focus:
                {
                    CharacterBody hovered = overlay.HoveredEnemy;
                    if (hovered && !ClientInput.PointerOverUi) SendTargeted(OrderKind.Attack, hovered, focus: true, queued: ClientInput.Shift);
                    else Mode = Pending.Focus;
                    break;
                }
                case Act.StanceAggressive: SendStance(Stance.Aggressive); break;
                case Act.StanceDefensive: SendStance(Stance.Defensive); break;
                case Act.StancePassive: SendStance(Stance.Passive); break;
                case Act.CastSecondary: CastOrToggle(1, toggleAuto); break;
                case Act.CastUtility: CastOrToggle(2, toggleAuto); break;
                case Act.CastSpecial: CastOrToggle(3, toggleAuto); break;
                case Act.CastEquipment: CastOrToggle(4, toggleAuto); break;
            }
        }

        // ---- commander level: abilities, revival, teleporter ------------------------------------------------

        /// <summary>Starts one of the commander's abilities (some need a click on the map or on an enemy first).</summary>
        internal void UseAbility(int index)
        {
            if (index < 0 || index >= CommanderAbilities.Count) return;
            float left = CommanderAbilities.Remaining(HcState.Instance, index);
            if (left > 0.05f)
            {
                Toast.Show(Loc.Format("HIGHCOMMAND_ABILITY_COOLDOWN", Mathf.CeilToInt(left)));
                return;
            }

            if (CommanderAbilities.NeedsPoint(index)) { abilityIndex = index; Mode = Pending.AbilityPoint; return; }
            if (CommanderAbilities.NeedsTarget(index))
            {
                CharacterBody hovered = overlay.HoveredEnemy;
                if (hovered && !ClientInput.PointerOverUi) { SendAbility(index, hovered.corePosition, hovered); return; }
                abilityIndex = index;
                Mode = Pending.AbilityTarget;
                return;
            }
            SendAbility(index, Vector3.zero, null);
        }

        private void SendAbility(int index, Vector3 point, CharacterBody target)
        {
            NetworkInstanceId id = NetworkInstanceId.Invalid;
            if (target)
            {
                var ni = target.GetComponent<NetworkIdentity>();
                if (ni) id = ni.netId;
            }
            string result = ArmyService.TryAbility(index, point, id);
            if (result != null && result != Net.CommandChannel.Relayed) Toast.Error(result);
            else
            {
                if (point != Vector3.zero) Feedback.Ping(point, new Color(1f, 0.4f, 0.2f, 1f), 2.2f);
                Feedback.Confirm();
            }
        }

        /// <summary>Revives the given fallen hero, or the first fallen one when null.</summary>
        internal void Revive(Hero hero)
        {
            HcState state = HcState.Instance;
            if (hero == null && state != null) hero = ArmyService.FirstFallen(state.Heroes);
            if (hero == null) { Toast.Show(Loc.Get("HIGHCOMMAND_REVIVE_NONE")); return; }

            string result = ArmyService.TryRevive(hero);
            if (result == Net.CommandChannel.Relayed) return;
            if (result != null) { Toast.Error(result); return; }
            Toast.Show(Loc.Format("HIGHCOMMAND_REVIVED", hero.DisplayName));
            Feedback.Confirm();
        }

        internal void TeleporterAction()
        {
            string result = ArmyService.TryTeleporter();
            if (result != Net.CommandChannel.Relayed && !string.IsNullOrEmpty(result)) Toast.Show(result);
            else Feedback.Confirm();
        }

        private void CastOrToggle(int slot, bool toggleAuto)
        {
            if (toggleAuto)
            {
                // flip the automatic use of that slot for the whole selection (based on the first hero)
                bool current = selection.Heroes.Count == 0 || (selection.Heroes[0].NetAutoMask & (1 << slot)) != 0;
                var cmd = new OrderCommand { HeroIds = SelectedIds(), SetAutoSlot = true, Slot = slot, AutoValue = !current };
                OrderService.Submit(cmd);
                Toast.Show(Loc.Format(!current ? "HIGHCOMMAND_AUTO_ON" : "HIGHCOMMAND_AUTO_OFF", SlotName(slot)));
                return;
            }

            if (slot == 4)
            {
                // equipment needs no aim: use it at once
                var cmd = new OrderCommand { HeroIds = SelectedIds(), Kind = OrderKind.Cast, Slot = 4, Point = Vector3.zero };
                OrderService.Submit(cmd);
                Feedback.Confirm();
                return;
            }

            castSlot = slot;
            Mode = Pending.Cast;
        }

        // ---- orders -----------------------------------------------------------------------------------

        private void ContextOrder(IReadOnlyList<Hero> heroes)
        {
            if (selection.Count == 0) return;
            Vector2 mouse = ClientInput.Mouse;
            bool queued = ClientInput.Shift;

            // minimap: right click = move there
            Vector3? onMap = minimap.ScreenToWorld(mouse);
            if (onMap.HasValue)
            {
                SendPoint(OrderKind.Move, Orders.Formation.SnapToGround(WithGround(onMap.Value)), queued);
                return;
            }
            if (ClientInput.PointerOverUi) return;

            CharacterBody enemy = WorldPicker.PickEnemy(mouse);
            if (enemy)
            {
                SendTargeted(OrderKind.Attack, enemy, focus: ClientInput.Ctrl, queued: queued);
                return;
            }

            Hero friend = WorldPicker.PickHero(mouse, heroes);
            if (friend != null && !selection.Contains(friend) && friend.Body)
            {
                SendTargeted(OrderKind.Follow, friend.Body, focus: false, queued: queued);
                return;
            }

            GameObject interactable = WorldPicker.PickInteractable(mouse);
            if (interactable)
            {
                SendObject(OrderKind.Interact, interactable, queued);
                return;
            }

            if (WorldPicker.GroundPoint(mouse, out Vector3 ground))
                SendPoint(OrderKind.Move, ground, queued);
        }

        private void ConfirmMode(IReadOnlyList<Hero> heroes)
        {
            Vector2 mouse = ClientInput.Mouse;
            bool stay = ClientInput.Shift;
            Pending mode = Mode;

            Vector3? onMap = minimap.ScreenToWorld(mouse);
            Vector3 point = default;
            bool havePoint = false;
            if (onMap.HasValue) { point = Orders.Formation.SnapToGround(WithGround(onMap.Value)); havePoint = true; }
            else if (WorldPicker.GroundPoint(mouse, out Vector3 g)) { point = g; havePoint = true; }

            switch (mode)
            {
                case Pending.AttackMove:
                    if (havePoint) SendPoint(OrderKind.AttackMove, point, ClientInput.Shift);
                    break;
                case Pending.Patrol:
                    if (havePoint) SendPoint(OrderKind.Patrol, point, ClientInput.Shift);
                    break;
                case Pending.Focus:
                {
                    CharacterBody enemy = WorldPicker.PickEnemy(mouse);
                    if (enemy) SendTargeted(OrderKind.Attack, enemy, focus: true, queued: ClientInput.Shift);
                    else { Mode = Pending.Focus; return; } // nothing there: keep waiting
                    break;
                }
                case Pending.Rally:
                    if (havePoint)
                    {
                        RallyPoint = point;
                        OrderService.Submit(new OrderCommand { SetRally = true, Point = point });
                        Feedback.Ping(point, new Color(0.4f, 0.8f, 1f, 1f), 1.2f);
                        Toast.Show(Loc.Get("HIGHCOMMAND_RALLY_SET"));
                    }
                    break;
                case Pending.AbilityPoint:
                    if (havePoint) SendAbility(abilityIndex, point, null);
                    break;
                case Pending.AbilityTarget:
                {
                    CharacterBody enemy = WorldPicker.PickEnemy(mouse);
                    if (enemy) SendAbility(abilityIndex, enemy.corePosition, enemy);
                    else { Mode = Pending.AbilityTarget; return; } // nothing there: keep waiting
                    break;
                }
                case Pending.Cast:
                {
                    CharacterBody enemy = WorldPicker.PickEnemy(mouse);
                    var cmd = new OrderCommand { HeroIds = SelectedIds(), Kind = OrderKind.Cast, Slot = castSlot };
                    if (enemy)
                    {
                        cmd.Point = enemy.corePosition;
                        var id = enemy.GetComponent<NetworkIdentity>();
                        if (id) cmd.TargetId = id.netId;
                        Feedback.Ping(enemy.corePosition, new Color(1f, 0.5f, 0.2f, 1f), 1.2f);
                    }
                    else if (havePoint)
                    {
                        cmd.Point = point;
                        Feedback.Ping(point, new Color(1f, 0.5f, 0.2f, 1f), 1.2f);
                    }
                    else break;
                    OrderService.Submit(cmd);
                    Feedback.Confirm();
                    break;
                }
            }

            if (!stay) Mode = Pending.None;
        }

        private void RetreatToRally()
        {
            Vector3 where = RallyPoint ?? (ClientRoot.ArmyCenter() ?? Vector3.zero);
            SendPoint(OrderKind.Retreat, where, false);
        }

        // ---- sending ------------------------------------------------------------------------------------

        private int[] SelectedIds()
        {
            var heroes = selection.Heroes;
            var ids = new int[heroes.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = heroes[i].Id;
            return ids;
        }

        private void SendSimple(OrderKind kind, bool queued)
        {
            OrderService.Submit(new OrderCommand { HeroIds = SelectedIds(), Kind = kind, Queue = queued });
            Feedback.Confirm();
        }

        private void SendPoint(OrderKind kind, Vector3 point, bool queued)
        {
            var cmd = new OrderCommand { HeroIds = SelectedIds(), Kind = kind, Point = point, Queue = queued, Formation = formation };
            OrderService.Submit(cmd);

            Color color = kind == OrderKind.Move ? new Color(0.4f, 1f, 0.5f, 1f)
                        : kind == OrderKind.Retreat ? new Color(0.5f, 0.8f, 1f, 1f)
                        : new Color(1f, 0.55f, 0.3f, 1f);
            Feedback.Ping(point, color, 1.0f);
            Feedback.Confirm();
        }

        private void SendTargeted(OrderKind kind, CharacterBody target, bool focus, bool queued)
        {
            var id = target ? target.GetComponent<NetworkIdentity>() : null;
            if (!id) return;
            var cmd = new OrderCommand { HeroIds = SelectedIds(), Kind = kind, TargetId = id.netId, Focus = focus, Queue = queued, Point = target.corePosition };
            OrderService.Submit(cmd);

            Color color = kind == OrderKind.Attack ? new Color(1f, 0.3f, 0.25f, 1f) : new Color(0.4f, 1f, 0.5f, 1f);
            Feedback.Ping(target.corePosition, color, 1.1f);
            Feedback.Confirm();
        }

        private void SendObject(OrderKind kind, GameObject target, bool queued)
        {
            var id = target.GetComponent<NetworkIdentity>();
            if (!id) id = target.GetComponentInParent<NetworkIdentity>();
            if (!id) return;
            var cmd = new OrderCommand { HeroIds = SelectedIds(), Kind = kind, TargetId = id.netId, Queue = queued, Point = target.transform.position };
            OrderService.Submit(cmd);
            Feedback.Ping(target.transform.position, new Color(1f, 0.9f, 0.3f, 1f), 1.1f);
            Feedback.Confirm();
        }

        private void SendStance(Stance stance)
        {
            OrderService.Submit(new OrderCommand { HeroIds = SelectedIds(), NewStance = stance });
            Toast.Show(Loc.Format("HIGHCOMMAND_STANCE_SET", Loc.Get("HIGHCOMMAND_STANCE_" + stance.ToString().ToUpperInvariant())));
        }

        private static Vector3 WithGround(Vector3 xz)
        {
            return new Vector3(xz.x, RtsCamera.GroundHeight(xz, xz.y), xz.z);
        }

        private static string SlotName(int slot)
        {
            switch (slot)
            {
                case 0: return Loc.Get("HIGHCOMMAND_SLOT_PRIMARY");
                case 1: return Loc.Get("HIGHCOMMAND_SLOT_SECONDARY");
                case 2: return Loc.Get("HIGHCOMMAND_SLOT_UTILITY");
                case 3: return Loc.Get("HIGHCOMMAND_SLOT_SPECIAL");
                default: return Loc.Get("HIGHCOMMAND_SLOT_EQUIPMENT");
            }
        }
    }

    /// <summary>Order confirmation: markers on the ground and a click sound.</summary>
    internal static class Feedback
    {
        internal struct PingMarker
        {
            internal Vector3 position;
            internal Color color;
            internal float born;
            internal float life;
            internal float size;
        }

        internal static readonly List<PingMarker> Pings = new List<PingMarker>(16);

        internal struct PopupText
        {
            internal string text;
            internal Vector3 position;
            internal Color color;
            internal float born;
        }

        internal static readonly List<PopupText> Popups = new List<PopupText>(16);

        internal static void Popup(string text, Vector3 world, Color color)
        {
            if (Popups.Count > 40) Popups.RemoveAt(0);
            Popups.Add(new PopupText { text = text, position = world, color = color, born = Time.unscaledTime });
        }

        internal static void Ping(Vector3 world, Color color, float size)
        {
            Pings.Add(new PingMarker { position = world, color = color, born = Time.unscaledTime, life = 1.0f, size = size });
        }

        internal static void Confirm()
        {
            try { Util.PlaySound("Play_UI_menuClick", RoR2Application.instance ? RoR2Application.instance.gameObject : null); }
            catch (System.Exception) { /* sounds are a nicety */ }
        }
    }
}
