using System;
using System.Reflection;
using HarmonyLib;
using RoR2;
using RoR2.UI;
using RoR2.UI.MainMenu;
using UnityEngine;
using UnityEngine.UI;

namespace HighCommand.Mode
{
    /// <summary>
    /// Adds a "High Command" button to the main menu's Extra Game Mode screen (next to Eclipse and Simulacrum).
    /// The button is a clone of the Eclipse button with a new label and click action; it is created lazily, just
    /// before the screen opens, so the title scene does not have to be touched at load time.
    /// </summary>
    // The title menu's "Extra Game Mode" button does not call TitleMenuController.OnExtraGameModeButtonPressed: its
    // click runs MainMenuController.SetDesiredMenuScreen(extraGameModeMenuScreen) directly (seen with hc_menu_buttons).
    // So the screen switch itself is what is watched.
    [HarmonyPatch(typeof(MainMenuController), nameof(MainMenuController.SetDesiredMenuScreen))]
    internal static class MenuButton
    {
        private const string ButtonName = "GenericMenuButton (HighCommand)";

        private static void Prefix(MainMenuController __instance, BaseMainMenuScreen newDesiredMenuScreen)
        {
            try
            {
                if (!__instance || !newDesiredMenuScreen || newDesiredMenuScreen != __instance.extraGameModeMenuScreen) return;
                Inject(newDesiredMenuScreen.transform);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("MenuButton: could not add the High Command button: " + e.Message);
            }
        }

        private static void Inject(Transform screen)
        {
            Transform panel = screen.Find("Main Panel/GenericMenuButtonPanel/JuicePanel");
            if (!panel) { Plugin.Log.LogWarning("MenuButton: button panel not found."); return; }
            if (panel.Find(ButtonName)) return;

            Transform template = null;
            for (int i = 0; i < panel.childCount; i++)
            {
                Transform child = panel.GetChild(i);
                if (child.name.IndexOf("Eclipse", StringComparison.OrdinalIgnoreCase) >= 0 && child.GetComponent<HGButton>())
                {
                    template = child;
                    break;
                }
            }
            if (!template) { Plugin.Log.LogWarning("MenuButton: Eclipse button template not found."); return; }

            GameObject clone = UnityEngine.Object.Instantiate(template.gameObject, panel);
            clone.name = ButtonName;
            clone.transform.SetSiblingIndex(template.GetSiblingIndex() + 1);

            // Replace the click event wholesale: persistent (serialized) listeners cannot be removed at runtime.
            HGButton button = clone.GetComponent<HGButton>();
            FieldInfo onClick = typeof(Button).GetField("m_OnClick", BindingFlags.Instance | BindingFlags.NonPublic);
            if (onClick != null)
            {
                var fresh = new Button.ButtonClickedEvent();
                fresh.AddListener(StartRun);
                onClick.SetValue(button, fresh);
            }
            else
            {
                Plugin.Log.LogWarning("MenuButton: Button.m_OnClick not found; the button will do nothing.");
            }

            // Other behaviours that fire console commands on click belong to the cloned Eclipse button.
            foreach (ConsoleFunctions cf in clone.GetComponents<ConsoleFunctions>()) UnityEngine.Object.Destroy(cf);

            LanguageTextMeshController label = clone.GetComponent<LanguageTextMeshController>();
            if (label) label.token = HighCommandRun.NameToken;

            Plugin.Log.LogInfo("MenuButton: High Command button added to the Extra Game Mode menu.");
        }

        private static void StartRun()
        {
            RoR2.Console.instance.SubmitCmd(null, "transition_command \"gamemode " + HighCommandRun.PrefabName + "; host 0;\"");
        }
    }
}
