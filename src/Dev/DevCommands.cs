using System.Linq;
using System.Text;
using RoR2;
using UnityEngine;

namespace HighCommand.Dev
{
    /// <summary>
    /// Console commands (Ctrl+Alt+~ in game). Every command is prefixed hc_ and works only for the host.
    /// </summary>
    internal static class DevCommands
    {
        // Local commands: they must also work in the main menu where no server exists
        // (ExecuteOnServer would forward them to a server that is not there and silently drop them).
        private const ConVarFlags HostOnly = ConVarFlags.None;

        [ConCommand(commandName = "hc_ping", flags = HostOnly, helpText = "Prints the High Command version and a short state summary.")]
        private static void CcPing(ConCommandArgs args)
        {
            var sb = new StringBuilder();
            sb.Append($"HighCommand {Plugin.Version} | game build {RoR2Application.GetBuildId()}");
            sb.Append($" | run={(Run.instance ? Run.instance.name : "none")}");
            sb.Append($" | scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
            Debug.Log(sb.ToString());
        }

        [ConCommand(commandName = "hc_dump_survivors", flags = HostOnly, helpText = "Lists every survivor with the AI master the game has for it.")]
        private static void CcDumpSurvivors(ConCommandArgs args)
        {
            var sb = new StringBuilder("HighCommand survivors:\n");
            foreach (SurvivorDef def in SurvivorCatalog.allSurvivorDefs)
            {
                if (def == null || !def.bodyPrefab) continue;
                var body = def.bodyPrefab.GetComponent<CharacterBody>();
                string masterName = "-";
                if (body)
                {
                    var idx = MasterCatalog.FindAiMasterIndexForBody(body.bodyIndex);
                    if (idx.isValid) masterName = MasterCatalog.GetMasterName(idx);
                }
                sb.AppendLine($"  {def.cachedName,-16} body={def.bodyPrefab.name,-24} hidden={def.hidden,-5} aiMaster={masterName}");
            }
            Debug.Log(sb.ToString());
        }

        [ConCommand(commandName = "hc_dump_gamemodes", flags = HostOnly, helpText = "Lists the registered game modes.")]
        private static void CcDumpGameModes(ConCommandArgs args)
        {
            var sb = new StringBuilder("HighCommand game modes:\n");
            for (int i = 0; i < GameModeCatalog.gameModeCount; i++)
            {
                Run run = GameModeCatalog.GetGameModePrefabComponent((GameModeIndex)i);
                if (!run) continue;
                sb.AppendLine($"  [{i}] {run.name,-24} nameToken={run.nameToken,-32} userPickable={run.userPickable} type={run.GetType().Name}");
            }
            Debug.Log(sb.ToString());
        }
    }
}
