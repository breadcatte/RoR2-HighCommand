using System;
using System.Collections;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using RoR2;
using RoR2.ContentManagement;
using UnityEngine;

namespace HighCommand
{
    /// <summary>
    /// High Command: a strategy game mode for Risk of Rain 2. Entry point; wires the modules together.
    /// Every module is isolated behind try/catch so one failure cannot take the whole game down.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency("com.rune580.riskofoptions", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "kantemiir.HighCommand";
        public const string Name = "HighCommand";
        public const string Version = "0.9.1";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static string PluginDirectory;
        internal static ContentPack Pack;

        private Harmony harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            PluginDirectory = System.IO.Path.GetDirectoryName(Info.Location);

            Pack = new ContentPack();
            typeof(ContentPack).GetField("_identifier", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(Pack, Guid);

            ContentManager.collectContentPackProviders += add => add(new Provider());
            harmony = new Harmony(Guid);

            Settings.Init(Config);
            Strings.Register();
            Pack.entityStateTypes.Add(new[] { typeof(Orders.HeroAIState) });
            TryPatch(typeof(LanguagePatches), "language tokens");
            TryPatch(typeof(Mode.LobbyPatches.MultiplayerMenu), "game mode in the multiplayer menu");
            TryPatch(typeof(Mode.LobbyPatches.LocalMultiplayerMenu), "game mode in the local multiplayer menu");
            TryPatch(typeof(Mode.RunRulePatch), "forced rule choices");
            TryPatch(typeof(Mode.MenuButton), "Extra Game Mode menu button");
            TryPatch(typeof(Mode.RunPatches.NoCommanderBody), "commander has no body");
            TryPatch(typeof(Mode.RunPatches.LivingPlayerCount), "living player count = living heroes");
            TryPatch(typeof(Mode.LobbyRules.InitPatch), "lobby rules category");
            TryPatch(typeof(Mode.RunPatches.DirectorSpawnTarget), "director spawns around heroes");
            TryPatch(typeof(Mode.RunPatches.ActivityGameMode), "no platform-activity error for the game mode");
            Client.ClientRoot.EscapeGuardActive = TryPatch(typeof(Mode.RunPatches.EscapeGuard), "Esc closes the mode's panels before it pauses");
            TryPatch(typeof(Mode.RunPatches.ZoneCountInRadius), "holdout zones count heroes inside");
            TryPatch(typeof(Mode.RunPatches.ZoneCountLiving), "holdout zones count living heroes");
            TryPatch(typeof(Mode.RunPatches.ParticipatingPlayers), "army size scales the run like extra players");
            TryPatch(typeof(Army.EconomyPatches.KillGold), "kill gold goes to the army purse");
            TryPatch(typeof(Army.EconomyPatches.RememberVictim), "gold popup position");
            TryPatch(typeof(Army.EconomyPatches.DirectGold), "direct gold goes to the army purse");
            TryPatch(typeof(Army.LootPatches.Droplets), "item droplets go to the pool");
            TryPatch(typeof(Army.LootPatches.WorldPickups), "world pickups go to the pool");
            TryPatch(typeof(Army.LootPatches.KillDrops), "enemies drop items");
            TryPatch(typeof(Army.LootPatches.NoPaidInteractables), "no chests and other paid interactables");
            TryPatch(typeof(Army.GameOverStatsPatch), "army statistics on the end-of-run screen");

            try
            {
                if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(OptionsIntegration.RiskOfOptionsGuid)) OptionsIntegration.Register();
            }
            catch (Exception e) { Log.LogWarning("Mod Options not registered: " + e.Message); }

            try { Dev.AutoExec.Install(); }
            catch (Exception e) { Log.LogWarning("AutoExec not installed: " + e.Message); }

            Log.LogInfo($"{Name} {Version} loaded (game build {RoR2Application.GetBuildId()})");
        }

        private void OnDestroy()
        {
            try { harmony?.UnpatchSelf(); } catch (Exception) { }
        }

        /// <summary>Applies one Harmony patch class; a failing patch is logged and skipped, never fatal.</summary>
        internal static bool TryPatch(Type patchClass, string what)
        {
            try
            {
                Instance.harmony.CreateClassProcessor(patchClass).Patch();
                Log.LogInfo($"Patch applied: {what}");
                return true;
            }
            catch (Exception e)
            {
                Log.LogError($"Patch FAILED ({what}): {e}");
                return false;
            }
        }

        private class Provider : IContentPackProvider
        {
            public string identifier => Guid;

            public IEnumerator LoadStaticContentAsync(LoadStaticContentAsyncArgs args)
            {
                yield return Mode.RunPrefabBuilder.Build(Pack);
                args.ReportProgress(1f);
            }

            public IEnumerator GenerateContentPackAsync(GetContentPackAsyncArgs args)
            {
                ContentPack.Copy(Pack, args.output);
                args.ReportProgress(1f);
                yield break;
            }

            public IEnumerator FinalizeAsync(FinalizeAsyncArgs args)
            {
                args.ReportProgress(1f);
                yield break;
            }
        }
    }
}
