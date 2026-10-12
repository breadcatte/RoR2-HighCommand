using System.Collections.Generic;
using HighCommand.Mode;
using HighCommand.Net;
using HighCommand.Orders;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace HighCommand.Army
{
    public enum RecruitStatus
    {
        Available,
        Locked,        // not unlocked on this profile
        NotAllowed,    // hidden / expansion not enabled / not owned
        AlreadyOwned,  // duplicates are off
        ArmyFull,
        NeedGold
    }

    /// <summary>Who may be recruited, for how much, and the act of recruiting itself.</summary>
    internal static class ArmyService
    {
        /// <summary>All survivors the commander may be shown, in the game's own order.</summary>
        internal static IEnumerable<SurvivorDef> Candidates()
        {
            foreach (SurvivorDef def in SurvivorCatalog.orderedSurvivorDefs)
            {
                if (!def || !def.bodyPrefab) continue;
                if (def.hidden && !Settings.AllowHiddenSurvivors.Value) continue;
                yield return def;
            }
        }

        /// <summary>Can this survivor be recruited right now? (Works on any machine; uses the replicated state.)</summary>
        internal static RecruitStatus Check(SurvivorDef def, IReadOnlyList<Hero> heroes, HcState state)
        {
            if (!def || state == null) return RecruitStatus.NotAllowed;

            if (!IsUnlocked(def)) return RecruitStatus.Locked;
            if (!def.CheckRequiredExpansionEnabled() || !def.CheckLocalUserHasRequiredEntitlement()) return RecruitStatus.NotAllowed;

            if (!Settings.AllowDuplicates.Value)
            {
                for (int i = 0; i < heroes.Count; i++)
                    if (heroes[i].Survivor == def) return RecruitStatus.AlreadyOwned;
            }

            if (heroes.Count >= state.ArmyLimit) return RecruitStatus.ArmyFull;
            if (state.Gold < (uint)state.NextHeroPrice()) return RecruitStatus.NeedGold;
            return RecruitStatus.Available;
        }

        internal static bool IsUnlocked(SurvivorDef def)
        {
            if (Settings.IgnoreUnlocks.Value) return true;
            if (!def.unlockableDef) return true;

            foreach (LocalUser user in LocalUserManager.readOnlyLocalUsersList)
            {
                if (user?.userProfile != null && user.userProfile.HasUnlockable(def.unlockableDef)) return true;
            }
            return false;
        }

        // ---- server --------------------------------------------------------------------------------------

        /// <summary>
        /// Server: recruits a survivor if everything checks out (gold, limit, availability). Returns null on success,
        /// otherwise the reason as a localised string.
        /// </summary>
        internal static string TryRecruit(SurvivorDef def, Loadout loadout)
        {
            // a client (or the host in relay-test mode) asks the server; the answer arrives as a toast
            if (CommandChannel.ShouldRelay) return CommandChannel.SendRecruit(def);
            return ExecRecruit(def, loadout);
        }

        /// <summary>Server: the recruiting itself (called directly on the host and by the command channel).</summary>
        internal static string ExecRecruit(SurvivorDef def, Loadout loadout)
        {
            HighCommandRun run = HighCommandRun.Instance;
            if (!NetworkServer.active || run == null) return Loc.Get("HIGHCOMMAND_ERR_NORUN");

            HcState state = run.State;
            RecruitStatus status = Check(def, run.Army.Heroes, state);
            if (status != RecruitStatus.Available) return StatusText(status, state);

            uint price = (uint)state.NextHeroPrice();
            if (!Economy.TrySpend(price)) return StatusText(RecruitStatus.NeedGold, state);

            // land a few meters from the rally point, not on top of the previous arrival
            Vector3 where = HighCommand.Orders.Formation.GroundNear(run.RallyPoint, 3f, 9f, run.Army.Heroes);
            Hero hero = run.RecruitHero(def, loadout, where, Quaternion.identity, withPod: true);
            if (hero == null)
            {
                Economy.Add(price, null, applyMultiplier: false); // refund
                return Loc.Get("HIGHCOMMAND_ERR_FAILED");
            }

            state.ServerSetBought(state.Bought + 1);
            run.PublishRoster();
            return null;
        }

        // ---- item actions ----------------------------------------------------------------------------------
        // Try*: the UI entry points (relay to the server when this machine is not the server).
        // Exec*: the server-side work (also called by the command channel). Both return null on success, else a text.
        // A relayed request returns CommandChannel.Relayed: the server answers with a toast of its own.

        /// <summary>Moves units of an item between two heroes.</summary>
        internal static string TryMoveItem(Hero from, Hero to, ItemIndex item, int count)
        {
            if (from == null || to == null) return Loc.Get("HIGHCOMMAND_ERR_FAILED");
            if (CommandChannel.ShouldRelay) return CommandChannel.SendMoveItem(from.Id, to.Id, item, count);
            return ExecMoveItem(from, to, item, count);
        }

        internal static string ExecMoveItem(Hero from, Hero to, ItemIndex item, int count)
        {
            HighCommandRun run = HighCommandRun.Instance;
            if (!NetworkServer.active || run == null) return Loc.Get("HIGHCOMMAND_ERR_NORUN");
            int moved = run.Items.Move(item, from, to, count);
            return moved > 0 ? null : Loc.Get("HIGHCOMMAND_ERR_FAILED");
        }

        internal static string TryMoveEquipment(Hero from, int fromSlot, Hero to)
        {
            if (from == null || to == null) return Loc.Get("HIGHCOMMAND_ERR_FAILED");
            if (CommandChannel.ShouldRelay) return CommandChannel.SendMoveEquipment(from.Id, fromSlot, to.Id);
            return ExecMoveEquipment(from, fromSlot, to);
        }

        internal static string ExecMoveEquipment(Hero from, int fromSlot, Hero to)
        {
            HighCommandRun run = HighCommandRun.Instance;
            if (!NetworkServer.active || run == null) return Loc.Get("HIGHCOMMAND_ERR_NORUN");
            return run.Items.MoveEquipment(from, fromSlot, to) ? null : Loc.Get("HIGHCOMMAND_ERR_FAILED");
        }

        internal static string TryEquipFromPool(int poolIndex, Hero to)
        {
            if (to == null) return Loc.Get("HIGHCOMMAND_ERR_FAILED");
            if (CommandChannel.ShouldRelay) return CommandChannel.SendEquipFromPool(poolIndex, to.Id);
            return ExecEquipFromPool(poolIndex, to);
        }

        internal static string ExecEquipFromPool(int poolIndex, Hero to)
        {
            HighCommandRun run = HighCommandRun.Instance;
            if (!NetworkServer.active || run == null) return Loc.Get("HIGHCOMMAND_ERR_NORUN");
            bool ok = run.Items.EquipFromPool(run.Army, poolIndex, to);
            if (ok) run.PublishRoster();
            return ok ? null : Loc.Get("HIGHCOMMAND_ERR_FAILED");
        }

        internal static string RedistributeItems()
        {
            if (CommandChannel.ShouldRelay) return CommandChannel.SendRedistribute();
            return ExecRedistribute();
        }

        internal static string ExecRedistribute()
        {
            HighCommandRun run = HighCommandRun.Instance;
            if (!NetworkServer.active || run == null) return Loc.Get("HIGHCOMMAND_ERR_NORUN");
            run.Items.RedistributeAll(run.Army);
            return null;
        }

        // ---- revival -----------------------------------------------------------------------------------------

        /// <summary>Revives a fallen hero for gold. Null = done (host) or sent (client: see CommandChannel.Relayed).</summary>
        internal static string TryRevive(Hero hero)
        {
            if (hero == null) return Loc.Get("HIGHCOMMAND_REVIVE_NONE");
            if (CommandChannel.ShouldRelay) return CommandChannel.SendRevive(hero.Id);
            return ExecRevive(hero);
        }

        /// <summary>The first fallen hero in join order, or null.</summary>
        internal static Hero FirstFallen(IReadOnlyList<Hero> heroes)
        {
            Hero best = null;
            for (int i = 0; i < heroes.Count; i++)
            {
                Hero h = heroes[i];
                if (h == null || h.IsAlive || !h.Master) continue;
                if (best == null || h.JoinOrder < best.JoinOrder) best = h;
            }
            return best;
        }

        internal static string ExecRevive(Hero hero)
        {
            HighCommandRun run = HighCommandRun.Instance;
            if (!NetworkServer.active || run == null) return Loc.Get("HIGHCOMMAND_ERR_NORUN");
            if (hero == null || !hero.Master) return Loc.Get("HIGHCOMMAND_ERR_FAILED");
            if (hero.IsAlive) return Loc.Get("HIGHCOMMAND_REVIVE_ALIVE");

            HcState state = run.State;
            int price = state.RevivePrice();
            if (!Economy.TrySpend((uint)price)) return Loc.Format("HIGHCOMMAND_RECRUIT_NEEDGOLD", price);

            // land near the rally point, like a recruit
            Vector3 where = HighCommand.Orders.Formation.GroundNear(run.RallyPoint, 2f, 8f, run.Army.Heroes);
            CharacterBody body = hero.Master.Respawn(where, Quaternion.identity, wasRevivedMidStage: true);
            if (!body)
            {
                Economy.Add((uint)price, null, applyMultiplier: false);
                return Loc.Get("HIGHCOMMAND_ERR_FAILED");
            }

            // the body comes back standing: no spawn animation, a short moment of immunity, the game's own revive effect
            body.AddTimedBuff(RoR2Content.Buffs.Immune, 3f);
            foreach (EntityStateMachine machine in body.GetComponents<EntityStateMachine>())
                machine.initialStateType = machine.mainStateType;
            GameObject effect = LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/HippoRezEffect");
            if (effect)
                EffectManager.SpawnEffect(effect, new EffectData { origin = where, rotation = body.transform.rotation }, transmit: true);

            run.PublishRoster();
            return null;
        }

        /// <summary>Uses one of the commander's abilities (host: right away; client: via the server).</summary>
        internal static string TryAbility(int index, Vector3 point, NetworkInstanceId target)
        {
            if (CommandChannel.ShouldRelay) return CommandChannel.SendAbility(index, point, target);
            return CommanderAbilities.Execute(index, point, target);
        }

        // ---- teleporter ----------------------------------------------------------------------------------------

        /// <summary>
        /// The one-button teleporter action: activate it (the nearest hero walks up and uses it), gather the army
        /// while it charges, or leave the stage once it is charged.
        /// </summary>
        internal static string TryTeleporter()
        {
            if (CommandChannel.ShouldRelay) return CommandChannel.SendTeleporter();
            return ExecTeleporter();
        }

        internal static string ExecTeleporter()
        {
            HighCommandRun run = HighCommandRun.Instance;
            if (!NetworkServer.active || run == null) return Loc.Get("HIGHCOMMAND_ERR_NORUN");

            TeleporterInteraction tp = TeleporterInteraction.instance;
            if (!tp) return Loc.Get("HIGHCOMMAND_TP_NONE");

            var alive = new List<Hero>();
            foreach (Hero h in run.Army.Heroes) if (h.IsAlive && h.Body && !h.Body.currentVehicle) alive.Add(h);
            if (alive.Count == 0) return Loc.Get("HIGHCOMMAND_TP_NOHEROES");

            Vector3 tpPos = tp.transform.position;
            alive.Sort((a, b) => (a.Position - tpPos).sqrMagnitude.CompareTo((b.Position - tpPos).sqrMagnitude));
            NetworkIdentity id = tp.GetComponent<NetworkIdentity>();

            switch (tp.activationState)
            {
                case TeleporterInteraction.ActivationState.Idle:
                case TeleporterInteraction.ActivationState.Charged:
                {
                    bool leaving = tp.activationState == TeleporterInteraction.ActivationState.Charged;
                    // the nearest hero uses it; everybody else walks up to it
                    OrderService.Execute(new OrderCommand
                    {
                        HeroIds = new[] { alive[0].Id },
                        Kind = OrderKind.Interact,
                        TargetId = id ? id.netId : NetworkInstanceId.Invalid
                    });
                    if (alive.Count > 1)
                    {
                        var others = new int[alive.Count - 1];
                        for (int i = 1; i < alive.Count; i++) others[i - 1] = alive[i].Id;
                        OrderService.Execute(new OrderCommand { HeroIds = others, Kind = OrderKind.AttackMove, Point = tpPos });
                    }
                    run.RallyPoint = HighCommand.Orders.Formation.SnapToGround(tpPos);
                    return Loc.Get(leaving ? "HIGHCOMMAND_TP_LEAVING" : "HIGHCOMMAND_TP_ACTIVATING");
                }

                case TeleporterInteraction.ActivationState.IdleToCharging:
                case TeleporterInteraction.ActivationState.Charging:
                {
                    var all = new int[alive.Count];
                    for (int i = 0; i < alive.Count; i++) all[i] = alive[i].Id;
                    OrderService.Execute(new OrderCommand { HeroIds = all, Kind = OrderKind.AttackMove, Point = tpPos });
                    return Loc.Get("HIGHCOMMAND_TP_GATHER");
                }

                default:
                    return Loc.Get("HIGHCOMMAND_TP_BUSY");
            }
        }

        internal static string StatusText(RecruitStatus status, HcState state)
        {
            switch (status)
            {
                case RecruitStatus.Locked: return Loc.Get("HIGHCOMMAND_RECRUIT_LOCKED");
                case RecruitStatus.NotAllowed: return Loc.Get("HIGHCOMMAND_RECRUIT_NOTALLOWED");
                case RecruitStatus.AlreadyOwned: return Loc.Get("HIGHCOMMAND_RECRUIT_OWNED");
                case RecruitStatus.ArmyFull: return Loc.Get("HIGHCOMMAND_RECRUIT_FULL");
                case RecruitStatus.NeedGold: return Loc.Format("HIGHCOMMAND_RECRUIT_NEEDGOLD", state != null ? state.NextHeroPrice() : 0);
                default: return "";
            }
        }

        /// <summary>The loadout (skills, skin) the player has saved for a survivor.</summary>
        internal static Loadout LoadoutFor(SurvivorDef def, NetworkUser owner = null)
        {
            var result = new Loadout();
            if (owner != null)
            {
                owner.networkLoadout.CopyLoadout(result);
                return result;
            }
            foreach (NetworkUser user in NetworkUser.readOnlyLocalPlayersList)
            {
                if (user == null) continue;
                user.networkLoadout.CopyLoadout(result);
                return result;
            }
            return result;
        }
    }
}
