using System.Collections.Generic;
using HighCommand.Mode;
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
            HighCommandRun run = HighCommandRun.Instance;
            if (!NetworkServer.active || run == null) return Loc.Get("HIGHCOMMAND_ERR_NORUN");

            HcState state = run.State;
            RecruitStatus status = Check(def, run.Army.Heroes, state);
            if (status != RecruitStatus.Available) return StatusText(status, state);

            uint price = (uint)state.NextHeroPrice();
            if (!Economy.TrySpend(price)) return StatusText(RecruitStatus.NeedGold, state);

            // land a few meters from the rally point, not on top of the previous arrival
            Vector2 ring = Random.insideUnitCircle.normalized * Random.Range(3f, 6f);
            Vector3 where = HighCommand.Orders.Formation.SnapToGround(run.RallyPoint + new Vector3(ring.x, 0f, ring.y));
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
        internal static Loadout LoadoutFor(SurvivorDef def)
        {
            var result = new Loadout();
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
