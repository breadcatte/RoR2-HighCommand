using HarmonyLib;
using RoR2;
using UnityEngine;

namespace HighCommand.Mode
{
    /// <summary>
    /// Identity of the High Command game mode. The Run prefab is a clone of ClassicRun (the stock Run component
    /// cannot be swapped: Run and RunArtifactManager require each other, so Unity refuses to remove either),
    /// marked by this component. Everything the mode adds lives here or in sibling components; the few places that
    /// need to differ from a classic run are Harmony patches that check <see cref="IsActive"/>.
    /// </summary>
    public class HighCommandRun : MonoBehaviour
    {
        public const string PrefabName = "HighCommandRun";
        public const string NameToken = "HIGHCOMMAND_GAMEMODE_NAME";

        /// <summary>The live run's marker, or null when no High Command run exists.</summary>
        public static HighCommandRun Instance { get; private set; }

        public static bool IsActive => Instance != null;

        /// <summary>True for a Run prefab/instance of this mode (works on the prefab in the lobby too).</summary>
        public static bool IsModeRun(Run run) => run && run.GetComponent<HighCommandRun>();

        private void OnEnable()
        {
            Instance = this;
            Plugin.Log.LogInfo("High Command run started.");
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
        }
    }

    [HarmonyPatch(typeof(Run), nameof(Run.OverrideRuleChoices))]
    internal static class RunRulePatch
    {
        private static void Postfix(Run __instance, RuleChoiceMask mustInclude, RuleChoiceMask mustExclude)
        {
            if (!HighCommandRun.IsModeRun(__instance)) return;

            // The commander has no body: nobody can "drop in" later, and the army stays the army.
            ForceOff(mustInclude, mustExclude, "Misc.AllowDropIn.Off");
        }

        private static void ForceOff(RuleChoiceMask mustInclude, RuleChoiceMask mustExclude, string choiceName)
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
}
