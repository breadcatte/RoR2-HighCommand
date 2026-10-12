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

        /// <summary>
        /// Holdout zones (teleporter, pillars ...) charge only for "player controlled" bodies. The heroes are AI
        /// bodies, so the zone would never charge: count living heroes instead.
        /// </summary>
        [HarmonyPatch(typeof(HoldoutZoneController), nameof(HoldoutZoneController.CountPlayersInRadius))]
        internal static class ZoneCountInRadius
        {
            private static bool Prefix(HoldoutZoneController holdoutZoneController, Vector3 origin, float chargingRadiusSqr,
                TeamIndex teamIndex, ref int __result)
            {
                if (!HighCommandRun.IsActive) return true;
                int n = 0;
                foreach (TeamComponent tc in TeamComponent.GetTeamMembers(teamIndex))
                {
                    CharacterBody body = tc ? tc.body : null;
                    if (!body || HeroMarker.Of(body) == null) continue;
                    if (!body.healthComponent || !body.healthComponent.alive) continue;
                    if (HoldoutZoneController.IsBodyInChargingRadius(holdoutZoneController, origin, chargingRadiusSqr, body)) n++;
                }
                __result = n;
                return false;
            }
        }

        [HarmonyPatch(typeof(HoldoutZoneController), "CountLivingPlayers")]
        internal static class ZoneCountLiving
        {
            private static bool Prefix(TeamIndex teamIndex, ref int __result)
            {
                if (!HighCommandRun.IsActive) return true;
                int n = 0;
                foreach (TeamComponent tc in TeamComponent.GetTeamMembers(teamIndex))
                {
                    CharacterBody body = tc ? tc.body : null;
                    if (!body || HeroMarker.Of(body) == null) continue;
                    if (body.healthComponent && body.healthComponent.alive) n++;
                }
                __result = n;
                return false;
            }
        }

        /// <summary>
        /// The game scales monster budgets, boss counts, loot and the difficulty clock by "players in the run". The
        /// commander is one player, so the army's size is mapped to an equivalent number of players.
        /// </summary>
        [HarmonyPatch(typeof(Run), nameof(Run.participatingPlayerCount), MethodType.Getter)]
        internal static class ParticipatingPlayers
        {
            private static void Postfix(ref int __result)
            {
                if (!HighCommandRun.IsActive) return;
                HcState state = HcState.Instance;
                int heroes = state != null ? state.Heroes.Count : 0;
                if (heroes <= 1) { __result = Mathf.Max(1, __result); return; }
                float equivalent = 1f + (heroes - 1) * LobbyRules.ThreatPerExtraHero;
                __result = Mathf.Max(1, Mathf.RoundToInt(equivalent));
            }
        }

        /// <summary>
        /// The platform activity manager knows only the stock game modes and logs an error for any other one. A
        /// High Command run reports "any mode" (the stock fallback) quietly.
        /// </summary>
        [HarmonyPatch(typeof(Assets.RoR2.Scripts.Platform.ActivityManager), nameof(Assets.RoR2.Scripts.Platform.ActivityManager.CurrentGameMode),
            MethodType.Getter)]
        internal static class ActivityGameMode
        {
            private static bool Prefix(ref Assets.RoR2.Scripts.Platform.BaseActivity.ActivityGameMode __result)
            {
                bool ours = false;
                Run run = Run.instance;
                if (run) ours = HighCommandRun.IsModeRun(run);
                else
                {
                    PreGameController pre = PreGameController.instance;
                    if (pre) ours = HighCommandRun.IsModeRun(GameModeCatalog.GetGameModePrefabComponent(pre.gameModeIndex));
                }
                if (!ours) return true;
                __result = Assets.RoR2.Scripts.Platform.BaseActivity.ActivityGameMode.Any;
                return false;
            }
        }

        /// <summary>
        /// Esc is the game's pause key. While one of the mode's own things is open (a targeting mode, the help sheet,
        /// the item or recruiting panel) the key closes that instead and the pause menu stays shut.
        /// </summary>
        [HarmonyPatch(typeof(PauseManager), nameof(PauseManager.TogglePauseScreen))]
        internal static class EscapeGuard
        {
            private static bool Prefix()
            {
                if (!HighCommandRun.IsActive || PauseManager.isPaused || PauseManager.PauseScreenIsOpen()) return true;
                Client.ClientRoot root = Client.ClientRoot.Instance;
                if (!root) return true;
                return !root.ConsumeEscape();
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
