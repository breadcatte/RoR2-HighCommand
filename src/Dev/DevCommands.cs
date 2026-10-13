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
            sb.Append($" | paused={PauseManager.isPaused}");
            Client.ClientRoot root = Client.ClientRoot.Instance;
            if (root) sb.Append(" | ui: ").Append(root.Describe());
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
                sb.AppendLine($"  {hero} '{hero.DisplayName}' alive={hero.IsAlive} hp={hp} lvl={(body ? body.level : 0):0} pos={hero.Position:0} " +
                              $"cd=[{hero.NetSkillCd[0]},{hero.NetSkillCd[1]},{hero.NetSkillCd[2]}] auto={hero.NetAutoMask:X2} {ai}");
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
                case "yaw":
                    cam.SetAngles(args.GetArgFloat(1), null);
                    break;
                case "pitch":
                    cam.SetAngles(null, args.GetArgFloat(1));
                    break;
                case "follow":
                    cam.FollowTarget = () => Client.ClientRoot.ArmyCenter();
                    break;
                case "at":
                    cam.FocusOn(new Vector3(args.GetArgFloat(1), cam.Focus.y, args.GetArgFloat(2)));
                    break;
                case "sweep":
                {
                    // hc_cam sweep <dx> <dz> <seconds>: pans from the current focus by (dx, dz) and logs the height
                    Vector3 from = cam.Focus;
                    cam.Sweep(from, from + new Vector3(args.GetArgFloat(1), 0f, args.GetArgFloat(2)), args.GetArgFloat(3));
                    break;
                }
                case "sweep_to":
                {
                    // hc_cam sweep_to <x> <z> <seconds>: the same, to a point of the map
                    Vector3 from = cam.Focus;
                    cam.Sweep(from, new Vector3(args.GetArgFloat(1), from.y, args.GetArgFloat(2)), args.GetArgFloat(3));
                    break;
                }
                case "audit":
                    Debug.Log("HighCommand camera audit: " + cam.AuditHeights());
                    break;
                case "info":
                {
                    Vector3? army = Client.ClientRoot.ArmyCenter();
                    Debug.Log($"HighCommand camera: focus={cam.Focus:0} distance={cam.Distance:0.0} yaw={cam.Yaw:0} pitch={cam.Pitch:0.0} " +
                              $"scene camera at {(cam.SceneCam ? cam.SceneCam.transform.position : Vector3.zero):0} army={(army.HasValue ? army.Value : Vector3.zero):0} " +
                              $"far={(cam.SceneCam ? cam.SceneCam.farClipPlane : 0f):0} max={Settings.CameraMaxDistance.Value:0} {cam.DebugWanted()} frame={Time.frameCount} t={Time.unscaledTime:0.0}");
                    break;
                }
                default:
                    throw new ConCommandException("Unknown option.");
            }
        }

        [ConCommand(commandName = "hc_gold", flags = HostOnly, helpText = "hc_gold <amount> | hc_gold set <amount>: gives the army gold / sets the purse (host).")]
        private static void CcGold(ConCommandArgs args)
        {
            if (!UnityEngine.Networking.NetworkServer.active || !Mode.HighCommandRun.IsActive)
                throw new ConCommandException("Only the host can do this during a High Command run.");
            if (args.Count > 1 && args.GetArgString(0) == "set")
            {
                Mode.HighCommandRun.Instance.State.ServerSetGold((uint)Mathf.Max(0, args.GetArgInt(1)));
                Debug.Log($"HighCommand: gold is now {Army.Economy.Gold}.");
                return;
            }
            uint amount = (uint)Mathf.Max(0, args.GetArgInt(0));
            Army.Economy.Add(amount, null, applyMultiplier: false);
            Debug.Log($"HighCommand: gold is now {Army.Economy.Gold}.");
        }

        [ConCommand(commandName = "hc_recruit", flags = HostOnly, helpText = "hc_recruit <SurvivorName>: recruits through the normal rules (gold, limit).")]
        private static void CcRecruit(ConCommandArgs args)
        {
            if (!UnityEngine.Networking.NetworkServer.active || !Mode.HighCommandRun.IsActive)
                throw new ConCommandException("Only the host can do this during a High Command run.");
            SurvivorDef def = SurvivorCatalog.FindSurvivorDef(args.GetArgString(0));
            if (!def) throw new ConCommandException("Unknown survivor: " + args.GetArgString(0));
            string error = Army.ArmyService.TryRecruit(def, Army.ArmyService.LoadoutFor(def));
            if (error == Net.CommandChannel.Relayed) Debug.Log($"HighCommand: recruit request for {def.cachedName} sent to the server.");
            else Debug.Log(error == null ? $"HighCommand: recruited {def.cachedName}." : "HighCommand: recruit failed: " + error);
        }

        [ConCommand(commandName = "hc_pool", flags = HostOnly, helpText = "Prints the army's item pool (commander inventory) and equipment pool.")]
        private static void CcPool(ConCommandArgs args)
        {
            var inv = Army.LootService.Pool;
            if (!inv) throw new ConCommandException("No item pool (not in a High Command run).");
            var sb = new StringBuilder("HighCommand pool:");
            int total = 0;
            foreach (ItemIndex idx in ItemCatalog.allItems)
            {
                int n = inv.GetItemCountPermanent(idx);
                if (n <= 0) continue;
                total += n;
                var def = ItemCatalog.GetItemDef(idx);
                sb.Append($" {def.name}x{n}");
            }
            var run = Mode.HighCommandRun.Instance;
            sb.Append($" | total items={total} equipment={(run != null ? run.Army.EquipmentPool.Count : 0)}");
            Debug.Log(sb.ToString());
        }

        [ConCommand(commandName = "hc_drop", flags = HostOnly, helpText = "hc_drop <ItemName>: adds an item to the pool as if it dropped.")]
        private static void CcDrop(ConCommandArgs args)
        {
            if (!UnityEngine.Networking.NetworkServer.active || !Mode.HighCommandRun.IsActive)
                throw new ConCommandException("Only the host can do this during a High Command run.");
            string name = args.GetArgString(0);
            PickupIndex pickup = PickupIndex.none;
            ItemIndex idx = ItemCatalog.FindItemIndex(name);
            if (idx != ItemIndex.None) pickup = PickupCatalog.FindPickupIndex(idx);
            else
            {
                EquipmentIndex eq = EquipmentCatalog.FindEquipmentIndex(name);
                if (eq != EquipmentIndex.None) pickup = PickupCatalog.FindPickupIndex(eq);
            }
            if (!pickup.isValid) throw new ConCommandException("Unknown item or equipment: " + name);
            bool ok = Army.LootService.Grant(new UniquePickup(pickup), null);
            Debug.Log("HighCommand: drop " + (ok ? "granted" : "failed"));
        }

        [ConCommand(commandName = "hc_inv_drag", flags = HostOnly,
            helpText = "hc_inv_drag item <fromHeroId> <ItemName> <toHeroId> [shift] | equip <fromHeroId> <slot> <toHeroId> | spare <index> <toHeroId> | hover item <heroId> <ItemName> | hover equip <heroId> <slot> | hover spare <index>: drives the open item panel with synthetic input.")]
        private static void CcInvDrag(ConCommandArgs args)
        {
            var root = Client.ClientRoot.Instance;
            var run = Mode.HighCommandRun.Instance;
            if (!root || run == null || root.Inventory == null) throw new ConCommandException("Not in a High Command run.");

            Army.Hero Find(int id)
            {
                foreach (var h in run.Army.Heroes) if (h.Id == id) return h;
                throw new ConCommandException("No hero with id " + id);
            }

            string mode = args.GetArgString(0).ToLowerInvariant();
            bool hover = mode == "hover";
            int a = hover ? 1 : 0; // argument offset
            string kind = args.GetArgString(a).ToLowerInvariant();
            Vector2 from, to = Vector2.zero;
            bool shift = HasFlag(args, 1, "shift");

            if (kind == "item")
            {
                Army.Hero owner = Find(args.GetArgInt(a + 1));
                ItemIndex idx = ItemCatalog.FindItemIndex(args.GetArgString(a + 2));
                if (!root.Inventory.TryFindCell(owner, idx, false, -1, out from)) throw new ConCommandException("Item cell not found (is the panel open and ticked?).");
                if (!hover && !root.Inventory.TryFindRow(Find(args.GetArgInt(a + 3)), out to)) throw new ConCommandException("Target row not found.");
            }
            else if (kind == "equip")
            {
                Army.Hero owner = Find(args.GetArgInt(a + 1));
                int slot = args.GetArgInt(a + 2);
                if (!root.Inventory.TryFindCell(owner, ItemIndex.None, true, -1, out from, slot)) throw new ConCommandException("Equipment cell not found.");
                if (!hover && !root.Inventory.TryFindRow(Find(args.GetArgInt(a + 3)), out to)) throw new ConCommandException("Target row not found.");
            }
            else if (kind == "spare")
            {
                if (!root.Inventory.TryFindCell(null, ItemIndex.None, false, args.GetArgInt(a + 1), out from)) throw new ConCommandException("Spare cell not found.");
                if (!hover && !root.Inventory.TryFindRow(Find(args.GetArgInt(a + 2)), out to)) throw new ConCommandException("Target row not found.");
            }
            else throw new ConCommandException("Unknown form.");

            if (hover) Client.ClientInput.InjectClick(from, 0);
            else Client.ClientInput.InjectDrag(from, to, 0, shift);
            Debug.Log($"HighCommand: {(hover ? "hover" : "drag")} {kind} from ({from.x:0},{from.y:0}) to ({to.x:0},{to.y:0})");
        }

        [ConCommand(commandName = "hc_kill_heroes", flags = HostOnly, helpText = "hc_kill_heroes [heroId]: kills one hero or the whole army (testing defeat and revival).")]
        private static void CcKillHeroes(ConCommandArgs args)
        {
            var run = Mode.HighCommandRun.Instance;
            if (run == null) throw new ConCommandException("Not in a High Command run.");
            int only = args.Count > 0 ? args.GetArgInt(0) : -1;
            int n = 0;
            foreach (var h in run.Army.Heroes)
            {
                if (only >= 0 && h.Id != only) continue;
                if (h.Health && h.Health.alive) { h.Health.Suicide(); n++; }
            }
            Debug.Log($"HighCommand: killed {n} hero(es).");
        }

        [ConCommand(commandName = "hc_revive", flags = HostOnly, helpText = "hc_revive [heroId]: revives a fallen hero for gold (the first fallen one without an id).")]
        private static void CcRevive(ConCommandArgs args)
        {
            var run = Mode.HighCommandRun.Instance;
            if (run == null) throw new ConCommandException("Not in a High Command run.");
            Army.Hero target = null;
            if (args.Count > 0)
            {
                foreach (var h in run.Army.Heroes) if (h.Id == args.GetArgInt(0)) target = h;
            }
            else target = Army.ArmyService.FirstFallen(run.Army.Heroes);
            string result = Army.ArmyService.TryRevive(target);
            Debug.Log("HighCommand: revive -> " + (result == null ? "ok" : result == Net.CommandChannel.Relayed ? "sent" : result));
        }

        [ConCommand(commandName = "hc_tp", flags = HostOnly, helpText = "hc_tp: the one-button teleporter action (activate / gather / leave).")]
        private static void CcTeleporter(ConCommandArgs args)
        {
            string result = Army.ArmyService.TryTeleporter();
            var state = Mode.HcState.Instance;
            Debug.Log($"HighCommand: teleporter action -> {result}; state={(state != null ? state.Teleporter.ToString() : "?")} charge={(state != null ? state.TeleCharge : 0f):0.00}");
        }

        [ConCommand(commandName = "hc_ability", flags = HostOnly, helpText = "hc_ability <0-3>: uses a commander ability (strike hits the army's centre, mark hits the nearest enemy).")]
        private static void CcAbility(ConCommandArgs args)
        {
            var run = Mode.HighCommandRun.Instance;
            if (run == null) throw new ConCommandException("Not in a High Command run.");
            int index = args.GetArgInt(0);

            Vector3 point = Vector3.zero;
            UnityEngine.Networking.NetworkInstanceId target = UnityEngine.Networking.NetworkInstanceId.Invalid;
            Vector3 center = Client.ClientRoot.ArmyCenter() ?? Vector3.zero;
            if (Army.CommanderAbilities.NeedsPoint(index)) point = center + Vector3.forward * 10f;
            if (Army.CommanderAbilities.NeedsTarget(index))
            {
                float best = float.MaxValue;
                foreach (var tc in TeamComponent.GetTeamMembers(TeamIndex.Monster))
                {
                    if (!tc || !tc.body || !tc.body.healthComponent || !tc.body.healthComponent.alive) continue;
                    float d = (tc.body.corePosition - center).sqrMagnitude;
                    var id = tc.body.GetComponent<UnityEngine.Networking.NetworkIdentity>();
                    if (d < best && id) { best = d; target = id.netId; point = tc.body.corePosition; }
                }
            }
            string result = Army.ArmyService.TryAbility(index, point, target);
            Debug.Log("HighCommand: ability " + index + " -> " + (result == null ? "ok" : result));
        }

        [ConCommand(commandName = "hc_spawn", flags = HostOnly, helpText = "hc_spawn <count> [MasterName] [distance]: spawns hostile AI around the army, 18-40 m away or about <distance> m (testing / load tests).")]
        private static void CcSpawn(ConCommandArgs args)
        {
            if (!UnityEngine.Networking.NetworkServer.active || !Run.instance)
                throw new ConCommandException("Only the host can do this, and a run must be in progress.");
            int count = args.Count > 0 ? Mathf.Clamp(args.GetArgInt(0), 1, 200) : 10;
            string name = args.Count > 1 ? args.GetArgString(1) : "LemurianMaster";
            float far = args.Count > 2 ? Mathf.Clamp(args.GetArgFloat(2), 5f, 500f) : 0f;
            GameObject prefab = MasterCatalog.FindMasterPrefab(name);
            if (!prefab) throw new ConCommandException("Unknown master: " + name);

            Vector3 center = Client.ClientRoot.ArmyCenter() ?? Vector3.zero;
            int made = 0;
            for (int i = 0; i < count; i++)
            {
                Vector2 ring = Random.insideUnitCircle.normalized * (far > 0f ? Random.Range(far * 0.85f, far) : Random.Range(18f, 40f));
                Vector3 at = Orders.Formation.SnapToGround(center + new Vector3(ring.x, 0f, ring.y));
                var summon = new MasterSummon
                {
                    masterPrefab = prefab,
                    position = at,
                    rotation = Quaternion.identity,
                    teamIndexOverride = TeamIndex.Monster,
                    ignoreTeamMemberLimit = true,
                    useAmbientLevel = true
                };
                if (summon.Perform()) made++;
            }
            Debug.Log($"HighCommand: spawned {made}/{count} x {name}.");
        }

        [ConCommand(commandName = "hc_ui", flags = HostOnly, helpText = "hc_ui on|off: shows or hides the whole interface of the mode (load tests).")]
        private static void CcUi(ConCommandArgs args)
        {
            var root = Client.ClientRoot.Instance;
            if (!root) throw new ConCommandException("Not in a High Command run.");
            bool off = args.Count > 0 && args.GetArgString(0) == "off";
            root.SetUiHidden(off);
            Debug.Log("HighCommand: interface " + (off ? "hidden" : "shown"));
        }

        [ConCommand(commandName = "hc_perf", flags = HostOnly, helpText = "Prints the cost of the mod's own work since the last call (ms per second) and the frame rate.")]
        private static void CcPerf(ConCommandArgs args)
        {
            int enemies = 0;
            foreach (TeamIndex team in new[] { TeamIndex.Monster, TeamIndex.Lunar, TeamIndex.Void })
                enemies += TeamComponent.GetTeamMembers(team).Count;
            var run = Mode.HighCommandRun.Instance;
            Debug.Log(Dev.Perf.Report() + $" | heroes={(run != null ? run.Army.Count : 0)} enemies={enemies}");
        }

        [ConCommand(commandName = "hc_cfg", flags = HostOnly, helpText = "hc_cfg <ArmyLimit|IgnoreUnlocks|AllowDuplicates|StartingGold|HeroBasePrice|AutoRetreat> <value>: changes a setting at runtime (testing).")]
        private static void CcCfg(ConCommandArgs args)
        {
            string key = args.GetArgString(0);
            string value = args.GetArgString(1);
            switch (key.ToLowerInvariant())
            {
                case "armylimit": Settings.ArmyLimit.Value = int.Parse(value); break;
                case "ignoreunlocks": Settings.IgnoreUnlocks.Value = bool.Parse(value); break;
                case "allowduplicates": Settings.AllowDuplicates.Value = bool.Parse(value); break;
                case "startinggold": Settings.StartingGold.Value = int.Parse(value); break;
                case "herobaseprice": Settings.HeroBasePrice.Value = int.Parse(value); break;
                case "autoretreat": Settings.AutoRetreat.Value = bool.Parse(value); break;
                case "verbose": Settings.VerboseLog.Value = bool.Parse(value); break;
                case "autobarrels": Settings.AutoOpenBarrels.Value = bool.Parse(value); break;
                case "autochests": Settings.AutoOpenChests.Value = bool.Parse(value); break;
                case "hunt": Settings.EnemiesHunt.Value = bool.Parse(value); break;
                case "huntrange": Settings.EnemyHuntRange.Value = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
                case "uiscale": Settings.UiScale.Value = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
                case "allowhidden": Settings.AllowHiddenSurvivors.Value = bool.Parse(value); break;
                default: throw new ConCommandException("Unknown setting.");
            }
            PublishSettings();
            Debug.Log($"HighCommand: {key} = {value}");
        }

        private static void PublishSettings()
        {
            Mode.HcState state = Mode.HcState.Instance;
            if (state) state.ServerPublishSettings();
        }

        [ConCommand(commandName = "hc_tp_charge", flags = HostOnly, helpText = "Fully charges the teleporter's holdout zone (testing).")]
        private static void CcTpCharge(ConCommandArgs args)
        {
            var tp = TeleporterInteraction.instance;
            if (!tp || !tp.holdoutZoneController) throw new ConCommandException("No teleporter.");
            tp.holdoutZoneController.FullyChargeHoldoutZone();
            Debug.Log("HighCommand: teleporter charge set to full.");
        }

        [ConCommand(commandName = "hc_flow", flags = HostOnly, helpText = "Prints the run-flow state: defeat countdown, teleporter, rally point.")]
        private static void CcFlow(ConCommandArgs args)
        {
            var run = Mode.HighCommandRun.Instance;
            var state = Mode.HcState.Instance;
            if (run == null || state == null) throw new ConCommandException("Not in a High Command run.");
            Debug.Log($"HighCommand flow: defeatIn={state.DefeatSeconds:0.0}s canRevive={state.DefeatCanRevive} teleporter={state.Teleporter} " +
                      $"charge={state.TeleCharge:0.00} radius={state.TeleRadius:0.0} at={state.TelePosition:0} rally={run.RallyPoint:0} " +
                      $"alive={run.Army.AliveCount}/{run.Army.Count} gold={Army.Economy.Gold} revivePrice={state.RevivePrice()} " +
                      $"participating={Run.instance.participatingPlayerCount} living={Run.instance.livingPlayerCount} " +
                      $"limit={state.ArmyLimit} priceMult={state.PriceMultiplier:0.00} mirror={state.ItemMirror}");
        }

        [ConCommand(commandName = "hc_rule", flags = HostOnly, helpText = "hc_rule <RuleName> <ChoiceName>: sets a lobby rule choice in the running game (testing), e.g. hc_rule HighCommand.ArmyLimit 12.")]
        private static void CcRule(ConCommandArgs args)
        {
            if (!Run.instance) throw new ConCommandException("No run.");
            RuleDef def = RuleCatalog.FindRuleDef(args.GetArgString(0));
            if (def == null) throw new ConCommandException("Unknown rule.");
            RuleChoiceDef choice = def.FindChoice(args.GetArgString(1));
            if (choice == null) throw new ConCommandException("Unknown choice.");
            Run.instance.ruleBook.ApplyChoice(choice);
            PublishSettings();
            Debug.Log($"HighCommand: rule {def.globalName} = {choice.localName}");
        }

        [ConCommand(commandName = "hc_drop_random", flags = HostOnly, helpText = "hc_drop_random <count>: adds that many random white/green/red items to the pool (layout and balance tests).")]
        private static void CcDropRandom(ConCommandArgs args)
        {
            if (!UnityEngine.Networking.NetworkServer.active || !Mode.HighCommandRun.IsActive || !Run.instance)
                throw new ConCommandException("Only the host can do this during a High Command run.");
            int count = Mathf.Clamp(args.GetArgInt(0), 1, 500);
            var lists = new[] { Run.instance.availableTier1DropList, Run.instance.availableTier2DropList, Run.instance.availableTier3DropList };
            var weights = new[] { 0.6f, 0.3f, 0.1f };
            int given = 0;
            for (int i = 0; i < count; i++)
            {
                float roll = Random.value;
                int pick = roll < weights[0] ? 0 : roll < weights[0] + weights[1] ? 1 : 2;
                var list = lists[pick];
                if (list == null || list.Count == 0) continue;
                PickupIndex p = list[Random.Range(0, list.Count)];
                if (Army.LootService.Grant(new UniquePickup(p), null)) given++;
            }
            Debug.Log($"HighCommand: dropped {given} random items.");
        }

        [ConCommand(commandName = "hc_name", flags = HostOnly, helpText = "hc_name <SurvivorName>: prints the survivor's display name as code points (font / localisation checks).")]
        private static void CcName(ConCommandArgs args)
        {
            SurvivorDef def = SurvivorCatalog.FindSurvivorDef(args.GetArgString(0));
            if (!def) throw new ConCommandException("Unknown survivor.");
            string token = def.displayNameToken;
            string text = Language.GetString(token);
            var sb = new StringBuilder();
            foreach (char c in text) sb.Append("U+").Append(((int)c).ToString("X4")).Append(' ');
            Debug.Log($"HighCommand name: token={token} length={text.Length} -> {sb}");
        }

        [ConCommand(commandName = "hc_rebalance", flags = HostOnly, helpText = "hc_rebalance on|off: a new recruit takes a share of the army's items (changes the config value).")]
        private static void CcRebalance(ConCommandArgs args)
        {
            if (args.Count > 0) Settings.RebalanceOnRecruit.Value = args.GetArgString(0).ToLowerInvariant() != "off";
            Debug.Log("HighCommand: rebalance on recruit is " + Settings.RebalanceOnRecruit.Value);
        }

        [ConCommand(commandName = "hc_sharing", flags = HostOnly, helpText = "hc_sharing split|mirror: how the pool's items are shared between heroes (changes the config value).")]
        private static void CcSharing(ConCommandArgs args)
        {
            if (args.Count > 0)
            {
                bool mirror = args.GetArgString(0).ToLowerInvariant() == "mirror";
                Settings.ItemSharing.Value = mirror ? Army.ItemDistributor.SharingMode.Mirror : Army.ItemDistributor.SharingMode.Split;
                PublishSettings();
            }
            Debug.Log("HighCommand: item sharing is " + Settings.ItemSharing.Value);
        }

        [ConCommand(commandName = "hc_netcmds", flags = HostOnly, helpText = "hc_netcmds [on|off]: send orders / recruiting / item moves through the network command channel even on the host (transport test).")]
        private static void CcNetCmds(ConCommandArgs args)
        {
            if (args.Count > 0) Net.CommandChannel.ForceRelay = args.GetArgString(0).ToLowerInvariant() == "on";
            Debug.Log("HighCommand: command relay is " + (Net.CommandChannel.ForceRelay ? "ON" : "off") + ".");
        }

        [ConCommand(commandName = "hc_inventory", flags = HostOnly, helpText = "Prints what every hero carries (items and equipment).")]
        private static void CcInventory(ConCommandArgs args)
        {
            var run = Mode.HighCommandRun.Instance;
            if (run == null) throw new ConCommandException("Not in a High Command run.");
            foreach (Army.Hero h in run.Army.Heroes)
            {
                var inv = h.Master ? h.Master.inventory : null;
                var sb = new StringBuilder($"HighCommand inventory {h}:");
                int total = 0;
                if (inv)
                {
                    foreach (ItemIndex idx in inv.itemAcquisitionOrder)
                    {
                        int n = inv.GetItemCountPermanent(idx);
                        var def = ItemCatalog.GetItemDef(idx);
                        if (n <= 0 || def == null || def.hidden || def.tier == ItemTier.NoTier) continue;
                        total += n;
                        sb.Append($" {def.name}x{n}");
                    }
                    var eq = new StringBuilder();
                    int slots = Army.HeroEquipment.SlotCount(inv);
                    for (int s = 0; s < slots; s++)
                    {
                        EquipmentIndex e = Army.HeroEquipment.InSlot(inv, s);
                        eq.Append(s > 0 ? "," : "").Append(e == EquipmentIndex.None ? "None" : EquipmentCatalog.GetEquipmentDef(e).name);
                    }
                    sb.Append($" | total={total} equipment=[{eq}] active={inv.activeEquipmentSlot}");
                }
                Debug.Log(sb.ToString());
            }
        }

        [ConCommand(commandName = "hc_items_move", flags = HostOnly, helpText = "hc_items_move <fromHeroId> <toHeroId> <ItemName> [count]: hands items from one hero to another.")]
        private static void CcItemsMove(ConCommandArgs args)
        {
            var run = Mode.HighCommandRun.Instance;
            if (run == null || !UnityEngine.Networking.NetworkServer.active) throw new ConCommandException("Only the host can do this during a High Command run.");
            Army.Hero from = null, to = null;
            foreach (Army.Hero h in run.Army.Heroes)
            {
                if (h.Id == args.GetArgInt(0)) from = h;
                if (h.Id == args.GetArgInt(1)) to = h;
            }
            ItemIndex idx = ItemCatalog.FindItemIndex(args.GetArgString(2));
            if (from == null || to == null || idx == ItemIndex.None) throw new ConCommandException("Unknown hero or item.");
            int count = args.Count > 3 ? Mathf.Max(1, args.GetArgInt(3)) : 1;
            string error = Army.ArmyService.TryMoveItem(from, to, idx, count);
            if (error == Net.CommandChannel.Relayed) Debug.Log("HighCommand: move request sent to the server.");
            else Debug.Log(error == null ? $"HighCommand: moved {count} x {args.GetArgString(2)} {from} -> {to}." : "HighCommand: move failed: " + error);
        }

        [ConCommand(commandName = "hc_items_auto", flags = HostOnly, helpText = "Takes all pooled items back and deals them out again by role.")]
        private static void CcItemsAuto(ConCommandArgs args)
        {
            string error = Army.ArmyService.RedistributeItems();
            if (error == Net.CommandChannel.Relayed) Debug.Log("HighCommand: redistribute request sent to the server.");
            else Debug.Log(error == null ? "HighCommand: items redistributed." : "HighCommand: " + error);
        }

        [ConCommand(commandName = "hc_interactables", flags = HostOnly, helpText = "Counts interactables on the map by name.")]
        private static void CcInteractables(ConCommandArgs args)
        {
            var counts = new System.Collections.Generic.Dictionary<string, int>();
            foreach (var p in UnityEngine.Object.FindObjectsOfType<PurchaseInteraction>())
            {
                string n = p.gameObject.name.Replace("(Clone)", "");
                counts[n] = counts.TryGetValue(n, out int c) ? c + 1 : 1;
            }
            var sb = new StringBuilder("HighCommand interactables:");
            foreach (var kv in counts) sb.Append($" {kv.Key}x{kv.Value}");
            if (counts.Count == 0) sb.Append(" (none)");
            Debug.Log(sb.ToString());
        }

        private static GameObject foundThing;

        [ConCommand(commandName = "hc_find", flags = HostOnly, helpText = "hc_find <name or kind fragment>: the nearest usable object of that kind (chest, shrine, barrel ...) to the army; the camera flies to it (testing).")]
        private static void CcFind(ConCommandArgs args)
        {
            string what = args.Count > 0 ? args.GetArgString(0) : "chest";
            Vector3 from = Client.ClientRoot.ArmyCenter() ?? Vector3.zero;
            GameObject best = null;
            float bestD = float.MaxValue;
            foreach (IInteractable i in InterfaceInstanceTracker<IInteractable>.GetInstancesList())
            {
                var mb = i as MonoBehaviour;
                if (!mb || !mb.isActiveAndEnabled || !Army.Interactables.Usable(mb.gameObject) || Targeted(mb.gameObject)) continue;
                string kind = Army.Interactables.KindOf(mb.gameObject).ToString();
                if (what.StartsWith("kind:", System.StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(kind, what.Substring(5), System.StringComparison.OrdinalIgnoreCase)) continue;
                }
                else if (mb.gameObject.name.IndexOf(what, System.StringComparison.OrdinalIgnoreCase) < 0
                         && kind.IndexOf(what, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                float d = (mb.transform.position - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = mb.gameObject; }
            }
            foundThing = best;
            if (!best) { Debug.Log($"HighCommand find: no usable '{what}'"); return; }
            Army.Interactables.TryGetPrice(best, out CostTypeIndex type, out int cost);
            Debug.Log($"HighCommand find: {best.name} kind={Army.Interactables.KindOf(best)} at {best.transform.position:0} " +
                      $"dist={Mathf.Sqrt(bestD):0} price={type}:{cost} affordable={Army.Interactables.Affordable(best)}");
            var cam = Client.RtsCamera.Instance;
            if (cam) cam.FocusOn(best.transform.position, snap: true);
        }

        // an object some hero is already on the way to (hc_find looks for the next one)
        private static bool Targeted(GameObject thing)
        {
            var run = Mode.HighCommandRun.Instance;
            if (run == null) return false;
            foreach (var h in run.Army.Heroes)
            {
                Orders.HeroBrain b = h.Brain;
                if (!b) continue;
                if (b.Current != null && b.Current.Target == thing) return true;
                foreach (var o in b.Queue) if (o.Target == thing) return true;
            }
            return false;
        }

        [ConCommand(commandName = "hc_click_found", flags = HostOnly, helpText = "hc_click_found [button] [shift] [hover]: clicks on the object found by hc_find (right button by default); hover only puts the pointer on it.")]
        private static void CcClickFound(ConCommandArgs args)
        {
            if (!foundThing) throw new ConCommandException("Nothing found (hc_find first).");
            int button = args.Count > 0 ? args.GetArgInt(0) : 1;
            // aim at the middle of its colliders, where the pointer would be put by a player
            Vector3 aim = foundThing.transform.position + Vector3.up * 0.8f;
            Bounds b = default;
            bool any = false;
            foreach (Collider c in foundThing.GetComponentsInChildren<Collider>())
            {
                if (!c || !c.enabled) continue;
                if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
            }
            if (any) aim = b.center;
            if (!Client.WorldPicker.Project(aim, 1f, out Vector2 at, out float _))
                throw new ConCommandException("The object is behind the camera.");
            GameObject picked = Client.WorldPicker.PickInteractable(at);
            Debug.Log($"HighCommand: pointer on {foundThing.name} at ({at.x:0},{at.y:0}); picked={(picked ? picked.name : "nothing")}");
            if (HasFlag(args, 1, "hover")) Client.ClientInput.InjectMove(at, HasFlag(args, 1, "shift"));
            else Client.ClientInput.InjectClick(at, button, HasFlag(args, 1, "shift"));
        }

        [ConCommand(commandName = "hc_move_found", flags = HostOnly, helpText = "hc_move_found <heroId>: orders the hero to stand 6 m beside the object found by hc_find (testing what heroes do near it).")]
        private static void CcMoveFound(ConCommandArgs args)
        {
            if (!foundThing) throw new ConCommandException("Nothing found (hc_find first).");
            Vector3 at = Orders.Formation.SnapToGround(foundThing.transform.position + new Vector3(6f, 0f, 0f));
            Orders.OrderService.Execute(new Orders.OrderCommand { HeroIds = new[] { args.GetArgInt(0) }, Kind = Orders.OrderKind.Move, Point = at });
            Debug.Log($"HighCommand: hero {args.GetArgInt(0)} sent beside {foundThing.name} at {at:0}");
        }

        [ConCommand(commandName = "hc_invulnerable", flags = HostOnly, helpText = "hc_invulnerable <true|false>: heroes take no damage (a test that must not be cut short by a death).")]
        private static void CcInvulnerable(ConCommandArgs args)
        {
            var run = Mode.HighCommandRun.Instance;
            if (run == null) throw new ConCommandException("Not in a High Command run.");
            bool on = args.Count == 0 || bool.Parse(args.GetArgString(0));
            int n = 0;
            foreach (var h in run.Army.Heroes)
                if (h.Body && h.Body.healthComponent) { h.Body.healthComponent.godMode = on; n++; }
            Debug.Log($"HighCommand: {n} hero(es) invulnerable={on}.");
        }

        [ConCommand(commandName = "hc_loot", flags = HostOnly, helpText = "Prints the purse, the pool, the stage's chests and what every hero is doing.")]
        private static void CcLoot(ConCommandArgs args)
        {
            var run = Mode.HighCommandRun.Instance;
            var s = Mode.HcState.Instance;
            if (run == null || !s) throw new ConCommandException("Not in a High Command run.");
            Army.Interactables.CountChests(out int opened, out int chests);
            Inventory pool = Army.LootService.Pool;
            int items = 0;
            if (pool)
                foreach (ItemIndex idx in pool.itemAcquisitionOrder) items += pool.GetItemCountPermanent(idx);
            int barrels = 0, barrelsOpen = 0;
            foreach (BarrelInteraction b in InstanceTracker.GetInstancesList<BarrelInteraction>())
            {
                if (!b) continue;
                barrels++;
                if (b.Networkopened) barrelsOpen++;
            }
            var sb = new StringBuilder($"HighCommand loot: gold={s.Gold} poolItems={items} chests={opened}/{chests} barrels={barrelsOpen}/{barrels} known={Client.Scouting.KnownCount} droplets={InstanceTracker.GetInstancesList<PickupDropletController>().Count}");
            foreach (var h in run.Army.Heroes)
            {
                Orders.HeroBrain b = h.Brain;
                if (!b) continue;
                sb.Append($"\n  {h} order={(b.Current != null ? b.Current.ToString() + (b.Current.Errand ? "*" : "") : "-")} queue=[");
                foreach (var o in b.Queue) sb.Append(o).Append(o.Errand ? "* " : " ");
                sb.Append($"] state={b.DebugState} pos={h.Position:0} anchor={b.Anchor:0}");
            }
            Debug.Log(sb.ToString());
        }

        [ConCommand(commandName = "hc_economy", flags = HostOnly, helpText = "Prints gold, income and prices.")]
        private static void CcEconomy(ConCommandArgs args)
        {
            var s = Mode.HcState.Instance;
            if (!s) throw new ConCommandException("Not in a High Command run.");
            Debug.Log($"HighCommand economy: gold={s.Gold} bought={s.Bought} nextHero=${s.NextHeroPrice()} revive=${s.RevivePrice()} " +
                      $"income/min={Army.Economy.IncomePerMinute():0} limit={s.ArmyLimit} heroes={s.Heroes.Count}");
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

        [ConCommand(commandName = "hc_drag", flags = HostOnly, helpText = "hc_drag <x1> <y1> <x2> <y2> [shift] [hold] [mid]: injects a left-button drag (mid: the middle button, which turns / tilts the camera); with hold the button stays down until hc_input_end.")]
        private static void CcDrag(ConCommandArgs args)
        {
            Client.ClientInput.InjectDrag(new Vector2(args.GetArgFloat(0), args.GetArgFloat(1)),
                new Vector2(args.GetArgFloat(2), args.GetArgFloat(3)), HasFlag(args, 4, "mid") ? 2 : 0, HasFlag(args, 4, "shift"), !HasFlag(args, 4, "hold"));
        }

        [ConCommand(commandName = "hc_scroll", flags = HostOnly, helpText = "hc_scroll <notches>: injects a mouse wheel turn (negative = zoom out); needs hc_mouse first.")]
        private static void CcScroll(ConCommandArgs args) => Client.ClientInput.InjectScroll(args.GetArgFloat(0));

        [ConCommand(commandName = "hc_key",flags = HostOnly, helpText = "hc_key <KeyCode> [ctrl] [shift] [alt]: injects a key press.")]
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

        [ConCommand(commandName = "hc_mouse", flags = HostOnly, helpText = "hc_mouse <x> <y> [shift]: moves the synthetic pointer there and leaves it (hc_input_end releases it).")]
        private static void CcMouse(ConCommandArgs args)
        {
            Client.ClientInput.InjectMove(new Vector2(args.GetArgFloat(0), args.GetArgFloat(1)), HasFlag(args, 2, "shift"));
        }

        [ConCommand(commandName = "hc_mouse_enemy", flags = HostOnly, helpText = "hc_mouse_enemy [shift]: moves the synthetic pointer onto the enemy nearest to the camera focus.")]
        private static void CcMouseEnemy(ConCommandArgs args)
        {
            var cam = Client.RtsCamera.Instance;
            if (!cam) throw new ConCommandException("No camera.");
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
            Debug.Log($"HighCommand: pointer on enemy {best.name} at ({at.x:0},{at.y:0})");
            // the pointer stays on the enemy while it walks (a fixed pixel would be on bare ground a moment later)
            CharacterBody tracked = best;
            Client.ClientInput.InjectTrack(() => TrackBody(tracked), HasFlag(args, 0, "shift"));
        }

        private static Vector2? TrackBody(CharacterBody body)
        {
            if (!body || !body.healthComponent || !body.healthComponent.alive) return null;
            return Client.WorldPicker.Project(body.corePosition, 1f, out Vector2 s, out float _) ? s : (Vector2?)null;
        }

        [ConCommand(commandName = "hc_mouse_hero", flags = HostOnly, helpText = "hc_mouse_hero <heroId>: moves the synthetic pointer onto the hero's body.")]
        private static void CcMouseHero(ConCommandArgs args)
        {
            var run = Mode.HighCommandRun.Instance;
            if (run == null) throw new ConCommandException("Not in a High Command run.");
            int id = args.GetArgInt(0);
            foreach (var h in run.Army.Heroes)
            {
                if (h.Id != id) continue;
                if (!h.Body) throw new ConCommandException("Hero has no body.");
                if (!Client.WorldPicker.Project(h.Body.corePosition, 1f, out Vector2 _, out float _))
                    throw new ConCommandException("Hero is behind the camera.");
                var tracked = h;
                Client.ClientInput.InjectTrack(() => TrackBody(tracked.Body));
                return;
            }
            throw new ConCommandException("No such hero.");
        }

        [ConCommand(commandName = "hc_cursor", flags = HostOnly, helpText = "Prints what the pointer badge shows now.")]
        private static void CcCursor(ConCommandArgs args)
        {
            var root = Client.ClientRoot.Instance;
            if (!root) throw new ConCommandException("Not in a High Command run.");
            var c = root.CursorInfo;
            Debug.Log($"HighCommand cursor: show={c.Show} glyph={c.Glyph} label='{c.Label}' preview={c.Preview} point={c.Point:0} queued={c.Queued} mouse={Client.ClientInput.Mouse:0}");
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

        [ConCommand(commandName = "hc_next_stage", flags = HostOnly, helpText = "Host: hc_next_stage [scene]: advances the run to the next stage (or to the named scene, e.g. bazaar) immediately.")]
        private static void CcNextStage(ConCommandArgs args)
        {
            if (!UnityEngine.Networking.NetworkServer.active || !Run.instance)
                throw new ConCommandException("Only the host can do this, and a run must be in progress.");
            SceneDef next = null;
            if (args.Count > 0)
            {
                next = SceneCatalog.FindSceneDef(args.GetArgString(0));
                if (!next) throw new ConCommandException("No such scene: " + args.GetArgString(0));
            }
            else
            {
                if (!Run.instance.nextStageScene)
                    Run.instance.PickNextStageSceneFromCurrentSceneDestinations();
                next = Run.instance.nextStageScene;
            }
            if (!next) throw new ConCommandException("No next stage is known.");
            Debug.Log("HighCommand: advancing to " + next.cachedName);
            Run.instance.AdvanceStage(next);
        }

        [ConCommand(commandName = "hc_upgrade", flags = HostOnly, helpText = "hc_upgrade [0-3]: buys an army upgrade (0 weapons, 1 armor, 2 medicine, 3 logistics) and prints the heroes' stats.")]
        private static void CcUpgrade(ConCommandArgs args)
        {
            if (args.Count > 0)
            {
                string error = Army.ArmyUpgrades.Buy(args.GetArgInt(0));
                Debug.Log("HighCommand: upgrade -> " + (error ?? "ok"));
            }
            PrintHeroStats();
        }

        [ConCommand(commandName = "hc_veteran", flags = HostOnly, helpText = "hc_veteran <hero id>: gives the hero the kills for its next veteran rank.")]
        private static void CcVeteran(ConCommandArgs args)
        {
            var run = Mode.HighCommandRun.Instance;
            if (run == null) throw new ConCommandException("No High Command run.");
            int id = args.GetArgInt(0);
            foreach (var hero in run.Army.Heroes)
            {
                if (hero.Id != id || hero.Rank >= Army.ArmyUpgrades.RankKills.Length) continue;
                hero.Kills = Army.ArmyUpgrades.RankKills[hero.Rank] - 1;
                // one more kill through the real counting path
                var report = new DamageReport(new DamageInfo { attacker = hero.BodyObject }, null, 0f, 0f);
                Army.ArmyUpgrades.CountKill(report);
            }
            PrintHeroStats();
        }

        private static void PrintHeroStats()
        {
            var run = Mode.HighCommandRun.Instance;
            if (run == null) return;
            var sb = new StringBuilder("HighCommand stats: upgrades=" + string.Join(",", Army.ArmyUpgrades.Levels));
            foreach (var hero in run.Army.Heroes)
            {
                CharacterBody b = hero.Body;
                if (!b) continue;
                b.RecalculateStats();
                sb.Append($"\n  {hero} rank={hero.Rank} kills={hero.Kills} damage={b.damage:0.0} armor={b.armor:0} regen={b.regen:0.00} speed={b.moveSpeed:0.0}");
            }
            Debug.Log(sb.ToString());
        }

        [ConCommand(commandName = "hc_dump_scenes", flags = HostOnly, helpText = "Lists the scenes where the game does not bring allied AI along (suppressNpcEntry).")]
        private static void CcDumpScenes(ConCommandArgs args)
        {
            var sb = new StringBuilder("HighCommand scenes without NPC entry:");
            foreach (SceneDef def in SceneCatalog.allSceneDefs)
                if (def && def.suppressNpcEntry) sb.Append(' ').Append(def.cachedName).Append('(').Append(def.sceneType).Append(')');
            Debug.Log(sb.ToString());
        }

        [ConCommand(commandName = "hc_ff", flags = HostOnly, helpText = "hc_ff [chaos on|off] | [hit]: friendly fire check - blocked hero-to-ally hits so far; 'chaos on' turns the game's friendly fire on (as the Chaos artifact does); 'hit' makes hero 1 hit hero 2 for 30 damage.")]
        private static void CcFriendlyFire(ConCommandArgs args)
        {
            string what = args.Count > 0 ? args.GetArgString(0) : "";
            var run = Mode.HighCommandRun.Instance;
            if (what == "chaos")
            {
                bool on = args.Count < 2 || args.GetArgString(1) != "off";
                FriendlyFireManager.friendlyFireMode = on ? FriendlyFireManager.FriendlyFireMode.FriendlyFire : FriendlyFireManager.FriendlyFireMode.Off;
            }
            else if (what == "hit")
            {
                if (run == null || run.Army.Count < 2) throw new ConCommandException("Needs two heroes.");
                CharacterBody a = run.Army.Heroes[0].Body, b = run.Army.Heroes[1].Body;
                if (!a || !b) throw new ConCommandException("A hero has no body.");
                float before = b.healthComponent.health;
                b.healthComponent.TakeDamage(new DamageInfo
                {
                    attacker = a.gameObject, inflictor = a.gameObject, damage = 30f, position = b.corePosition,
                    procCoefficient = 1f, damageColorIndex = DamageColorIndex.Default, crit = false
                });
                Debug.Log($"HighCommand ff: hero {run.Army.Heroes[0].Id} hit hero {run.Army.Heroes[1].Id} for 30: health {before:0} -> {b.healthComponent.health:0}");
            }
            Debug.Log($"HighCommand ff: mode={FriendlyFireManager.friendlyFireMode} blocked={Mode.RunPatches.NoFriendlyFire.Blocked}");
        }

        [ConCommand(commandName = "hc_hunt",flags = HostOnly, helpText = "Prints how many enemies have a target, how many stand idle and how far the idle ones are from the army.")]
        private static void CcHunt(ConCommandArgs args) => Debug.Log("HighCommand hunt: " + Mode.EnemyHunt.Describe());

        [ConCommand(commandName = "hc_kill_enemies",flags = HostOnly, helpText = "Host: kills every enemy on the map (monster and lunar teams).")]
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
            string mode = args.Count > 0 ? args.GetArgString(0) : "";
            bool click = mode == "click";
            if (mode == "real")
            {
                // what a mouse click on the title menu's button really runs (its persistent listeners)
                foreach (var button in Resources.FindObjectsOfTypeAll<RoR2.UI.HGButton>())
                {
                    if (!button.gameObject.scene.IsValid() || !button.gameObject.activeInHierarchy) continue;
                    var label = button.GetComponentInChildren<RoR2.UI.LanguageTextMeshController>(true);
                    if (label && label.token == "TITLE_EXTRAGAMEMODE")
                    {
                        Debug.Log("HighCommand: invoking the real title button " + button.name);
                        button.onClick.Invoke();
                        return;
                    }
                }
                throw new ConCommandException("The title menu's Extra Game Mode button was not found.");
            }
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

        [ConCommand(commandName = "hc_menu_buttons", flags = HostOnly, helpText = "Lists the visible menu buttons with their label token and what their click runs.")]
        private static void CcMenuButtons(ConCommandArgs args)
        {
            var sb = new StringBuilder("HighCommand menu buttons:\n");
            foreach (var button in Resources.FindObjectsOfTypeAll<RoR2.UI.HGButton>())
            {
                if (!button.gameObject.scene.IsValid() || !button.gameObject.activeInHierarchy) continue;
                var label = button.GetComponentInChildren<RoR2.UI.LanguageTextMeshController>(true);
                sb.Append($"  {button.name} [parent {(button.transform.parent ? button.transform.parent.name : "-")}] token={(label ? label.token : "-")} click=");
                int n = button.onClick.GetPersistentEventCount();
                for (int i = 0; i < n; i++)
                {
                    Object target = button.onClick.GetPersistentTarget(i);
                    sb.Append($"[{(target ? target.GetType().Name : "null")}.{button.onClick.GetPersistentMethodName(i)}] ");
                }
                sb.AppendLine();
            }
            Debug.Log(sb.ToString());
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
