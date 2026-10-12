using System;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using RiskOfOptions;
using RiskOfOptions.OptionConfigs;
using RiskOfOptions.Options;

namespace HighCommand
{
    /// <summary>
    /// Puts the mod's settings into the game's Options menu (Mod Options) when Risk of Options is installed. The mod
    /// works without it: this class is only touched after the plugin list says the dependency is present.
    /// Key bindings are not offered here (they are plain key codes): edit them in the .cfg file.
    /// </summary>
    internal static class OptionsIntegration
    {
        internal const string RiskOfOptionsGuid = "com.rune580.riskofoptions";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Register()
        {
            try
            {
                ModSettingsManager.SetModDescription(
                    "High Command: a strategy mode. Settings of the camera, economy, loot and the army. Key bindings are in the .cfg file.",
                    Plugin.Guid, Plugin.Name);

                // camera
                Slider(Settings.CameraPanSpeed, 0.2f, 4f);
                Slider(Settings.CameraZoomSpeed, 0.2f, 4f);
                Slider(Settings.CameraRotateSpeed, 0.2f, 4f);
                Slider(Settings.CameraSmoothing, 0f, 0.6f);
                Slider(Settings.CameraPitch, 25f, 85f, "{0:0}");
                Slider(Settings.CameraFov, 20f, 100f, "{0:0}");
                Slider(Settings.CameraMinDistance, 4f, 60f, "{0:0}");
                Slider(Settings.CameraMaxDistance, 30f, 300f, "{0:0}");
                Check(Settings.EdgeScroll);
                IntSlider(Settings.EdgeScrollPixels, 1, 40);
                Check(Settings.ListenerAtFocus);

                // interface
                Slider(Settings.UiScale, 0.6f, 1.6f);

                // minimap
                Check(Settings.MinimapEnabled);
                Slider(Settings.MinimapSize, 140f, 520f, "{0:0}");

                // economy and army
                IntSlider(Settings.StartingGold, 0, 5000);
                Slider(Settings.GoldMultiplier, 0.1f, 10f);
                IntSlider(Settings.HeroBasePrice, 10, 3000);
                Slider(Settings.HeroPriceGrowth, 1f, 4f);
                Slider(Settings.PriceMultiplier, 0.1f, 10f);
                Slider(Settings.ReviveCostFraction, 0f, 3f);
                IntSlider(Settings.ArmyLimit, 1, 20);
                Check(Settings.AllowDuplicates);
                Check(Settings.IgnoreUnlocks);
                Check(Settings.AllowHiddenSurvivors);
                Check(Settings.AutoRetreat);

                // run
                Slider(Settings.ThreatPerExtraHero, 0f, 2f);
                Slider(Settings.DefeatGraceBroke, 0f, 120f, "{0:0}");
                Slider(Settings.DefeatGraceRevive, 0f, 600f, "{0:0}");

                // loot
                Check(Settings.ItemDropsEnabled);
                Slider(Settings.ItemGoldFactor, 0.2f, 30f);
                Slider(Settings.ItemDropMultiplier, 0f, 10f);
                Check(Settings.RemovePaidInteractables);
                Check(Settings.RebalanceOnRecruit);
                ModSettingsManager.AddOption(new ChoiceOption(Settings.ItemSharing));

                Check(Settings.VerboseLog);
                Plugin.Log.LogInfo("Settings are available in Mod Options (Risk of Options).");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Risk of Options integration failed: " + e.Message);
            }
        }

        private static void Check(ConfigEntry<bool> entry)
        {
            if (entry != null) ModSettingsManager.AddOption(new CheckBoxOption(entry));
        }

        private static void Slider(ConfigEntry<float> entry, float min, float max, string format = "{0:0.00}")
        {
            if (entry != null) ModSettingsManager.AddOption(new SliderOption(entry, new SliderConfig { min = min, max = max, FormatString = format }));
        }

        private static void IntSlider(ConfigEntry<int> entry, int min, int max)
        {
            if (entry != null) ModSettingsManager.AddOption(new IntSliderOption(entry, new IntSliderConfig { min = min, max = max }));
        }
    }
}
