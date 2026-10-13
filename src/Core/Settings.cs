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
        internal static ConfigEntry<bool> ZoomToCursor, ZoomTilt;
        internal static ConfigEntry<float> CameraPitch;
        internal static ConfigEntry<float> CameraFov;
        internal static ConfigEntry<float> CameraRotateSpeed;
        internal static ConfigEntry<float> CameraSmoothing;
        internal static ConfigEntry<bool> EdgeScroll;
        internal static ConfigEntry<int> EdgeScrollPixels;

        // ---- keys ----
        internal static ConfigEntry<KeyCode> KeyUp, KeyDown, KeyLeft, KeyRight, KeyRotateLeft, KeyRotateRight, KeyTiltUp, KeyTiltDown;
        internal static ConfigEntry<float> CameraTiltSpeed;
        internal static ConfigEntry<bool> InvertTilt;
        internal static ConfigEntry<KeyCode> KeyFocusSelected, KeyHelp, KeyFastCamera;
        internal static ConfigEntry<KeyCode> KeyAttackMove, KeyStop, KeyHold, KeyFocusFire, KeyRetreat, KeyPatrol, KeyRally;
        internal static ConfigEntry<KeyCode> KeyCastSecondary, KeyCastUtility, KeyCastSpecial, KeyCastEquipment;
        internal static ConfigEntry<KeyCode> KeyStanceAggressive, KeyStanceDefensive, KeyStancePassive;
        internal static ConfigEntry<KeyCode> KeyToggleAuto, KeyBarracks, KeyInventory;
        internal static ConfigEntry<KeyCode> KeyRevive, KeyTeleporter;
        internal static readonly ConfigEntry<KeyCode>[] KeyAbility = new ConfigEntry<KeyCode>[4];
        internal static ConfigEntry<KeyCode> KeyFormationCycle;
        internal static ConfigEntry<KeyCode> KeyCycleSelection, KeySelectAll, KeyLastAlert, KeyResetCamera;

        // ---- interface ----
        internal static ConfigEntry<float> UiScale;
        internal static ConfigEntry<bool> Alerts;

        // ---- economy / army ----
        internal static ConfigEntry<int> StartingGold;
        internal static ConfigEntry<float> GoldMultiplier;
        internal static ConfigEntry<int> HeroBasePrice;
        internal static ConfigEntry<float> HeroPriceGrowth;
        internal static ConfigEntry<float> PriceMultiplier;
        internal static ConfigEntry<int> ArmyLimit;
        internal static ConfigEntry<int> StartingHeroes;
        internal static ConfigEntry<bool> AllowDuplicates;
        internal static ConfigEntry<bool> IgnoreUnlocks;
        internal static ConfigEntry<bool> AllowHiddenSurvivors;
        internal static ConfigEntry<float> ReviveCostFraction;
        internal static ConfigEntry<bool> AutoRetreat;
        internal static ConfigEntry<float> DefeatGraceBroke;
        internal static ConfigEntry<float> DefeatGraceRevive;
        internal static ConfigEntry<bool> AutoRevive;
        internal static ConfigEntry<float> AutoReviveDelay;
        internal static ConfigEntry<float> ThreatPerExtraHero;

        // ---- loot ----
        internal static ConfigEntry<bool> ItemDropsEnabled;
        internal static ConfigEntry<float> ItemGoldFactor;
        internal static ConfigEntry<float> ItemDropMultiplier;
        internal static ConfigEntry<bool> RemovePaidInteractables;
        internal static ConfigEntry<string> KeepInteractables;
        internal static ConfigEntry<Army.ItemDistributor.SharingMode> ItemSharing;
        internal static ConfigEntry<bool> RebalanceOnRecruit;
        internal static ConfigEntry<bool> AutoOpenBarrels, AutoOpenChests;
        internal static ConfigEntry<int> ConfigVersion;

        private static string[] keepCache;
        private static string keepCacheSource;

        /// <summary>Name fragments of paid interactables that stay on the map anyway.</summary>
        internal static string[] KeptInteractables()
        {
            string raw = KeepInteractables.Value ?? string.Empty;
            if (keepCache == null || keepCacheSource != raw)
            {
                keepCacheSource = raw;
                keepCache = raw.Split(new[] { ',', ';' }, System.StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < keepCache.Length; i++) keepCache[i] = keepCache[i].Trim();
            }
            return keepCache;
        }

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
            CameraMinDistance = F("Camera", "MinDistance", 6f, 3f, 60f, "Closest camera distance to the ground focus point, meters.");
            ZoomToCursor = cfg.Bind("Camera", "ZoomToCursor", true, "The wheel zooms towards the point under the mouse (it stays under the mouse), as in Company of Heroes or Total War. Off: towards the middle of the screen.");
            ZoomTilt = cfg.Bind("Camera", "ZoomTilt", true, "Zooming in close also lowers the view towards the ground (a side view of the heroes at the closest zoom); zooming out brings your tilt back.");
            CameraMaxDistance = F("Camera", "MaxDistance", 95f, 30f, 300f, "Farthest camera distance, meters.");
            CameraPitch = F("Camera", "Pitch", 58f, 25f, 85f, "Camera tilt in degrees (90 = straight down). The starting tilt: middle mouse drag up / down and PageUp / PageDown change it during the game.");
            CameraTiltSpeed = F("Camera", "TiltSpeed", 1.0f, 0.2f, 4f, "Tilt speed multiplier for middle mouse drag up / down and the tilt keys.");
            InvertTilt = cfg.Bind("Camera", "InvertTilt", false, "Swap the direction of the tilt (middle mouse drag up looks more down).");
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
            KeyTiltUp = cfg.Bind("Keys", "TiltUp", KeyCode.PageUp, "Tilt the camera up: a flatter view towards the horizon (middle mouse drag up does the same).");
            KeyTiltDown = cfg.Bind("Keys", "TiltDown", KeyCode.PageDown, "Tilt the camera down: a view more from above (middle mouse drag down).");

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
            KeyCastEquipment = cfg.Bind("Keys", "CastEquipment", KeyCode.Y, "Use the selected heroes' equipment.");
            KeyBarracks = cfg.Bind("Keys", "Barracks", KeyCode.B, "Open / close the recruiting panel.");
            KeyInventory = cfg.Bind("Keys", "Inventory", KeyCode.I, "Open / close the item panel (the army's items, per hero; drag an item onto a hero to give it).");
            KeyAbility[0] = cfg.Bind("Keys", "AbilityStrike", KeyCode.F5, "Commander ability: orbital strike (click a point).");
            KeyAbility[1] = cfg.Bind("Keys", "AbilityMedic", KeyCode.F6, "Commander ability: field medic (heals every hero).");
            KeyAbility[2] = cfg.Bind("Keys", "AbilityWarCry", KeyCode.F7, "Commander ability: war cry (attack and move speed for every hero).");
            KeyAbility[3] = cfg.Bind("Keys", "AbilityMark", KeyCode.F8, "Commander ability: mark an enemy (everybody focuses it, it takes more damage).");
            KeyRevive = cfg.Bind("Keys", "Revive", KeyCode.U, "Revive the first fallen hero (costs gold).");
            KeyTeleporter = cfg.Bind("Keys", "Teleporter", KeyCode.H, "Teleporter: activate it, gather the army in the zone, or leave the stage - whatever is possible now.");
            KeyStanceAggressive = cfg.Bind("Keys", "StanceAggressive", KeyCode.F2, "Stance: aggressive (chase anything seen).");
            KeyStanceDefensive = cfg.Bind("Keys", "StanceDefensive", KeyCode.F3, "Stance: defensive (fight near the post, then return).");
            KeyStancePassive = cfg.Bind("Keys", "StancePassive", KeyCode.F4, "Stance: passive (never start a fight).");
            KeyToggleAuto = cfg.Bind("Keys", "ToggleAutoSkills", KeyCode.LeftAlt, "Hold with Z / X / C / Y to switch that skill's automatic use on or off.");
            KeyFormationCycle = cfg.Bind("Keys", "FormationCycle", KeyCode.N, "Cycle the formation used by move orders: blob / line / column.");
            KeyFocusSelected = cfg.Bind("Keys", "FocusSelected", KeyCode.Space, "Fly the camera to the selected heroes (or the whole army).");
            KeyHelp = cfg.Bind("Keys", "Help", KeyCode.F1, "Show / hide the hotkey cheat sheet.");
            KeyFastCamera = cfg.Bind("Keys", "FastCamera", KeyCode.LeftShift, "Hold to pan the camera faster.");
            KeyCycleSelection = cfg.Bind("Keys", "CycleSelection", KeyCode.Tab, "Select the next hero (hold Shift: the previous one).");
            KeySelectAll = cfg.Bind("Keys", "SelectAll", KeyCode.A, "Together with Ctrl: select the whole army. (Digits 1-0 are the control groups: Ctrl+digit sets, digit recalls.)");

            KeyLastAlert = cfg.Bind("Keys", "LastAlert", KeyCode.Backspace, "Fly the camera to the place of the last alert (a fallen or nearly dead hero).");
            KeyResetCamera = cfg.Bind("Keys", "ResetCamera", KeyCode.Home, "Reset the camera's turn, tilt and zoom to the start view (it stays where it looks).");
            Alerts = cfg.Bind("Interface", "Alerts", true, "Message, sound and a mark on the map when a hero falls or is nearly dead.");

            UiScale = F("Interface", "UiScale", 1.0f, 0.6f, 1.6f,
                "Size of the whole interface of the mode: 1 = normal, 1.25 = bigger text and panels, 0.8 = smaller. It comes on top of the automatic scaling to the screen resolution.");

            StartingGold = I("Economy", "StartingGold", 100, 0, 100000, "Gold the army starts the run with.");
            GoldMultiplier = F("Economy", "GoldMultiplier", 1.0f, 0.1f, 10f, "Multiplier on all gold the army receives.");
            HeroBasePrice = I("Economy", "HeroBasePrice", 250, 10, 100000, "Price of the first recruit at the start of the run (scales with the run's difficulty).");
            HeroPriceGrowth = F("Economy", "HeroPriceGrowth", 1.4f, 1f, 4f, "Every hero bought makes the next one this many times more expensive.");
            PriceMultiplier = F("Economy", "PriceMultiplier", 1.0f, 0.1f, 10f, "Multiplier on all hero prices (recruiting and reviving).");
            ArmyLimit = I("Army", "ArmyLimit", 6, 1, 20, "Largest army size (including the starting hero).");
            AllowDuplicates = cfg.Bind("Army", "AllowDuplicates", false, "Allow recruiting several heroes of the same kind.");
            IgnoreUnlocks = cfg.Bind("Army", "IgnoreUnlocks", false, "Offer survivors you have not unlocked yet.");
            AllowHiddenSurvivors = cfg.Bind("Army", "AllowHiddenSurvivors", false, "Offer hidden survivors (for example Heretic). Experimental: they are not meant to be played and may misbehave.");
            ReviveCostFraction = F("Economy", "ReviveCostFraction", 0.4f, 0f, 3f, "A fallen hero's revival costs this fraction of the price of a first recruit (it grows with time and stages, not with the army size).");
            StartingHeroes = I("Army", "StartingHeroes", 2, 1, 4, "Heroes the army starts with: the survivor chosen in the lobby plus random unlocked survivors (free; recruit prices are not raised by them).");
            AutoRetreat = cfg.Bind("Army", "AutoRetreat", true,
                "A badly hurt hero (threshold per survivor in profiles\\hero_profiles.json) runs to its friends until it recovers. Explicit move / hold / interact orders are never interrupted.");
            DefeatGraceBroke = F("Run", "DefeatGraceSecondsBroke", 6f, 0f, 120f, "When every hero is dead and the army cannot pay for a revival, the run is lost after this many seconds.");
            DefeatGraceRevive = F("Run", "DefeatGraceSecondsCanRevive", 45f, 0f, 600f, "When every hero is dead but a revival is affordable, the commander has this many seconds to pay for it.");
            AutoRevive = cfg.Bind("Run", "AutoRevive", true,
                "When every hero has fallen and the army can pay for a revival, the first fallen hero is revived automatically (after AutoReviveDelay seconds) instead of waiting for the Revive key.");
            AutoReviveDelay = F("Run", "AutoReviveDelay", 4f, 0f, 30f, "Seconds between the fall of the last hero and the automatic revival.");
            ThreatPerExtraHero = F("Run", "ThreatPerExtraHero", 0.65f, 0f, 2f,
                "How much each hero beyond the first counts as an extra player for the game's difficulty scaling (monster credits, boss count, loot). 1 = like a real co-op player.");

            ItemDropsEnabled = cfg.Bind("Loot", "ItemDropsEnabled", false, "Enemies also drop items straight into the army's pool (the old way of the mode; normally items come from chests).");
            ItemGoldFactor = F("Loot", "ItemGoldFactor", 2.5f, 0.2f, 30f, "With ItemDropsEnabled: an item is worth this many chests' worth of gold: bigger = fewer drops.");
            ItemDropMultiplier = F("Loot", "ItemDropMultiplier", 1.0f, 0f, 10f, "With ItemDropsEnabled: multiplier on the chance of every item drop.");
            RemovePaidInteractables = cfg.Bind("Loot", "RemovePaidInteractables", false, "Do not place chests, printers, shrines of chance and other interactables that cost something (the old way of the mode).");
            KeepInteractables = cfg.Bind("Loot", "KeepInteractables", "", "With RemovePaidInteractables: comma separated name fragments of paid interactables that should stay (for example: ShrineBoss,Duplicator).");
            AutoOpenBarrels = cfg.Bind("Loot", "AutoOpenBarrels", true, "Heroes with nothing to do open barrels (free gold) near their post by themselves.");
            AutoOpenChests = cfg.Bind("Loot", "AutoOpenChests", true, "Heroes with nothing to do, with no enemy near, open a chest near their post by themselves when the purse can pay for it. Only chests: shops, shrines, printers and drones wait for an order.");
            ConfigVersion = cfg.Bind("Dev", "ConfigVersion", 0, "Version of this file's defaults (the mod updates old values once; do not change).");
            Migrate();

            ItemSharing = cfg.Bind("Loot", "ItemSharing", Army.ItemDistributor.SharingMode.Split,
                "Split: every item unit belongs to one hero (like a normal co-op run). Mirror: every hero gets a copy of every item (much stronger).");
            RebalanceOnRecruit = cfg.Bind("Loot", "RebalanceOnRecruit", true, "A newly recruited hero takes a fair share of items from the heroes who have the most.");

            MinimapEnabled = cfg.Bind("Minimap", "Enabled", true, "Show the minimap.");
            MinimapSize = F("Minimap", "Size", 260f, 140f, 520f, "Minimap size in reference pixels (1080p).");

            ListenerAtFocus = cfg.Bind("Audio", "ListenerAtFocus", true,
                "Place the audio listener on the ground below the camera focus instead of on the high camera, so battle sounds stay audible.");

            VerboseLog = cfg.Bind("Dev", "VerboseLog", false, "Write extra diagnostic lines to the BepInEx log.");
        }

        /// <summary>
        /// Values written by an older version that the new design overrides. BepInEx keeps a value once it is in the file,
        /// so a changed default never reaches an existing player: these are set once, and the file remembers it.
        /// </summary>
        private static void Migrate()
        {
            const int Current = 3;
            if (ConfigVersion.Value >= Current) return;

            if (ConfigVersion.Value < 2)
            {
                // 0.11: items come from chests on the map again (paid from the army's purse), not from kills
                ItemDropsEnabled.Value = false;
                RemovePaidInteractables.Value = false;
                Plugin.Log.LogInfo("Config: loot switched to chests on the map (Loot/ItemDropsEnabled = false, Loot/RemovePaidInteractables = false).");
            }
            if (ConfigVersion.Value < 3 && Mathf.Approximately(CameraMinDistance.Value, 10f))
            {
                // 0.11: the camera may come closer (with ZoomTilt it then looks along the ground)
                CameraMinDistance.Value = 6f;
                Plugin.Log.LogInfo("Config: Camera/MinDistance 10 -> 6 (closer zoom).");
            }

            ConfigVersion.Value = Current;
            cfg.Save();
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
