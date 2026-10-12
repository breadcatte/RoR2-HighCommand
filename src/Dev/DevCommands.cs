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

        [ConCommand(commandName = "hc_status", flags = HostOnly, helpText = "Prints the army: every hero, whether it is alive, where it is and what its AI is doing.")]
        private static void CcStatus(ConCommandArgs args)
        {
            var run = Mode.HighCommandRun.Instance;
            if (run == null) { Debug.Log("HighCommand: no High Command run."); return; }

            var sb = new StringBuilder();
            Stage stage = Stage.instance;
            sb.AppendLine($"HighCommand status: stage={(stage && stage.sceneDef ? stage.sceneDef.cachedName : "none")} " +
                          $"cleared={Run.instance.stageClearCount} time={Run.instance.GetRunStopwatch():0}s " +
                          $"difficulty={Run.instance.difficultyCoefficient:0.00} heroes={run.Army.Count} alive={run.Army.AliveCount}");
            foreach (var hero in run.Army.Heroes)
            {
                CharacterBody body = hero.Body;
                string ai = "-";
                var baseAi = hero.Master ? hero.Master.GetComponent<RoR2.CharacterAI.BaseAI>() : null;
                if (baseAi)
                {
                    ai = $"driver={(string.IsNullOrEmpty(baseAi.selectedSkilldriverName) ? "none" : baseAi.selectedSkilldriverName)} " +
                         $"enemy={(baseAi.currentEnemy != null && baseAi.currentEnemy.gameObject ? baseAi.currentEnemy.gameObject.name : "none")} " +
                         $"state={(baseAi.stateMachine && baseAi.stateMachine.state != null ? baseAi.stateMachine.state.GetType().Name : "none")}";
                }
                var brain = hero.Master ? hero.Master.GetComponent<Orders.HeroBrain>() : null;
                if (brain != null)
                    ai += $" | brain: mode={brain.Mode} {brain.DebugState} order={(brain.Current != null ? brain.Current.ToString() : "-")} queued={brain.QueuedCount} stance={brain.Stance} stuck={brain.Stuck} anchor={brain.Anchor:0} goal={brain.Goal:0}";
                string hp = body && body.healthComponent ? $"{body.healthComponent.health:0}/{body.healthComponent.fullHealth:0}" : "-";
                sb.AppendLine($"  {hero} '{hero.DisplayName}' alive={hero.IsAlive} hp={hp} lvl={(body ? body.level : 0):0} pos={hero.Position:0} {ai}");
            }
            Debug.Log(sb.ToString());
        }

        [ConCommand(commandName = "hc_screenshot", flags = HostOnly, helpText = "hc_screenshot <name>: saves a screenshot (with the mod's UI) to the plugin's folder.")]
        private static void CcScreenshot(ConCommandArgs args)
        {
            string name = args.Count > 0 ? args.GetArgString(0) : "shot";
            string dir = System.IO.Path.Combine(Plugin.PluginDirectory, "screenshots");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("HighCommand: screenshot -> " + path);
        }

        [ConCommand(commandName = "hc_cam", flags = HostOnly, helpText = "hc_cam army | dist <m> | yaw <deg> | at <x> <z>: moves the RTS camera (testing).")]
        private static void CcCam(ConCommandArgs args)
        {
            var cam = Client.RtsCamera.Instance;
            if (!cam) throw new ConCommandException("No RTS camera (not in a High Command run).");
            string what = args.Count > 0 ? args.GetArgString(0) : "army";
            switch (what)
            {
                case "army":
                    Vector3? c = Client.ClientRoot.ArmyCenter();
                    if (c.HasValue) cam.FocusOn(c.Value);
                    break;
                case "dist":
                    cam.SetDistance(args.GetArgFloat(1));
                    break;
                case "follow":
                    cam.FollowTarget = Client.ClientRoot.ArmyCenter;
                    break;
                case "at":
                    cam.FocusOn(new Vector3(args.GetArgFloat(1), cam.Focus.y, args.GetArgFloat(2)));
                    break;
                default:
                    throw new ConCommandException("Unknown option.");
            }
        }

        [ConCommand(commandName = "hc_sel", flags = HostOnly, helpText = "Prints the current selection and control groups.")]
        private static void CcSel(ConCommandArgs args)
        {
            var root = Client.ClientRoot.Instance;
            if (!root) throw new ConCommandException("Not in a High Command run.");
            var sb = new StringBuilder($"HighCommand selection: {root.Selection.Count} [");
            foreach (var h in root.Selection.Heroes) sb.Append(h.Id).Append(' ');
            sb.Append("] groups:");
            for (int g = 0; g < Client.Selection.GroupCount; g++)
                if (root.Selection.Group(g).Count > 0) sb.Append($" {g}={root.Selection.Group(g).Count}");
            Debug.Log(sb.ToString());
        }

        [ConCommand(commandName = "hc_select", flags = HostOnly, helpText = "hc_select all | none | <heroId>: changes the selection (testing).")]
        private static void CcSelect(ConCommandArgs args)
        {
            var root = Client.ClientRoot.Instance;
            var run = Mode.HighCommandRun.Instance;
            if (!root || run == null) throw new ConCommandException("Not in a High Command run.");

            string what = args.Count > 0 ? args.GetArgString(0) : "all";
            if (what == "none") root.Selection.Clear();
            else if (what == "all") root.Selection.Set(run.Army.Heroes);
            else
            {
                int id = args.GetArgInt(0);
                foreach (var h in run.Army.Heroes) if (h.Id == id) { root.Selection.SetSingle(h); break; }
            }
            Debug.Log($"HighCommand: selected {root.Selection.Count} hero(es).");
        }

        // ---- synthetic input (testing) --------------------------------------------------------------

        [ConCommand(commandName = "hc_click", flags = HostOnly, helpText = "hc_click <x> <y> [button 0/1/2] [shift] [ctrl]: injects a mouse click (screen pixels, origin bottom-left).")]
        private static void CcClick(ConCommandArgs args)
        {
            float x = args.GetArgFloat(0), y = args.GetArgFloat(1);
            int button = args.Count > 2 ? args.GetArgInt(2) : 0;
            bool shift = HasFlag(args, 3, "shift"), ctrl = HasFlag(args, 3, "ctrl");
            Client.ClientInput.InjectClick(new Vector2(x, y), button, shift, ctrl);
        }

        [ConCommand(commandName = "hc_drag", flags = HostOnly, helpText = "hc_drag <x1> <y1> <x2> <y2> [shift]: injects a left-button drag.")]
        private static void CcDrag(ConCommandArgs args)
        {
            Client.ClientInput.InjectDrag(new Vector2(args.GetArgFloat(0), args.GetArgFloat(1)),
                new Vector2(args.GetArgFloat(2), args.GetArgFloat(3)), 0, HasFlag(args, 4, "shift"));
        }

        [ConCommand(commandName = "hc_key", flags = HostOnly, helpText = "hc_key <KeyCode> [ctrl] [shift] [alt]: injects a key press.")]
        private static void CcKey(ConCommandArgs args)
        {
            var key = (KeyCode)System.Enum.Parse(typeof(KeyCode), args.GetArgString(0), true);
            Client.ClientInput.InjectKey(key, HasFlag(args, 1, "ctrl"), HasFlag(args, 1, "shift"), HasFlag(args, 1, "alt"));
        }

        [ConCommand(commandName = "hc_click_hero", flags = HostOnly, helpText = "hc_click_hero <heroId> [button] [shift]: clicks on the hero's body on screen.")]
        private static void CcClickHero(ConCommandArgs args)
        {
            var run = Mode.HighCommandRun.Instance;
            if (run == null) throw new ConCommandException("Not in a High Command run.");
            int id = args.GetArgInt(0);
            int button = args.Count > 1 ? args.GetArgInt(1) : 0;
            foreach (var h in run.Army.Heroes)
            {
                if (h.Id != id) continue;
                if (!h.Body) throw new ConCommandException("Hero has no body.");
                if (!Client.WorldPicker.Project(h.Body.corePosition, 1f, out Vector2 s, out float _))
                    throw new ConCommandException("Hero is behind the camera.");
                Client.ClientInput.InjectClick(s, button, HasFlag(args, 2, "shift"));
                return;
            }
            throw new ConCommandException("No such hero.");
        }

        [ConCommand(commandName = "hc_click_enemy", flags = HostOnly, helpText = "hc_click_enemy [button] [shift]: clicks on the enemy nearest to the camera focus.")]
        private static void CcClickEnemy(ConCommandArgs args)
        {
            var cam = Client.RtsCamera.Instance;
            if (!cam) throw new ConCommandException("No camera.");
            int button = args.Count > 0 ? args.GetArgInt(0) : 1;
            CharacterBody best = null;
            float bestD = float.MaxValue;
            foreach (TeamIndex team in new[] { TeamIndex.Monster, TeamIndex.Lunar, TeamIndex.Void })
            {
                foreach (TeamComponent tc in TeamComponent.GetTeamMembers(team))
                {
                    if (!tc || !tc.body || !tc.body.healthComponent || !tc.body.healthComponent.alive) continue;
                    if (!Client.WorldPicker.Project(tc.body.corePosition, 1f, out Vector2 s, out float _)) continue;
                    if (!Client.WorldPicker.IsOnScreen(s, -60f)) continue;
                    float d = (tc.body.corePosition - cam.Focus).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = tc.body; }
                }
            }
            if (!best) throw new ConCommandException("No enemy on screen.");
            Client.WorldPicker.Project(best.corePosition, 1f, out Vector2 at, out float _);
            Debug.Log($"HighCommand: clicking enemy {best.name} at ({at.x:0},{at.y:0})");
            Client.ClientInput.InjectClick(at, button, HasFlag(args, 1, "shift"));
        }

        [ConCommand(commandName = "hc_click_teleporter", flags = HostOnly, helpText = "hc_click_teleporter [button]: clicks on the teleporter if it is on screen.")]
        private static void CcClickTeleporter(ConCommandArgs args)
        {
            var tp = TeleporterInteraction.instance;
            if (!tp) throw new ConCommandException("No teleporter.");
            int button = args.Count > 0 ? args.GetArgInt(0) : 1;
            if (!Client.WorldPicker.Project(tp.transform.position + Vector3.up, 1f, out Vector2 at, out float _))
                throw new ConCommandException("Teleporter is behind the camera.");
            Debug.Log($"HighCommand: clicking teleporter at ({at.x:0},{at.y:0}); picked={(Client.WorldPicker.PickInteractable(at) ? Client.WorldPicker.PickInteractable(at).name : "nothing")}");
            Client.ClientInput.InjectClick(at, button);
        }

        [ConCommand(commandName = "hc_input_end", flags = HostOnly, helpText = "Ends synthetic input; the real mouse works again.")]
        private static void CcInputEnd(ConCommandArgs args) => Client.ClientInput.EndSynthetic();

        private static bool HasFlag(ConCommandArgs args, int fromIndex, string flag)
        {
            for (int i = fromIndex; i < args.Count; i++)
                if (string.Equals(args.GetArgString(i), flag, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        [ConCommand(commandName = "hc_overlay", flags = HostOnly, helpText = "Prints diagnostics of the world overlay (rings, bars).")]
        private static void CcOverlay(ConCommandArgs args)
        {
            var cam = Client.RtsCamera.Instance;
            var sb = new StringBuilder();
            sb.AppendLine($"HighCommand overlay: bars={UI.WorldOverlay.LastBars} rings={UI.WorldOverlay.LastRings} " +
                          $"camera={(cam ? "yes" : "no")} sceneCam={(cam && cam.SceneCam ? cam.SceneCam.name : "none")} " +
                          $"focus={(cam ? cam.Focus : Vector3.zero):0.0} dist={(cam ? cam.Distance : 0f):0.0}");
            var run = Mode.HighCommandRun.Instance;
            if (run != null && cam && cam.SceneCam)
            {
                foreach (var h in run.Army.Heroes)
                {
                    var b = h.Body;
                    if (!b) { sb.AppendLine($"  {h}: no body"); continue; }
                    Vector3 sp = cam.SceneCam.WorldToScreenPoint(b.corePosition);
                    sb.AppendLine($"  {h}: world={b.corePosition:0.0} screen=({sp.x:0},{sp.y:0}) depth={sp.z:0.0} screenSize={Screen.width}x{Screen.height}");
                }
            }
            Debug.Log(sb.ToString());
        }

        [ConCommand(commandName = "hc_dump_fonts", flags = HostOnly, helpText = "Lists TextMeshPro font assets the game has loaded.")]
        private static void CcDumpFonts(ConCommandArgs args)
        {
            var sb = new StringBuilder("TMP fonts:\n");
            foreach (var f in Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>())
                sb.AppendLine($"  {f.name}  glyphs={f.characterTable?.Count}  fallbacks={f.fallbackFontAssetTable?.Count}");
            Debug.Log(sb.ToString());
        }

        [ConCommand(commandName = "hc_next_stage", flags = HostOnly, helpText = "Host: advances the run to the next stage immediately.")]
        private static void CcNextStage(ConCommandArgs args)
        {
            if (!UnityEngine.Networking.NetworkServer.active || !Run.instance)
                throw new ConCommandException("Only the host can do this, and a run must be in progress.");
            if (!Run.instance.nextStageScene)
                Run.instance.PickNextStageSceneFromCurrentSceneDestinations();
            SceneDef next = Run.instance.nextStageScene;
            if (!next) throw new ConCommandException("No next stage is known.");
            Debug.Log("HighCommand: advancing to " + next.cachedName);
            Run.instance.AdvanceStage(next);
        }

        [ConCommand(commandName = "hc_kill_enemies", flags = HostOnly, helpText = "Host: kills every enemy on the map (monster and lunar teams).")]
        private static void CcKillEnemies(ConCommandArgs args)
        {
            if (!UnityEngine.Networking.NetworkServer.active) throw new ConCommandException("Only the host can do this.");
            int killed = 0;
            foreach (TeamIndex team in new[] { TeamIndex.Monster, TeamIndex.Lunar, TeamIndex.Void })
            {
                var members = new System.Collections.Generic.List<TeamComponent>(TeamComponent.GetTeamMembers(team));
                foreach (TeamComponent tc in members)
                {
                    if (!tc || !tc.body || !tc.body.healthComponent) continue;
                    tc.body.healthComponent.Suicide();
                    killed++;
                }
            }
            Debug.Log($"HighCommand: killed {killed} enemies.");
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

        [ConCommand(commandName = "hc_dump_ai", flags = HostOnly, helpText = "hc_dump_ai <MasterName|all>: prints the AI skill drivers of a master prefab (in evaluation order).")]
        private static void CcDumpAi(ConCommandArgs args)
        {
            string want = args.Count > 0 ? args.GetArgString(0) : "CommandoMonsterMaster";
            var sb = new StringBuilder();
            foreach (SurvivorDef def in SurvivorCatalog.allSurvivorDefs)
            {
                if (!def || !def.bodyPrefab) continue;
                var body = def.bodyPrefab.GetComponent<CharacterBody>();
                if (!body) continue;
                var idx = MasterCatalog.FindAiMasterIndexForBody(body.bodyIndex);
                if (!idx.isValid) continue;
                string masterName = MasterCatalog.GetMasterName(idx);
                if (want != "all" && !string.Equals(masterName, want, System.StringComparison.OrdinalIgnoreCase)) continue;

                GameObject prefab = MasterCatalog.GetMasterPrefab(idx);
                var ai = prefab.GetComponent<RoR2.CharacterAI.BaseAI>();
                var drivers = prefab.GetComponents<RoR2.CharacterAI.AISkillDriver>();
                sb.AppendLine($"== {masterName}  scanState={(ai ? ai.scanState.stateType?.Name : "-")} fullVision={ai?.fullVision} drivers={drivers.Length}");
                foreach (var d in drivers)
                {
                    sb.AppendLine($"  [{d.customName}] slot={d.skillSlot} ready={d.requireSkillReady} range={d.minDistance:0}-{d.maxDistance:0} " +
                                  $"los={d.selectionRequiresTargetLoS}/{d.activationRequiresTargetLoS} target={d.moveTargetType} aim={d.aimType} " +
                                  $"move={d.movementType} press={d.buttonPressType} sprint={d.shouldSprint} jump={d.shouldJump} equip={d.shouldFireEquipment} " +
                                  $"userHp={d.minUserHealthFraction:0.##}-{d.maxUserHealthFraction:0.##} targetHp={d.minTargetHealthFraction:0.##}-{d.maxTargetHealthFraction:0.##} " +
                                  $"skill={(d.requiredSkill ? d.requiredSkill.skillName : "-")} enabled={d.enabled}");
                }
            }
            if (sb.Length == 0) sb.Append("no such master");
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
