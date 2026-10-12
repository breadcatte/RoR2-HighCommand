using HarmonyLib;
using HighCommand.Mode;
using RoR2;
using RoR2.Stats;
using UnityEngine;

namespace HighCommand.Army
{
    /// <summary>
    /// The end-of-run screen shows the statistics of the player's master. The commander has no body and never fights,
    /// so the army's numbers are collected here and written to the commander's stat sheet just before the game
    /// generates the run report.
    /// </summary>
    internal static class RunStats
    {
        private static ulong kills;
        private static ulong damageDealt;
        private static ulong damageTaken;
        private static ulong deaths;
        private static ulong goldEarned;
        private static ulong items;
        private static float highestHit;
        private static int highestLevel = 1;
        private static bool subscribed;

        internal static void Reset()
        {
            kills = damageDealt = damageTaken = deaths = goldEarned = items = 0;
            highestHit = 0f;
            highestLevel = 1;
        }

        /// <summary>Remembers the best hero level seen so far (dead heroes have no body to ask at the end).</summary>
        internal static void NoteLevel(int level)
        {
            if (level > highestLevel) highestLevel = level;
        }

        internal static void Subscribe()
        {
            if (subscribed) return;
            subscribed = true;
            GlobalEventManager.onCharacterDeathGlobal += OnDeath;
            GlobalEventManager.onServerDamageDealt += OnDamage;
        }

        internal static void Unsubscribe()
        {
            if (!subscribed) return;
            subscribed = false;
            GlobalEventManager.onCharacterDeathGlobal -= OnDeath;
            GlobalEventManager.onServerDamageDealt -= OnDamage;
        }

        internal static void AddGold(uint amount) => goldEarned += amount;

        internal static void AddItem() => items++;

        private static bool IsHostile(TeamIndex team) => team == TeamIndex.Monster || team == TeamIndex.Lunar || team == TeamIndex.Void;

        private static void OnDeath(DamageReport report)
        {
            if (!HighCommandRun.IsActive || report == null) return;
            if (report.attackerTeamIndex == TeamIndex.Player && IsHostile(report.victimTeamIndex)) kills++;
            if (report.victimTeamIndex == TeamIndex.Player && report.victimBody && HeroMarker.Of(report.victimBody) != null) deaths++;
        }

        private static void OnDamage(DamageReport report)
        {
            if (!HighCommandRun.IsActive || report == null) return;
            if (report.attackerTeamIndex == TeamIndex.Player && IsHostile(report.victimTeamIndex))
            {
                damageDealt += (ulong)Mathf.Max(0f, report.damageDealt);
                highestHit = Mathf.Max(highestHit, report.damageDealt);
            }
            else if (report.victimTeamIndex == TeamIndex.Player)
            {
                damageTaken += (ulong)Mathf.Max(0f, report.damageDealt);
            }
        }

        /// <summary>Writes the army's numbers into the commander's sheet (once; called right before the report).</summary>
        internal static void WriteToCommander()
        {
            CharacterMaster commander = LootService.PoolMaster;
            PlayerStatsComponent component = commander ? commander.GetComponent<PlayerStatsComponent>() : null;
            if (!component || component.currentStats == null) return;

            StatSheet sheet = component.currentStats;
            sheet.PushStatValue(StatDef.totalKills, kills);
            sheet.PushStatValue(StatDef.totalDamageDealt, damageDealt);
            sheet.PushStatValue(StatDef.totalDamageTaken, damageTaken);
            sheet.PushStatValue(StatDef.highestDamageDealt, (ulong)Mathf.Max(0f, highestHit));
            sheet.PushStatValue(StatDef.totalDeaths, deaths);
            sheet.PushStatValue(StatDef.goldCollected, goldEarned);
            sheet.PushStatValue(StatDef.totalItemsCollected, items);

            Run run = Run.instance;
            if (run)
            {
                sheet.PushStatValue(StatDef.totalStagesCompleted, (ulong)Mathf.Max(0, run.stageClearCount));
                sheet.PushStatValue(StatDef.totalTimeAlive, (double)run.GetRunStopwatch());
            }

            HighCommandRun hc = HighCommandRun.Instance;
            if (hc != null)
            {
                int level = highestLevel;
                foreach (Hero h in hc.Army.Heroes)
                    if (h.Body) level = Mathf.Max(level, (int)h.Body.level);
                sheet.PushStatValue(StatDef.highestLevel, (ulong)level);
                if (hc.State != null) sheet.PushStatValue(StatDef.totalPurchases, (ulong)Mathf.Max(0, hc.State.Bought));
            }
        }
    }

    /// <summary>Fills the commander's sheet right before the stock game builds the report of the finished run.</summary>
    [HarmonyPatch(typeof(Run), nameof(Run.BeginGameOver))]
    internal static class GameOverStatsPatch
    {
        private static void Prefix(Run __instance)
        {
            if (!HighCommandRun.IsModeRun(__instance) || __instance.isGameOverServer) return;
            try { RunStats.WriteToCommander(); }
            catch (System.Exception e) { Plugin.Log.LogWarning("RunStats failed: " + e.Message); }
        }
    }
}
