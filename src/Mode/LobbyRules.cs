using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RoR2;
using UnityEngine;

namespace HighCommand.Mode
{
    /// <summary>
    /// The mode's own rules in the lobby (a "High Command" category next to Difficulty / Artifacts / Misc): starting gold,
    /// hero price, army size, how strongly the army counts as extra players, item sharing and the loot rate. The first
    /// choice of every rule is "Config": the value from the .cfg file / Mod Options is used.
    ///
    /// The rule catalog builds itself inside a private initializer, so a Harmony transpiler inserts one call right after
    /// the last stock rule is added (before the indices are assigned). Everything is optional: if the code of the game
    /// differs, the mode simply has no lobby rules and the config values apply.
    /// </summary>
    internal static class LobbyRules
    {
        internal const string GoldRule = "HighCommand.StartingGold";
        internal const string PriceRule = "HighCommand.HeroPrice";
        internal const string ArmyRule = "HighCommand.ArmyLimit";
        internal const string ThreatRule = "HighCommand.Threat";
        internal const string SharingRule = "HighCommand.ItemSharing";
        internal const string LootRule = "HighCommand.Loot";

        private static bool injected;

        // ---- patch ---------------------------------------------------------------------------------------

        [HarmonyPatch(typeof(RuleCatalog), "Init")]
        internal static class InitPatch
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var list = new List<CodeInstruction>(instructions);
                MethodInfo addRule = AccessTools.Method(typeof(RuleCatalog), "AddRule");
                MethodInfo inject = AccessTools.Method(typeof(LobbyRules), nameof(Inject));
                if (addRule == null || inject == null) return list;

                // the last AddRule call in Init is the stock "Misc.RemoteOp" rule: our category goes right after it
                int last = -1;
                for (int i = 0; i < list.Count; i++)
                    if (list[i].Calls(addRule)) last = i;
                if (last < 0) return list;

                list.Insert(last + 1, new CodeInstruction(OpCodes.Call, inject));
                return list;
            }
        }

        // ---- injection (runs inside RuleCatalog.Init) ---------------------------------------------------------

        internal static void Inject()
        {
            if (injected) return;
            try
            {
                MethodInfo addCategory = null;
                foreach (MethodInfo m in typeof(RuleCatalog).GetMethods(BindingFlags.Static | BindingFlags.NonPublic))
                {
                    if (m.Name == "AddCategory" && m.GetParameters().Length == 8) { addCategory = m; break; }
                }
                MethodInfo addRule = AccessTools.Method(typeof(RuleCatalog), "AddRule");
                if (addCategory == null || addRule == null) throw new MissingMethodException("RuleCatalog.AddCategory / AddRule");

                Func<bool> hidden = HiddenOutsideMode;
                addCategory.Invoke(null, new object[]
                {
                    "HIGHCOMMAND_RULES_HEADER", "HIGHCOMMAND_RULES_SUBTITLE", new Color(1f, 0.64f, 0.18f, 1f),
                    "", "HIGHCOMMAND_RULES_EDIT", hidden, RuleCatalog.RuleCategoryType.VoteResultGrid, null
                });

                Add(addRule, Icon(0), GoldRule, "HIGHCOMMAND_RULE_GOLD", "{0}",
                    "HIGHCOMMAND_RULE_GOLD_DESC", new object[] { 0, 150, 300, 600, 1000 });
                Add(addRule, Icon(1), PriceRule, "HIGHCOMMAND_RULE_PRICE", "x{0}",
                    "HIGHCOMMAND_RULE_PRICE_DESC", new object[] { 0.5f, 0.75f, 1.5f, 2f });
                Add(addRule, Icon(2), ArmyRule, "HIGHCOMMAND_RULE_ARMY", "{0}",
                    "HIGHCOMMAND_RULE_ARMY_DESC", new object[] { 3, 4, 6, 8, 10, 12, 16 });
                Add(addRule, Icon(3), ThreatRule, "HIGHCOMMAND_RULE_THREAT", "{0}",
                    "HIGHCOMMAND_RULE_THREAT_DESC", new object[] { 0.35f, 0.65f, 1f, 1.25f });
                Add(addRule, Icon(4), LootRule, "HIGHCOMMAND_RULE_LOOT", "x{0}",
                    "HIGHCOMMAND_RULE_LOOT_DESC", new object[] { 0.5f, 1.5f, 2f });

                // item sharing: two named choices
                Sprite sharingIcon = Icon(5);
                var sharing = new RuleDef(SharingRule, "HIGHCOMMAND_RULE_SHARING");
                AddChoice(sharing, sharingIcon, "Config", null, "HIGHCOMMAND_RULE_CONFIG_NAME", "HIGHCOMMAND_RULE_SHARING_DESC");
                AddChoice(sharing, sharingIcon, "Split", false, "HIGHCOMMAND_RULE_SHARING_SPLIT", "HIGHCOMMAND_RULE_SHARING_SPLIT_DESC");
                AddChoice(sharing, sharingIcon, "Mirror", true, "HIGHCOMMAND_RULE_SHARING_MIRROR", "HIGHCOMMAND_RULE_SHARING_MIRROR_DESC");
                sharing.defaultChoiceIndex = 0;
                addRule.Invoke(null, new object[] { sharing });

                injected = true;
                Plugin.Log.LogInfo("Lobby rules added to the rule catalog.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Lobby rules could not be added (the config values apply): " + e.Message);
            }
        }

        // ---- icons (drawn in code, no game assets) -----------------------------------------------------------

        private static Sprite Icon(int kind)
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[size * size];
            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    float x = (px + 0.5f) / size * 2f - 1f;
                    float y = (py + 0.5f) / size * 2f - 1f;
                    Color c = IconPixel(kind, x, y);
                    pixels[py * size + px] = c;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static float Soft(float d, float edge) => Mathf.Clamp01((edge - d) * 24f);

        private static Color IconPixel(int kind, float x, float y)
        {
            Color none = new Color(0f, 0f, 0f, 0f);
            float r = Mathf.Sqrt(x * x + y * y);
            switch (kind)
            {
                case 0: // a gold coin with a bar
                {
                    float disc = Soft(r, 0.82f);
                    if (disc <= 0f) return none;
                    Color c = r > 0.62f ? new Color(0.8f, 0.55f, 0.1f) : new Color(1f, 0.84f, 0.25f);
                    if (Mathf.Abs(x) < 0.1f && Mathf.Abs(y) < 0.42f) c = new Color(0.55f, 0.35f, 0.05f);
                    c.a = disc;
                    return c;
                }
                case 1: // a price tag
                {
                    bool body = Mathf.Abs(x) < 0.75f && Mathf.Abs(y) < 0.45f;
                    float hole = Mathf.Sqrt((x + 0.5f) * (x + 0.5f) + y * y);
                    if (!body || hole < 0.14f) return none;
                    return new Color(0.45f, 0.85f, 0.55f, 1f);
                }
                case 2: // three heroes
                {
                    Vector2[] p = { new Vector2(0f, 0.45f), new Vector2(-0.5f, -0.35f), new Vector2(0.5f, -0.35f) };
                    foreach (Vector2 c in p)
                    {
                        float d = Mathf.Sqrt((x - c.x) * (x - c.x) + (y - c.y) * (y - c.y));
                        if (d < 0.32f) return new Color(0.4f, 0.75f, 1f, Soft(d, 0.32f));
                    }
                    return none;
                }
                case 3: // a warning triangle
                {
                    float top = 0.8f;
                    float half = (top - y) * 0.55f;
                    bool inside = y > -0.7f && y < top && Mathf.Abs(x) < half;
                    if (!inside) return none;
                    bool mark = (Mathf.Abs(x) < 0.07f && y > -0.25f && y < 0.35f) || (Mathf.Abs(x) < 0.09f && y > -0.5f && y < -0.36f);
                    return mark ? new Color(1f, 1f, 1f, 1f) : new Color(0.95f, 0.3f, 0.25f, 1f);
                }
                case 4: // a loot box
                {
                    bool box = Mathf.Abs(x) < 0.7f && y > -0.6f && y < 0.5f;
                    if (!box) return none;
                    bool lid = y > 0.2f;
                    bool band = Mathf.Abs(x) < 0.12f;
                    if (band) return new Color(1f, 0.85f, 0.3f, 1f);
                    return lid ? new Color(0.75f, 0.5f, 0.95f, 1f) : new Color(0.55f, 0.35f, 0.8f, 1f);
                }
                default: // two overlapping circles: sharing
                {
                    float d1 = Mathf.Sqrt((x + 0.32f) * (x + 0.32f) + y * y);
                    float d2 = Mathf.Sqrt((x - 0.32f) * (x - 0.32f) + y * y);
                    if (d1 < 0.5f && d2 < 0.5f) return new Color(1f, 1f, 0.6f, 1f);
                    if (d1 < 0.5f) return new Color(1f, 0.64f, 0.18f, Soft(d1, 0.5f));
                    if (d2 < 0.5f) return new Color(0.3f, 0.85f, 0.8f, Soft(d2, 0.5f));
                    return none;
                }
            }
        }

        private static bool HiddenOutsideMode()
        {
            try
            {
                PreGameController pre = PreGameController.instance;
                if (!pre) return true;
                Run prefab = GameModeCatalog.GetGameModePrefabComponent(pre.gameModeIndex);
                return !HighCommandRun.IsModeRun(prefab);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void Add(MethodInfo addRule, Sprite icon, string name, string displayToken, string nameFormat,
            string bodyToken, object[] values)
        {
            var rule = new RuleDef(name, displayToken);
            AddChoice(rule, icon, "Config", null, "HIGHCOMMAND_RULE_CONFIG_NAME", bodyToken);
            foreach (object value in values)
            {
                string text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
                string token = "HIGHCOMMAND_RULECHOICE_" + name.Replace('.', '_').ToUpperInvariant() + "_" + text.Replace('.', '_').ToUpperInvariant();
                Loc.Add(token, string.Format(nameFormat, text), string.Format(nameFormat, text));
                AddChoice(rule, icon, text, value, token, bodyToken);
            }
            rule.defaultChoiceIndex = 0;
            addRule.Invoke(null, new object[] { rule });
        }

        private static void AddChoice(RuleDef rule, Sprite icon, string localName, object data, string nameToken, string bodyToken)
        {
            RuleChoiceDef choice = rule.AddChoice(localName, data, excludeByDefault: false);
            choice.sprite = icon;
            choice.tooltipNameToken = nameToken;
            choice.tooltipBodyToken = bodyToken;
            choice.tooltipNameColor = new Color(1f, 0.64f, 0.18f, 1f);
            choice.onlyShowInGameBrowserIfNonDefault = true;
        }

        // ---- reading the choices ---------------------------------------------------------------------------

        private static object Extra(string ruleName)
        {
            try
            {
                Run run = Run.instance;
                if (!run || run.ruleBook == null) return null;
                RuleDef def = RuleCatalog.FindRuleDef(ruleName);
                if (def == null) return null;
                RuleChoiceDef choice = run.ruleBook.GetRuleChoice(def);
                return choice?.extraData;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The lobby's value for a numeric rule, or the config value when "Config" is chosen.</summary>
        internal static float Number(string ruleName, float configValue)
        {
            object extra = Extra(ruleName);
            if (extra is float f) return f;
            if (extra is int i) return i;
            return configValue;
        }

        internal static int StartingGold => Mathf.RoundToInt(Number(GoldRule, Settings.StartingGold.Value));

        internal static float PriceMultiplier => Number(PriceRule, Settings.PriceMultiplier.Value);

        internal static int ArmyLimit => Mathf.Max(1, Mathf.RoundToInt(Number(ArmyRule, Settings.ArmyLimit.Value)));

        internal static float ThreatPerExtraHero => Number(ThreatRule, Settings.ThreatPerExtraHero.Value);

        internal static float LootMultiplier => Number(LootRule, Settings.ItemDropMultiplier.Value);

        internal static bool ItemMirror
        {
            get
            {
                object extra = Extra(SharingRule);
                if (extra is bool b) return b;
                return Settings.ItemSharing.Value == HighCommand.Army.ItemDistributor.SharingMode.Mirror;
            }
        }
    }
}
