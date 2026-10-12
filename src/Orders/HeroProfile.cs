using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HighCommand.Orders
{
    /// <summary>
    /// How one kind of hero behaves on its own, as plain data (see <see cref="ProfileStore"/>). The skills themselves
    /// are chosen by the game's own AI rules for the survivor; a profile adds ranges, a flee threshold and a usage rule
    /// per skill slot.
    ///
    /// Rules: "auto" (the survivor's AI decides), "never", "escape" (only when hurt or fleeing), "engage" (only against
    /// a target that is far away: gap closers), "burst" (only when several enemies are close or the target is a boss).
    /// </summary>
    public sealed class HeroProfile
    {
        public string survivor = "*";        // SurvivorDef.cachedName, "*" = every survivor without an own profile
        public string role = "";             // informative

        public float acquireAggressive = 55f;
        public float acquireDefensive = 32f;
        public float leashAggressive = 95f;
        public float leashDefensive = 42f;

        public float retreatHealth = 0.28f;  // below this fraction (and enemies near) the hero runs to its friends
        public float recoverHealth = 0.55f;  // back to the fight above this

        public string primary = "auto";
        public string secondary = "auto";
        public string utility = "auto";
        public string special = "auto";
        public string equipment = "auto";

        public int burstEnemies = 3;         // "burst": at least this many enemies within 20 m
        public float engageDistance = 14f;   // "engage": the target is at least this far away

        /// <summary>
        /// Skill slot (0..3) that carries the hero out of a lost fight (a dash, blink, roll, smoke bomb), pressed while it
        /// flees, aimed and moving away from the enemies; -1 = none. Only movement skills belong here: a wall or a mine
        /// thrown away from the enemies would not help.
        /// </summary>
        public int fleeSlot = -1;

        /// <summary>
        /// Ranged heroes: the distance (m) they keep from the enemy they fight. The game's AI rules for survivors were
        /// written for Umbra-style hunters that always close in; a 90 HP archer walking up to a Lemurian dies in seconds.
        /// Closer than 65 % of this: back off; closer than this: circle instead of advancing. 0 = melee, no limit.
        /// </summary>
        public float standoff;

        internal HeroProfile Copy() => (HeroProfile)MemberwiseClone();

        internal string RuleFor(int slot)
        {
            switch (slot)
            {
                case 0: return primary;
                case 1: return secondary;
                case 2: return utility;
                case 3: return special;
                default: return equipment;
            }
        }

        internal static HeroProfile FromJson(Dictionary<string, object> o)
        {
            string name = MiniJson.String(o, "survivor", "*");
            // a field missing from the file (written by an older version) takes the built-in value for that survivor
            HeroProfile d = ProfileStore.BuiltIn(name) ?? new HeroProfile();
            var p = new HeroProfile
            {
                survivor = name,
                role = MiniJson.String(o, "role", d.role),
                acquireAggressive = MiniJson.Float(o, "acquireAggressive", d.acquireAggressive),
                acquireDefensive = MiniJson.Float(o, "acquireDefensive", d.acquireDefensive),
                leashAggressive = MiniJson.Float(o, "leashAggressive", d.leashAggressive),
                leashDefensive = MiniJson.Float(o, "leashDefensive", d.leashDefensive),
                retreatHealth = MiniJson.Float(o, "retreatHealth", d.retreatHealth),
                recoverHealth = MiniJson.Float(o, "recoverHealth", d.recoverHealth),
                primary = MiniJson.String(o, "primary", d.primary),
                secondary = MiniJson.String(o, "secondary", d.secondary),
                utility = MiniJson.String(o, "utility", d.utility),
                special = MiniJson.String(o, "special", d.special),
                equipment = MiniJson.String(o, "equipment", d.equipment),
                burstEnemies = MiniJson.Int(o, "burstEnemies", d.burstEnemies),
                engageDistance = MiniJson.Float(o, "engageDistance", d.engageDistance),
                fleeSlot = MiniJson.Int(o, "fleeSlot", d.fleeSlot),
                standoff = MiniJson.Float(o, "standoff", d.standoff)
            };
            return p;
        }

        internal string ToJson(string indent)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(indent).Append("{\n");
            void S(string k, string v, bool last = false) =>
                sb.Append(indent).Append("  ").Append(MiniJson.Quote(k)).Append(": ").Append(MiniJson.Quote(v)).Append(last ? "\n" : ",\n");
            void N(string k, float v) =>
                sb.Append(indent).Append("  ").Append(MiniJson.Quote(k)).Append(": ").Append(MiniJson.Number(v)).Append(",\n");
            S("survivor", survivor);
            S("role", role);
            N("acquireAggressive", acquireAggressive);
            N("acquireDefensive", acquireDefensive);
            N("leashAggressive", leashAggressive);
            N("leashDefensive", leashDefensive);
            N("retreatHealth", retreatHealth);
            N("recoverHealth", recoverHealth);
            N("burstEnemies", burstEnemies);
            N("engageDistance", engageDistance);
            N("fleeSlot", fleeSlot);
            N("standoff", standoff);
            S("primary", primary);
            S("secondary", secondary);
            S("utility", utility);
            S("special", special);
            S("equipment", equipment, last: true);
            sb.Append(indent).Append("}");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Loads <c>profiles\hero_profiles.json</c> next to the plugin (written with sensible defaults the first time).
    /// A broken file never breaks the game: the built-in defaults are used and the problem is logged.
    /// </summary>
    internal static class ProfileStore
    {
        private static readonly Dictionary<string, HeroProfile> ByName = new Dictionary<string, HeroProfile>(StringComparer.OrdinalIgnoreCase);
        private static HeroProfile fallback = new HeroProfile();
        private static bool loaded;

        internal static string FilePath
        {
            get
            {
                string dir = Path.GetDirectoryName(typeof(ProfileStore).Assembly.Location) ?? ".";
                return Path.Combine(Path.Combine(dir, "profiles"), "hero_profiles.json");
            }
        }

        internal static HeroProfile For(string survivorName)
        {
            if (!loaded) Load();
            if (!string.IsNullOrEmpty(survivorName) && ByName.TryGetValue(survivorName, out HeroProfile p)) return p;
            return fallback;
        }

        internal static void Reload()
        {
            loaded = false;
            Load();
        }

        private static void Load()
        {
            loaded = true;
            ByName.Clear();
            fallback = new HeroProfile();

            List<HeroProfile> profiles = null;
            string path = FilePath;
            try
            {
                if (File.Exists(path))
                {
                    var root = MiniJson.Parse(File.ReadAllText(path)) as Dictionary<string, object>;
                    var list = root != null && root.TryGetValue("profiles", out object raw) ? raw as List<object> : null;
                    if (list == null || list.Count == 0) throw new InvalidDataException("no \"profiles\" array in the file");

                    profiles = new List<HeroProfile>();
                    foreach (object item in list)
                    {
                        if (item is Dictionary<string, object> dict) profiles.Add(HeroProfile.FromJson(dict));
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Hero profiles: {path} is unreadable ({e.Message}); built-in defaults are used.");
                profiles = null;
            }

            if (profiles == null)
            {
                profiles = Defaults();
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, Serialize(profiles));
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Hero profiles: could not write the default file: " + e.Message);
                }
            }

            foreach (HeroProfile p in profiles)
            {
                if (p == null || string.IsNullOrEmpty(p.survivor)) continue;
                Sanitize(p);
                if (p.survivor == "*") fallback = p;
                else ByName[p.survivor] = p;
            }
            Plugin.Log.LogInfo($"Hero profiles: {ByName.Count} survivor profile(s) + fallback loaded.");
        }

        private static string Serialize(List<HeroProfile> profiles)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"version\": 1,\n");
            sb.Append("  // rules per skill slot: auto | never | escape | engage | burst (see the mod's STATUS.md)\n");
            sb.Append("  \"profiles\": [\n");
            for (int i = 0; i < profiles.Count; i++)
            {
                sb.Append(profiles[i].ToJson("    "));
                sb.Append(i < profiles.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");
            return sb.ToString();
        }

        private static void Sanitize(HeroProfile p)
        {
            p.acquireAggressive = Mathf.Clamp(p.acquireAggressive, 10f, 150f);
            p.acquireDefensive = Mathf.Clamp(p.acquireDefensive, 10f, 150f);
            p.leashAggressive = Mathf.Clamp(p.leashAggressive, 10f, 300f);
            p.leashDefensive = Mathf.Clamp(p.leashDefensive, 10f, 300f);
            p.retreatHealth = Mathf.Clamp(p.retreatHealth, 0f, 0.9f);
            p.recoverHealth = Mathf.Clamp(Mathf.Max(p.recoverHealth, p.retreatHealth + 0.05f), 0.05f, 1f);
            p.burstEnemies = Mathf.Clamp(p.burstEnemies, 1, 20);
            p.engageDistance = Mathf.Clamp(p.engageDistance, 2f, 80f);
            if (p.fleeSlot < 0 || p.fleeSlot > 3) p.fleeSlot = -1;
            p.standoff = Mathf.Clamp(p.standoff, 0f, 60f);
        }

        /// <summary>
        /// Survivors whose utility skill is a real "get away" move in the default loadout (it follows the move or aim
        /// direction): Commando's roll, Huntress' blink, Bandit's smoke bomb, Mercenary's dash, the Void Fiend's trespass.
        /// </summary>
        internal static int BuiltInFleeSlot(string survivor)
        {
            switch (survivor)
            {
                case "Commando":
                case "Huntress":
                case "Bandit2":
                case "Merc":
                case "VoidSurvivor":
                    return 2;
                default:
                    return -1;
            }
        }

        // ---- built-in defaults -------------------------------------------------------------------------------

        private static Dictionary<string, HeroProfile> builtIn;

        /// <summary>The built-in profile of a survivor (a fresh copy), or null when there is none.</summary>
        internal static HeroProfile BuiltIn(string survivor)
        {
            if (builtIn == null)
            {
                builtIn = new Dictionary<string, HeroProfile>(StringComparer.OrdinalIgnoreCase);
                foreach (HeroProfile p in Defaults()) builtIn[p.survivor] = p;
            }
            return survivor != null && builtIn.TryGetValue(survivor, out HeroProfile found) ? found.Copy() : null;
        }

        private static HeroProfile P(string survivor, string role, float retreat, float standoff = 0f, string utility = "auto", string special = "auto",
            string secondary = "auto", float acquire = 55f, float leash = 95f)
        {
            return new HeroProfile
            {
                survivor = survivor,
                fleeSlot = BuiltInFleeSlot(survivor),
                standoff = standoff,
                role = role,
                retreatHealth = retreat,
                recoverHealth = Mathf.Min(0.9f, retreat + 0.3f),
                utility = utility,
                special = special,
                secondary = secondary,
                acquireAggressive = acquire,
                leashAggressive = leash
            };
        }

        private static List<HeroProfile> Defaults()
        {
            var l = new List<HeroProfile>();
            l.Add(new HeroProfile { survivor = "*", role = "Survivor" });

            // ranged damage: keep their distance, flee early
            l.Add(P("Commando", "Ranged", 0.3f, standoff: 18f, utility: "escape", special: "burst"));
            l.Add(P("Huntress", "Ranged", 0.35f, standoff: 24f, utility: "escape", special: "burst", acquire: 62f));
            l.Add(P("Chef", "Ranged", 0.3f, standoff: 12f, utility: "escape", special: "burst"));
            l.Add(P("Railgunner", "Sniper", 0.35f, standoff: 30f, utility: "escape", special: "burst", acquire: 70f, leash: 110f));
            l.Add(P("Mage", "Caster", 0.35f, standoff: 18f, utility: "escape", special: "burst"));
            l.Add(P("VoidSurvivor", "Caster", 0.3f, standoff: 14f, utility: "escape", special: "burst"));

            // melee and tanks: stay in the fight longer, use gap closers to engage
            l.Add(P("Merc", "Assassin", 0.25f, utility: "engage", special: "burst", acquire: 48f));
            l.Add(P("Bandit2", "Assassin", 0.3f, standoff: 10f, utility: "escape", special: "burst", acquire: 52f));
            l.Add(P("Loader", "Melee", 0.22f, utility: "engage", special: "burst", acquire: 46f));
            l.Add(P("Croco", "Melee", 0.18f, utility: "auto", special: "burst", acquire: 42f));
            l.Add(P("Toolbot", "Tank", 0.2f, utility: "auto", special: "auto", acquire: 48f));
            l.Add(P("FalseSon", "Tank", 0.2f, utility: "engage", special: "burst", acquire: 46f));
            l.Add(P("Enforcer2", "Tank", 0.2f, utility: "auto", special: "auto", acquire: 46f));
            l.Add(P("Drifter", "Melee", 0.22f, utility: "engage", special: "burst", acquire: 46f));
            l.Add(P("Acolyte", "Melee", 0.22f, utility: "engage", special: "burst", acquire: 46f));

            // support and summoners: far behind the line, early retreat
            l.Add(P("Engi", "Summoner", 0.35f, standoff: 18f, utility: "auto", special: "auto"));
            l.Add(P("DroneTech", "Summoner", 0.35f, standoff: 18f, utility: "auto", special: "auto"));
            l.Add(P("Treebot", "Support", 0.3f, standoff: 14f, utility: "escape", special: "auto"));
            l.Add(P("Seeker", "Support", 0.3f, standoff: 12f, utility: "escape", special: "auto"));
            l.Add(P("Captain", "Support", 0.3f, standoff: 18f, utility: "auto", special: "burst"));
            return l;
        }
    }
}
