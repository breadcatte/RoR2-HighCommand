using BepInEx.Configuration;
using UnityEngine;

namespace HighCommand
{
    /// <summary>
    /// All user-facing settings (BepInEx config: BepInEx\config\kantemiir.HighCommand.cfg). Every number has an
    /// allowed range so a typo in the file can never break the mode.
    /// </summary>
    internal static class Settings
    {
        private static ConfigFile cfg;

        // ---- camera ----
        internal static ConfigEntry<float> CameraPanSpeed;
        internal static ConfigEntry<float> CameraZoomSpeed;
        internal static ConfigEntry<float> CameraMinDistance;
        internal static ConfigEntry<float> CameraMaxDistance;
        internal static ConfigEntry<float> CameraPitch;
        internal static ConfigEntry<float> CameraFov;
        internal static ConfigEntry<float> CameraRotateSpeed;
        internal static ConfigEntry<float> CameraSmoothing;
        internal static ConfigEntry<bool> EdgeScroll;
        internal static ConfigEntry<int> EdgeScrollPixels;

        // ---- keys ----
        internal static ConfigEntry<KeyCode> KeyUp, KeyDown, KeyLeft, KeyRight, KeyRotateLeft, KeyRotateRight;
        internal static ConfigEntry<KeyCode> KeyFocusSelected, KeyHelp, KeyFastCamera;
        internal static ConfigEntry<KeyCode> KeyAttackMove, KeyStop, KeyHold, KeyFocusFire, KeyRetreat, KeyPatrol, KeyRally;
        internal static ConfigEntry<KeyCode> KeyCastSecondary, KeyCastUtility, KeyCastSpecial, KeyCastEquipment;
        internal static ConfigEntry<KeyCode> KeyStanceAggressive, KeyStanceDefensive, KeyStancePassive;
        internal static ConfigEntry<KeyCode> KeyToggleAuto;
        internal static ConfigEntry<KeyCode> KeyFormationCycle;

        // ---- minimap ----
        internal static ConfigEntry<bool> MinimapEnabled;
        internal static ConfigEntry<float> MinimapSize;

        // ---- audio ----
        internal static ConfigEntry<bool> ListenerAtFocus;

        // ---- dev ----
        internal static ConfigEntry<bool> VerboseLog;

        internal static void Init(ConfigFile file)
        {
            cfg = file;

            CameraPanSpeed = F("Camera", "PanSpeed", 1.0f, 0.2f, 4f, "Camera pan speed multiplier (WASD and screen edge). The speed also scales with zoom.");
            CameraZoomSpeed = F("Camera", "ZoomSpeed", 1.0f, 0.2f, 4f, "Mouse wheel zoom speed multiplier.");
            CameraMinDistance = F("Camera", "MinDistance", 10f, 4f, 60f, "Closest camera distance to the ground focus point, meters.");
            CameraMaxDistance = F("Camera", "MaxDistance", 95f, 30f, 300f, "Farthest camera distance, meters.");
            CameraPitch = F("Camera", "Pitch", 58f, 25f, 85f, "Camera tilt in degrees (90 = straight down).");
            CameraFov = F("Camera", "FieldOfView", 50f, 20f, 100f, "Camera field of view in degrees.");
            CameraRotateSpeed = F("Camera", "RotateSpeed", 1.0f, 0.2f, 4f, "Rotation speed multiplier for middle mouse drag and the rotate keys.");
            CameraSmoothing = F("Camera", "Smoothing", 0.12f, 0f, 0.6f, "Camera smoothing time in seconds (0 = snappy).");
            EdgeScroll = cfg.Bind("Camera", "EdgeScroll", true, "Pan the camera when the mouse touches the screen edge.");
            EdgeScrollPixels = I("Camera", "EdgeScrollPixels", 4, 1, 40, "Width of the screen edge zone in pixels.");

            KeyUp = cfg.Bind("Keys", "CameraUp", KeyCode.W, "Camera forward.");
            KeyDown = cfg.Bind("Keys", "CameraDown", KeyCode.S, "Camera back.");
            KeyLeft = cfg.Bind("Keys", "CameraLeft", KeyCode.A, "Camera left.");
            KeyRight = cfg.Bind("Keys", "CameraRight", KeyCode.D, "Camera right.");
            KeyRotateLeft = cfg.Bind("Keys", "RotateLeft", KeyCode.LeftBracket, "Rotate the camera left (middle mouse drag also rotates).");
            KeyRotateRight = cfg.Bind("Keys", "RotateRight", KeyCode.RightBracket, "Rotate the camera right.");

            KeyAttackMove = cfg.Bind("Keys", "AttackMove", KeyCode.Q, "Attack-move: press, then left click a point (A would clash with camera panning).");
            KeyStop = cfg.Bind("Keys", "Stop", KeyCode.E, "Stop: drop all orders.");
            KeyHold = cfg.Bind("Keys", "HoldPosition", KeyCode.R, "Hold position: stand and fight only what comes into range.");
            KeyFocusFire = cfg.Bind("Keys", "FocusFire", KeyCode.F, "Focus fire: attack the enemy under the cursor (or press, then click an enemy).");
            KeyRetreat = cfg.Bind("Keys", "Retreat", KeyCode.V, "Retreat to the rally point.");
            KeyPatrol = cfg.Bind("Keys", "Patrol", KeyCode.T, "Patrol: press, then left click the far point.");
            KeyRally = cfg.Bind("Keys", "RallyPoint", KeyCode.G, "Set the rally point: press, then left click.");
            KeyCastSecondary = cfg.Bind("Keys", "CastSecondary", KeyCode.Z, "Use the selected heroes' secondary skill (then click a target).");
            KeyCastUtility = cfg.Bind("Keys", "CastUtility", KeyCode.X, "Use the selected heroes' utility skill.");
            KeyCastSpecial = cfg.Bind("Keys", "CastSpecial", KeyCode.C, "Use the selected heroes' special skill.");
            KeyCastEquipment = cfg.Bind("Keys", "CastEquipment", KeyCode.B, "Use the selected heroes' equipment.");
            KeyStanceAggressive = cfg.Bind("Keys", "StanceAggressive", KeyCode.F2, "Stance: aggressive (chase anything seen).");
            KeyStanceDefensive = cfg.Bind("Keys", "StanceDefensive", KeyCode.F3, "Stance: defensive (fight near the post, then return).");
            KeyStancePassive = cfg.Bind("Keys", "StancePassive", KeyCode.F4, "Stance: passive (never start a fight).");
            KeyToggleAuto = cfg.Bind("Keys", "ToggleAutoSkills", KeyCode.Y, "Hold with Z / X / C / B to switch that skill's automatic use on or off.");
            KeyFormationCycle = cfg.Bind("Keys", "FormationCycle", KeyCode.N, "Cycle the formation used by move orders: blob / line / column.");
            KeyFocusSelected = cfg.Bind("Keys", "FocusSelected", KeyCode.Space, "Fly the camera to the selected heroes (or the whole army).");
            KeyHelp = cfg.Bind("Keys", "Help", KeyCode.F1, "Show / hide the hotkey cheat sheet.");
            KeyFastCamera = cfg.Bind("Keys", "FastCamera", KeyCode.LeftShift, "Hold to pan the camera faster.");

            MinimapEnabled = cfg.Bind("Minimap", "Enabled", true, "Show the minimap.");
            MinimapSize = F("Minimap", "Size", 260f, 140f, 520f, "Minimap size in reference pixels (1080p).");

            ListenerAtFocus = cfg.Bind("Audio", "ListenerAtFocus", true,
                "Place the audio listener on the ground below the camera focus instead of on the high camera, so battle sounds stay audible.");

            VerboseLog = cfg.Bind("Dev", "VerboseLog", false, "Write extra diagnostic lines to the BepInEx log.");
        }

        private static ConfigEntry<float> F(string section, string key, float value, float min, float max, string description)
        {
            return cfg.Bind(section, key, value, new ConfigDescription(description, new AcceptableValueRange<float>(min, max)));
        }

        private static ConfigEntry<int> I(string section, string key, int value, int min, int max, string description)
        {
            return cfg.Bind(section, key, value, new ConfigDescription(description, new AcceptableValueRange<int>(min, max)));
        }
    }
}
