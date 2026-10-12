using System;
using System.Collections.Generic;
using HighCommand.Army;
using HighCommand.Mode;
using HighCommand.UI;
using RoR2;
using RoR2.UI;
using UnityEngine;

namespace HighCommand.Client
{
    /// <summary>
    /// Everything that runs on the player's machine while a High Command run is live: the RTS camera, the input gate,
    /// the UI canvas with its cursor, minimap, selection and help sheet. Created by HighCommandRun, destroyed with it.
    /// </summary>
    [DefaultExecutionOrder(10000)] // after the stock camera rig has placed the camera: overlays use this frame's pose
    internal sealed class ClientRoot : MonoBehaviour
    {
        internal static ClientRoot Instance { get; private set; }

        internal RtsCamera Camera { get; private set; }

        internal Selection Selection { get; private set; }

        private Canvas canvas;
        private Minimap minimap;
        private HelpOverlay help;
        private WorldOverlay overlay;
        private SelectionPanel selectionPanel;
        private SelectionController selectionController;

        private bool mapDirty = true;
        private bool focusedThisStage;
        private bool draggingMinimap;

        private static readonly IReadOnlyList<Hero> NoHeroes = new List<Hero>();

        private void Awake()
        {
            Instance = this;
            Camera = gameObject.AddComponent<RtsCamera>();
            Selection = new Selection();

            canvas = UiKit.CreateCanvas("HighCommand.UI", 100);
            canvas.transform.SetParent(transform, false);
            canvas.gameObject.AddComponent<MPEventSystemProvider>();
            canvas.gameObject.AddComponent<MPEventSystemLocator>();
            // The stock cursor opener shows the OS-confined mouse cursor the same way menus do.
            canvas.gameObject.AddComponent<CursorOpener>();

            overlay = new WorldOverlay(canvas.transform);   // first: it draws underneath the panels
            minimap = new Minimap(canvas.transform);
            selectionPanel = new SelectionPanel(canvas.transform);
            help = new HelpOverlay(canvas.transform);
            selectionController = new SelectionController(Selection, overlay);

            RegisterHelp();
            Stage.onStageStartGlobal += OnStageStart;
        }

        private void OnDestroy()
        {
            Stage.onStageStartGlobal -= OnStageStart;
            minimap?.Destroy();
            help?.Destroy();
            overlay?.Destroy();
            selectionPanel?.Destroy();
            if (Instance == this) Instance = null;
        }

        private static void RegisterHelp()
        {
            HelpOverlay.Clear();
            HelpOverlay.Add(
                $"{Settings.KeyUp.Value} {Settings.KeyLeft.Value} {Settings.KeyDown.Value} {Settings.KeyRight.Value}",
                Loc.Get("HIGHCOMMAND_HELP_PAN"));
            HelpOverlay.Add(Loc.Get("HIGHCOMMAND_KEY_EDGE"), Loc.Get("HIGHCOMMAND_HELP_EDGE"));
            HelpOverlay.Add(Loc.Get("HIGHCOMMAND_KEY_WHEEL"), Loc.Get("HIGHCOMMAND_HELP_ZOOM"));
            HelpOverlay.Add($"{Loc.Get("HIGHCOMMAND_KEY_MMB")} / {Settings.KeyRotateLeft.Value} {Settings.KeyRotateRight.Value}", Loc.Get("HIGHCOMMAND_HELP_ROTATE"));
            HelpOverlay.Add(Settings.KeyFocusSelected.Value.ToString(), Loc.Get("HIGHCOMMAND_HELP_FOCUS"));
            HelpOverlay.Add(Loc.Get("HIGHCOMMAND_KEY_MINIMAP"), Loc.Get("HIGHCOMMAND_HELP_MINIMAP"));
            HelpOverlay.Add(Loc.Get("HIGHCOMMAND_KEY_LMB"), Loc.Get("HIGHCOMMAND_HELP_SELECT"));
            HelpOverlay.Add("Shift + " + Loc.Get("HIGHCOMMAND_KEY_LMB"), Loc.Get("HIGHCOMMAND_HELP_SELECT_ADD"));
            HelpOverlay.Add(Loc.Get("HIGHCOMMAND_KEY_DOUBLE"), Loc.Get("HIGHCOMMAND_HELP_SELECT_TYPE"));
            HelpOverlay.Add("Ctrl + A", Loc.Get("HIGHCOMMAND_HELP_SELECT_ALL"));
            HelpOverlay.Add("Ctrl + 1..9", Loc.Get("HIGHCOMMAND_HELP_GROUP_SET"));
            HelpOverlay.Add("1..9 (x2)", Loc.Get("HIGHCOMMAND_HELP_GROUP_RECALL"));
            HelpOverlay.Add("Tab", Loc.Get("HIGHCOMMAND_HELP_CYCLE"));
            HelpOverlay.Add(Settings.KeyHelp.Value.ToString(), Loc.Get("HIGHCOMMAND_HELP_HELP"));
            HelpOverlay.Add("Esc", Loc.Get("HIGHCOMMAND_HELP_PAUSE"));
        }

        private void OnStageStart(Stage stage)
        {
            mapDirty = true;
            focusedThisStage = false;
        }

        private IReadOnlyList<Hero> CurrentHeroes()
        {
            HighCommandRun run = HighCommandRun.Instance;
            return run != null ? run.Army.Heroes : NoHeroes;
        }

        private void Update()
        {
            ClientInput.Refresh();

            // The map picture is built once the stage's node graph exists.
            if (mapDirty && SceneInfo.instance && SceneInfo.instance.groundNodes)
            {
                mapDirty = false;
                try
                {
                    MapInfo.Rebuild();
                    minimap.OnMapChanged();
                }
                catch (Exception e) { Plugin.Log.LogError("MapInfo.Rebuild failed: " + e); }
            }

            // Show the interface only while a stage is running (not in loading screens / game over).
            bool inStage = Stage.instance != null && Run.instance != null && !Run.instance.isGameOverServer;
            minimap.SetVisible(inStage);

            if (!focusedThisStage && inStage && Camera)
                TryInitialFocus();

            if (ClientInput.KeyDown(Settings.KeyHelp.Value)) help.Toggle();
            if (help.Visible && ClientInput.RawKeyDown(KeyCode.Escape)) help.Hide();

            IReadOnlyList<Hero> heroes = CurrentHeroes();
            bool inputAllowed = inStage && !PauseManager.isPaused && !help.Visible;

            if (Camera && inStage)
            {
                HandleMinimapMouse();
                if (ClientInput.KeyDown(Settings.KeyFocusSelected.Value)) FocusSelectionOrArmy();
            }

            if (inStage) selectionController.Tick(heroes, Camera, inputAllowed);
            else selectionController.Tick(heroes, Camera, false);

            selectionPanel.Tick(Selection, inStage);
        }

        // Overlays use the camera pose of this very frame (this component runs after the stock camera rig).
        private void LateUpdate()
        {
            bool inStage = Stage.instance != null && Run.instance != null && !Run.instance.isGameOverServer;
            if (inStage && Camera && Camera.SceneCam) overlay.Draw(Camera, Selection, CurrentHeroes());
            else overlay.Draw(null, Selection, CurrentHeroes());

            minimap.Tick(Camera, Time.unscaledDeltaTime);
        }

        /// <summary>Looks at the army when a stage begins (first friendly body appears).</summary>
        private void TryInitialFocus()
        {
            Vector3? where = ArmyCenter();
            if (!where.HasValue) return;
            Camera.FocusOn(where.Value, snap: true);
            Camera.SetDistance(Mathf.Lerp(Settings.CameraMinDistance.Value, Settings.CameraMaxDistance.Value, 0.28f));
            focusedThisStage = true;
        }

        private void HandleMinimapMouse()
        {
            if (!ClientInput.MouseHeld(0)) { draggingMinimap = false; return; }

            bool pressed = ClientInput.MouseDown(0);
            if (pressed && minimap.Contains(ClientInput.Mouse)) draggingMinimap = true;
            if (!draggingMinimap) return;

            Vector3? world = minimap.ScreenToWorld(ClientInput.Mouse);
            if (world.HasValue)
                Camera.FocusOn(new Vector3(world.Value.x, Camera.Focus.y, world.Value.z));
        }

        private void FocusSelectionOrArmy()
        {
            Vector3? where = Selection.Center() ?? ArmyCenter();
            if (where.HasValue) Camera.FocusOn(where.Value);
        }

        /// <summary>Average position of living friendly bodies (hero bodies first when the roster is known).</summary>
        internal static Vector3? ArmyCenter()
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            var bodies = CharacterBody.readOnlyInstancesList;
            for (int pass = 0; pass < 2 && count == 0; pass++)
            {
                for (int i = 0; i < bodies.Count; i++)
                {
                    CharacterBody b = bodies[i];
                    if (!b || !b.healthComponent || !b.healthComponent.alive || !b.teamComponent) continue;
                    if (b.teamComponent.teamIndex != TeamIndex.Player) continue;
                    if (pass == 0 && HeroMarker.Of(b) == null) continue; // server knows the heroes; clients fall through
                    sum += b.corePosition;
                    count++;
                }
            }
            return count > 0 ? sum / count : (Vector3?)null;
        }
    }
}
