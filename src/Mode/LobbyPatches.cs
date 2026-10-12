using System.Linq;
using HarmonyLib;
using RoR2.UI;

namespace HighCommand.Mode
{
    /// <summary>
    /// Makes the mode selectable in the game-mode carousels. HostGamePanelController lists every userPickable mode on its
    /// own, but the multiplayer / local-multiplayer menus only keep modes whose token is already in the carousel of the
    /// menu prefab, so our token is added there just before the list is built.
    /// </summary>
    internal static class LobbyPatches
    {
        internal static void AddChoice(CarouselController picker)
        {
            if (!picker) return;
            CarouselController.Choice[] choices = picker.choices ?? new CarouselController.Choice[0];
            if (choices.Any(c => c.suboptionDisplayToken == HighCommandRun.NameToken)) return;

            var list = choices.ToList();
            list.Add(new CarouselController.Choice
            {
                suboptionDisplayToken = HighCommandRun.NameToken,
                convarValue = HighCommandRun.PrefabName
            });
            picker.choices = list.ToArray();
        }

        [HarmonyPatch(typeof(RoR2.UI.MainMenu.MultiplayerMenuController), "BuildGameModeChoices")]
        internal static class MultiplayerMenu
        {
            private static void Prefix(RoR2.UI.MainMenu.MultiplayerMenuController __instance) => AddChoice(__instance.gameModePicker);
        }

        [HarmonyPatch(typeof(RoR2.UI.MainMenu.LocalMultiplayerMenuController), "BuildGameModeChoices")]
        internal static class LocalMultiplayerMenu
        {
            private static void Prefix(RoR2.UI.MainMenu.LocalMultiplayerMenuController __instance) => AddChoice(__instance.gameModePicker);
        }
    }
}
