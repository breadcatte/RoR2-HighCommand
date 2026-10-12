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
        internal static ConfigEntry<KeyCode> KeyToggleAuto, KeyBarracks, KeyInventory;
        internal static ConfigEntry<KeyCode> KeyRevive, KeyTeleporter;
        internal static readonly ConfigEntry<KeyCode>[] KeyAbility = new ConfigEntry<KeyCode>[4];
        internal static ConfigEntry<KeyCode> KeyFormationCycle;

        // ---- economy / army ----
        internal static ConfigEntry<int> StartingGold;
        internal static ConfigEntry<float> GoldMultiplier;
        internal static ConfigEntry<int> HeroBasePrice;
        internal static ConfigEntry<float> HeroPriceGrowth;
        internal static ConfigEntry<float> PriceMultiplier;
        internal static ConfigEntry<int> ArmyLimit;
        internal static ConfigEntry<bool> AllowDuplicates;
        internal static ConfigEntry<bool> IgnoreUnlocks;
        internal static ConfigEntry<bool> AllowHiddenSurvivors;
        internal static ConfigEntry<float> ReviveCostFraction;
        internal static ConfigEntry<bool> AutoRetreat;
        internal static ConfigEntry<float> DefeatGraceBroke;
        internal static ConfigEntry<float> DefeatGraceRevive;
        internal static ConfigEntry<float> ThreatPerExtraHero;

        // ---- loot ----
        internal static ConfigEntry<bool> ItemDropsEnabled;
        internal static ConfigEntry<float> ItemGoldFactor;
        internal static ConfigEntry<float> ItemDropMultiplier;
        internal static ConfigEntry<bool> RemovePaidInteractables;
        internal static ConfigEntry<string> KeepInteractables;
        internal static ConfigEntry<Army.ItemDistributor.SharingMode> ItemSharing;
        internal static ConfigEntry<bool> RebalanceOnRecruit;

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

            StartingGold = I("Economy", "StartingGold", 0, 0, 100000, "Gold the army starts the run with.");
            GoldMultiplier = F("Economy", "GoldMultiplier", 1.0f, 0.1f, 10f, "Multiplier on all gold the army receives.");
            HeroBasePrice = I("Economy", "HeroBasePrice", 300, 10, 100000, "Price of the first recruit at the start of the run (scales with the run's difficulty).");
            HeroPriceGrowth = F("Economy", "HeroPriceGrowth", 1.4f, 1f, 4f, "Every hero bought makes the next one this many times more expensive.");
            PriceMultiplier = F("Economy", "PriceMultiplier", 1.0f, 0.1f, 10f, "Multiplier on all hero prices (recruiting and reviving).");
            ArmyLimit = I("Army", "ArmyLimit", 6, 1, 20, "Largest army size (including the starting hero).");
            AllowDuplicates = cfg.Bind("Army", "AllowDuplicates", false, "Allow recruiting several heroes of the same kind.");
            IgnoreUnlocks = cfg.Bind("Army", "IgnoreUnlocks", false, "Offer survivors you have not unlocked yet.");
            AllowHiddenSurvivors = cfg.Bind("Army", "AllowHiddenSurvivors", false, "Offer hidden survivors (for example Heretic).");
            ReviveCostFraction = F("Economy", "ReviveCostFraction", 0.4f, 0f, 3f, "A fallen hero's revival costs this fraction of the price of the next recruit.");
            AutoRetreat = cfg.Bind("Army", "AutoRetreat", true,
                "A badly hurt hero (threshold per survivor in profiles\\hero_profiles.json) runs to its friends until it recovers. Explicit move / hold / interact orders are never interrupted.");
            DefeatGraceBroke = F("Run", "DefeatGraceSecondsBroke", 6f, 0f, 120f, "When every hero is dead and the army cannot pay for a revival, the run is lost after this many seconds.");
            DefeatGraceRevive = F("Run", "DefeatGraceSecondsCanRevive", 45f, 0f, 600f, "When every hero is dead but a revival is affordable, the commander has this many seconds to pay for it.");
            ThreatPerExtraHero = F("Run", "ThreatPerExtraHero", 0.65f, 0f, 2f,
                "How much each hero beyond the first counts as an extra player for the game's difficulty scaling (monster credits, boss count, loot). 1 = like a real co-op player.");

            ItemDropsEnabled = cfg.Bind("Loot", "ItemDropsEnabled", true, "Enemies drop items straight into the army's pool.");
            ItemGoldFactor = F("Loot", "ItemGoldFactor", 2.5f, 0.2f, 30f, "An item is worth this many chests' worth of gold: bigger = fewer drops. At 2.5 a normal run gives about as many items as vanilla.");
            ItemDropMultiplier = F("Loot", "ItemDropMultiplier", 1.0f, 0f, 10f, "Multiplier on the chance of every item drop.");
            RemovePaidInteractables = cfg.Bind("Loot", "RemovePaidInteractables", true, "Do not place chests, printers, shrines of chance and other interactables that cost something.");
            KeepInteractables = cfg.Bind("Loot", "KeepInteractables", "", "Comma separated name fragments of paid interactables that should stay (for example: ShrineBoss,Duplicator).");

            ItemSharing = cfg.Bind("Loot", "ItemSharing", Army.ItemDistributor.SharingMode.Split,
                "Split: every item unit belongs to one hero (like a normal co-op run). Mirror: every hero gets a copy of every item (much stronger).");
            RebalanceOnRecruit = cfg.Bind("Loot", "RebalanceOnRecruit", true, "A newly recruited hero takes a fair share of items from the heroes who have the most.");

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
