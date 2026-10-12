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

        [ConCommand(commandName = "hc_start", flags = HostOnly, helpText = "Starts a High Command run (single player, host).")]
        private static void CcStart(ConCommandArgs args)
        {
            RoR2.Console.instance.SubmitCmd(null, "transition_command \"gamemode " + Mode.HighCommandRun.PrefabName + "; host 0;\"");
        }

        [ConCommand(commandName = "hc_dump_run", flags = HostOnly, helpText = "Dumps the High Command run prefab (components, key fields) and the live run if any.")]
        private static void CcDumpRun(ConCommandArgs args)
        {
            var sb = new StringBuilder("HighCommand run prefab:\n");
            GameObject prefab = Mode.RunPrefabBuilder.Prefab;
            if (!prefab) sb.AppendLine("  (not built)");
            else DescribeRun(sb, prefab, "  ");

            if (Run.instance)
            {
                sb.AppendLine("Live run:");
                DescribeRun(sb, Run.instance.gameObject, "  ");
            }
            Debug.Log(sb.ToString());
        }

        private static void DescribeRun(StringBuilder sb, GameObject go, string indent)
        {
            sb.AppendLine($"{indent}object '{go.name}' activeSelf={go.activeSelf} activeInHierarchy={go.activeInHierarchy}");
            foreach (Component c in go.GetComponents<Component>())
                sb.AppendLine($"{indent}  component {(c ? c.GetType().FullName : "<missing>")}");

            var run = go.GetComponent<Run>();
            if (run)
            {
                sb.AppendLine($"{indent}Run: type={run.GetType().Name} nameToken={run.nameToken} userPickable={run.userPickable} " +
                              $"gameModeIndex={run.gameModeIndex} startingSceneGroup={(run.startingSceneGroup ? run.startingSceneGroup.name : "null")} " +
                              $"gameOverPrefab={(run.gameOverPrefab ? run.gameOverPrefab.name : "null")} uiPrefab={(run.uiPrefab ? run.uiPrefab.name : "null")}");
            }
            var id = go.GetComponent<UnityEngine.Networking.NetworkIdentity>();
            if (id) sb.AppendLine($"{indent}NetworkIdentity: assetId={id.assetId} netId={id.netId}");
        }

        [ConCommand(commandName = "hc_dump_ui", flags = HostOnly, helpText = "hc_dump_ui <name part> [depth]: prints the hierarchy of objects whose name contains the text.")]
        private static void CcDumpUi(ConCommandArgs args)
        {
            if (args.Count < 1) throw new ConCommandException("Usage: hc_dump_ui <name part> [depth]");
            string part = args.GetArgString(0);
            int depth = args.Count > 1 ? args.GetArgInt(1) : 4;

            var sb = new StringBuilder();
            int found = 0;
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (!go.scene.IsValid() || go.name.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                // only the top-most match, children are printed by the walk
                Transform p = go.transform.parent;
                bool parentMatches = false;
                while (p) { if (p.name.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0) { parentMatches = true; break; } p = p.parent; }
                if (parentMatches) continue;

                found++;
                sb.AppendLine($"== scene '{go.scene.name}' path '{PathOf(go.transform)}'");
                Walk(sb, go.transform, 0, depth);
                if (found >= 6) break;
            }
            if (found == 0) sb.AppendLine("(nothing found)");
            Debug.Log(sb.ToString());
        }

        private static string PathOf(Transform t)
        {
            string path = t.name;
            while (t.parent) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }

        private static void Walk(StringBuilder sb, Transform t, int level, int maxDepth)
        {
            var comps = new StringBuilder();
            foreach (Component c in t.GetComponents<Component>())
            {
                if (!c || c is Transform) continue;
                string n = c.GetType().Name;
                if (comps.Length > 0) comps.Append(',');
                comps.Append(n);
            }
            sb.AppendLine($"{new string(' ', level * 2)}{t.name} [{(t.gameObject.activeSelf ? "on" : "off")}] {comps}");
            if (level >= maxDepth) return;
            for (int i = 0; i < t.childCount; i++) Walk(sb, t.GetChild(i), level + 1, maxDepth);
        }

        [ConCommand(commandName = "hc_menu_extra", flags = HostOnly, helpText = "Presses the title menu's Extra Game Mode button (and, with an argument 'click', the High Command button).")]
        private static void CcMenuExtra(ConCommandArgs args)
        {
            bool click = args.Count > 0 && args.GetArgString(0) == "click";
            if (!click)
            {
                RoR2.UI.MainMenu.TitleMenuController title = null;
                foreach (var t in Resources.FindObjectsOfTypeAll<RoR2.UI.MainMenu.TitleMenuController>())
                    if (t.gameObject.scene.IsValid()) { title = t; break; }
                if (!title) throw new ConCommandException("Title menu not found (not in the title scene?).");
                title.OnExtraGameModeButtonPressed();
            }
            else
            {
                foreach (var button in Resources.FindObjectsOfTypeAll<RoR2.UI.HGButton>())
                {
                    if (button.name.Contains("HighCommand"))
                    {
                        Debug.Log("HighCommand: clicking " + button.name);
                        button.onClick.Invoke();
                        return;
                    }
                }
                throw new ConCommandException("High Command button not found.");
            }
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
