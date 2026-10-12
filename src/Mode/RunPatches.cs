using System.Collections.Generic;
using HarmonyLib;
using HighCommand.Army;
using RoR2;
using UnityEngine;

namespace HighCommand.Mode
{
    /// <summary>Rules the lobby must keep fixed for this mode (checked on the prefab, so it works before the run exists).</summary>
    [HarmonyPatch(typeof(Run), nameof(Run.OverrideRuleChoices))]
    internal static class RunRulePatch
    {
        private static void Postfix(Run __instance, RuleChoiceMask mustInclude, RuleChoiceMask mustExclude)
        {
            if (!HighCommandRun.IsModeRun(__instance)) return;

            // The commander has no body: nobody can "drop in" later, and the army stays the army.
            Force(mustInclude, mustExclude, "Misc.AllowDropIn.Off");
        }

        private static void Force(RuleChoiceMask mustInclude, RuleChoiceMask mustExclude, string choiceName)
        {
            RuleChoiceDef choice = RuleCatalog.FindChoiceDef(choiceName);
            if (choice == null) return;
            foreach (RuleChoiceDef other in choice.ruleDef.choices)
            {
                mustInclude[other.globalIndex] = false;
                mustExclude[other.globalIndex] = true;
            }
            mustInclude[choice.globalIndex] = true;
            mustExclude[choice.globalIndex] = false;
        }
    }

    /// <summary>
    /// Harmony patches that make the stock game cope with a commander who has no body. Each one checks
    /// <see cref="HighCommandRun.IsActive"/> first, so classic runs are untouched.
    /// </summary>
    internal static class RunPatches
    {
        /// <summary>The commander never gets a body: the army does the fighting.</summary>
        [HarmonyPatch(typeof(Stage), nameof(Stage.RespawnCharacter))]
        internal static class NoCommanderBody
        {
            private static bool Prefix(CharacterMaster characterMaster)
            {
                if (!HighCommandRun.IsActive || !characterMaster) return true;
                if (!characterMaster.GetComponent<PlayerCharacterMasterController>()) return true;
                return false;
            }
        }

        /// <summary>
        /// Everything in the game that scales by "players alive" (monster health, boss count, costs, run timer)
        /// should see the living heroes, not the bodiless commander.
        /// </summary>
        [HarmonyPatch(typeof(PlayerCharacterMasterController), nameof(PlayerCharacterMasterController.GetPlayersWithBodiesCount))]
        internal static class LivingPlayerCount
        {
            private static void Postfix(ref int __result)
            {
                HighCommandRun run = HighCommandRun.Instance;
                if (run == null) return;
                __result = run.Army.AliveCount;
            }
        }

        /// <summary>The combat director picks a player body to spawn monsters around; give it hero bodies.</summary>
        [HarmonyPatch(typeof(CombatDirector), "PickPlayerAsSpawnTarget")]
        internal static class DirectorSpawnTarget
        {
            private static readonly List<CharacterBody> Buffer = new List<CharacterBody>();

            private static bool Prefix(CombatDirector __instance)
            {
                HighCommandRun run = HighCommandRun.Instance;
                if (run == null) return true;

                Buffer.Clear();
                run.Army.CollectLivingBodies(Buffer);
                if (Buffer.Count > 0)
                    __instance.currentSpawnTarget = Buffer[Random.Range(0, Buffer.Count)].gameObject;
                Buffer.Clear();
                return false;
            }
        }
    }
}
