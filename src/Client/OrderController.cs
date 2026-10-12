using System.Collections.Generic;
using HighCommand.Army;
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
        internal enum Pending { None, AttackMove, Focus, Patrol, Rally, Cast }

        private readonly Selection selection;
        private readonly Minimap minimap;
        private readonly WorldOverlay overlay;

        internal Pending Mode { get; private set; }
        private int castSlot;
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
                if (ClientInput.MouseDown(1) || ClientInput.RawKeyDown(KeyCode.Escape)) { CancelMode(); return; }
                if (ClientInput.MouseDown(0) && OrderState.WantsLeftClick) { ConfirmMode(heroes); return; }
            }
            else if (ClientInput.MouseDown(1))
            {
                ContextOrder(heroes);
            }
        }

        // ---- hotkeys -------------------------------------------------------------------------------

        private void HandleHotkeys(IReadOnlyList<Hero> heroes)
        {
            if (ClientInput.KeyDown(Settings.KeyFormationCycle.Value))
            {
                formation = (byte)((formation + 1) % 3);
                Toast.Show(Loc.Format("HIGHCOMMAND_FORMATION_SET", FormationName));
            }

            if (selection.Count == 0)
            {
                if (ClientInput.KeyDown(Settings.KeyRally.Value)) Mode = Pending.Rally;
                return;
            }

            bool toggleAuto = ClientInput.Key(Settings.KeyToggleAuto.Value);

            if (ClientInput.KeyDown(Settings.KeyStop.Value)) { SendSimple(OrderKind.Stop, false); CancelMode(); }
            else if (ClientInput.KeyDown(Settings.KeyHold.Value)) { SendSimple(OrderKind.Hold, ClientInput.Shift); CancelMode(); }
            else if (ClientInput.KeyDown(Settings.KeyRetreat.Value)) RetreatToRally();
            else if (ClientInput.KeyDown(Settings.KeyAttackMove.Value)) Mode = Pending.AttackMove;
            else if (ClientInput.KeyDown(Settings.KeyPatrol.Value)) Mode = Pending.Patrol;
            else if (ClientInput.KeyDown(Settings.KeyRally.Value)) Mode = Pending.Rally;
            else if (ClientInput.KeyDown(Settings.KeyFocusFire.Value))
            {
                CharacterBody hovered = overlay.HoveredEnemy;
                if (hovered) SendTargeted(OrderKind.Attack, hovered, focus: true, queued: ClientInput.Shift);
                else Mode = Pending.Focus;
            }
            else if (ClientInput.KeyDown(Settings.KeyStanceAggressive.Value)) SendStance(Stance.Aggressive);
            else if (ClientInput.KeyDown(Settings.KeyStanceDefensive.Value)) SendStance(Stance.Defensive);
            else if (ClientInput.KeyDown(Settings.KeyStancePassive.Value)) SendStance(Stance.Passive);
            else HandleCastKeys(toggleAuto);
        }

        private void HandleCastKeys(bool toggleAuto)
        {
            int slot = -1;
            if (ClientInput.KeyDown(Settings.KeyCastSecondary.Value)) slot = 1;
            else if (ClientInput.KeyDown(Settings.KeyCastUtility.Value)) slot = 2;
            else if (ClientInput.KeyDown(Settings.KeyCastSpecial.Value)) slot = 3;
            else if (ClientInput.KeyDown(Settings.KeyCastEquipment.Value)) slot = 4;
            if (slot < 0) return;

            if (toggleAuto)
            {
                // flip the automatic use of that slot for the whole selection (based on the first hero)
                bool current = true;
                var first = selection.Heroes.Count > 0 ? OrderService.BrainOf(selection.Heroes[0]) : null;
                if (first != null) current = first.AutoSlot[slot];
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
                        Feedback.Ping(point, new Color(0.4f, 0.8f, 1f, 1f), 1.2f);
                        Toast.Show(Loc.Get("HIGHCOMMAND_RALLY_SET"));
                    }
                    break;
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
