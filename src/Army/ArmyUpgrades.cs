using System;
using HarmonyLib;
using HighCommand.Mode;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace HighCommand.Army
{
    /// <summary>
    /// What gold buys besides heroes, and what heroes earn by fighting:
    ///  * army upgrades (barracks panel): weapons, armor, field medicine, logistics; every hero gets them, recruits too;
    ///  * veteran ranks: a hero's kills (its own and its minions') raise it to rank 1..3 at RankKills.
    /// The bonuses are added after the stock stat calculation (<see cref="UpgradeStatsPatch"/>), for army heroes only.
    /// Single player: the state lives on the host, the interface reads it directly.
    /// </summary>
    internal static class ArmyUpgrades
    {
        internal const int Weapons = 0, Armor = 1, Medicine = 2, Logistics = 3, Count = 4;

        internal static readonly int[] MaxLevel = { 5, 5, 5, 3 };
        private static readonly float[] BaseCost = { 160f, 140f, 120f, 180f };
        private const float CostGrowth = 1.6f;

        /// <summary>Kills needed for veteran rank 1, 2, 3.</summary>
        internal static readonly int[] RankKills = { 25, 75, 175 };

        // per level / per rank
        private const float DamagePerWeapons = 0.10f;
        private const float ArmorPerLevel = 8f;
        private const float RegenPerMedicine = 0.6f;     // HP/s at hero level 1, +20 % per hero level (the stock regen curve)
        private const float SpeedPerLogistics = 0.08f;
        private const float DamagePerRank = 0.10f;
        private const float ArmorPerRank = 5f;

        internal static readonly int[] Levels = new int[Count];

        /// <summary>Raised on the host when a hero reaches a new veteran rank.</summary>
        internal static event Action<Hero> RankedUp;

        internal static void Reset() => Array.Clear(Levels, 0, Levels.Length);

        internal static string NameToken(int index) => "HIGHCOMMAND_UPGRADE_" + index + "_NAME";
        internal static string DescToken(int index) => "HIGHCOMMAND_UPGRADE_" + index + "_DESC";

        /// <summary>Price of the next level (-1 = fully upgraded). Grows with the level and, like hero prices, with time and stages.</summary>
        internal static int Price(int index)
        {
            if (index < 0 || index >= Count || Levels[index] >= MaxLevel[index]) return -1;
            HcState state = HcState.Instance;
            float scale = state != null ? state.PriceScale() : 1f;
            return Mathf.Max(1, Mathf.RoundToInt(BaseCost[index] * Mathf.Pow(CostGrowth, Levels[index]) * scale));
        }

        /// <summary>Buys the next level. Null = done, else the reason as text.</summary>
        internal static string Buy(int index)
        {
            HighCommandRun run = HighCommandRun.Instance;
            if (!NetworkServer.active || run == null) return Loc.Get("HIGHCOMMAND_ERR_NORUN");
            int price = Price(index);
            if (price < 0) return Loc.Get("HIGHCOMMAND_UPGRADE_MAXED");
            if (!Economy.TrySpend((uint)price)) return Loc.Format("HIGHCOMMAND_RECRUIT_NEEDGOLD", price);
            Levels[index]++;
            RefreshStats(run.Army);
            Plugin.Log.LogInfo($"Upgrade {index} bought: level {Levels[index]} for ${price}");
            return null;
        }

        /// <summary>Server: an enemy died; the hero that killed it (or owns the minion that did) counts the kill.</summary>
        internal static void CountKill(DamageReport report)
        {
            CharacterMaster killer = report.attackerMaster;
            if (killer && HeroMarker.Of(killer) == null && killer.minionOwnership && killer.minionOwnership.ownerMaster)
                killer = killer.minionOwnership.ownerMaster;
            Hero hero = HeroMarker.Of(killer);
            if (hero == null) return;

            hero.Kills++;
            if (hero.Rank < RankKills.Length && hero.Kills >= RankKills[hero.Rank])
            {
                hero.Rank++;
                CharacterBody body = hero.Body;
                if (body) body.MarkAllStatsDirty();
                Plugin.Log.LogInfo($"{hero} reached veteran rank {hero.Rank} ({hero.Kills} kills)");
                try { RankedUp?.Invoke(hero); }
                catch (Exception e) { Plugin.Log.LogError("RankedUp handler failed: " + e); }
            }
        }

        private static void RefreshStats(ArmyManager army)
        {
            foreach (Hero h in army.Heroes)
            {
                CharacterBody body = h.Body;
                if (body) body.MarkAllStatsDirty();
            }
        }

        // ---- the bonuses -------------------------------------------------------------------------------------

        private static readonly Action<CharacterBody, float> SetDamage = Setter("damage");
        private static readonly Action<CharacterBody, float> SetArmor = Setter("armor");
        private static readonly Action<CharacterBody, float> SetRegen = Setter("regen");
        private static readonly Action<CharacterBody, float> SetMoveSpeed = Setter("moveSpeed");

        private static Action<CharacterBody, float> Setter(string property)
        {
            try
            {
                var method = AccessTools.PropertySetter(typeof(CharacterBody), property);
                return method != null ? AccessTools.MethodDelegate<Action<CharacterBody, float>>(method) : null;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"ArmyUpgrades: CharacterBody.{property} cannot be set ({e.Message}); that bonus is off.");
                return null;
            }
        }

        internal static void Apply(CharacterBody body)
        {
            Hero hero = HeroMarker.Of(body);
            if (hero == null) return;

            float damage = 1f + DamagePerWeapons * Levels[Weapons] + DamagePerRank * hero.Rank;
            float armor = ArmorPerLevel * Levels[Armor] + ArmorPerRank * hero.Rank;
            float regen = RegenPerMedicine * Levels[Medicine] * (1f + 0.2f * (body.level - 1f));
            float speed = 1f + SpeedPerLogistics * Levels[Logistics];

            if (damage != 1f) SetDamage?.Invoke(body, body.damage * damage);
            if (armor != 0f) SetArmor?.Invoke(body, body.armor + armor);
            if (regen != 0f) SetRegen?.Invoke(body, body.regen + regen);
            if (speed != 1f) SetMoveSpeed?.Invoke(body, body.moveSpeed * speed);
        }
    }

    /// <summary>Adds the army's upgrades and the hero's veteran rank after the stock stat calculation.</summary>
    [HarmonyPatch(typeof(CharacterBody), nameof(CharacterBody.RecalculateStats))]
    internal static class UpgradeStatsPatch
    {
        private static void Postfix(CharacterBody __instance)
        {
            if (!HighCommandRun.IsActive || !__instance || !__instance.master) return;
            try { ArmyUpgrades.Apply(__instance); }
            catch (Exception e) { Plugin.Log.LogError("ArmyUpgrades.Apply failed: " + e.Message); }
        }
    }
}
