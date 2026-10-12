using System;
using System.Collections.Generic;
using HarmonyLib;

namespace HighCommand
{
    /// <summary>
    /// Localisation. Tokens are registered in code (English + Russian) and served through a tiny Harmony patch on
    /// RoR2.Language, so the mod needs no R2API. Russian is used for the "ru" language, English for all others.
    /// </summary>
    internal static class Loc
    {
        public const string Prefix = "HIGHCOMMAND_";

        private static readonly Dictionary<string, string> En = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> Ru = new Dictionary<string, string>();

        /// <summary>Registers a token. Without Russian text the English one is used for both.</summary>
        internal static string Add(string token, string en, string ru = null)
        {
            En[token] = en;
            Ru[token] = ru ?? en;
            return token;
        }

        internal static bool IsRussianName(string name)
        {
            return !string.IsNullOrEmpty(name)
                && (name.StartsWith("ru", StringComparison.OrdinalIgnoreCase)
                    || name.IndexOf("russ", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>True when the game itself is set to Russian.</summary>
        internal static bool IsRussian()
        {
            string current = RoR2.Language.currentLanguageName;
            if (!string.IsNullOrEmpty(current)) return IsRussianName(current);
            return UnityEngine.Application.systemLanguage == UnityEngine.SystemLanguage.Russian;
        }

        /// <summary>Text for a token in the current language; the token itself if unknown.</summary>
        internal static string Get(string token)
        {
            var table = IsRussian() ? Ru : En;
            return table.TryGetValue(token, out string text) ? text : token;
        }

        internal static string Format(string token, params object[] args)
        {
            try { return string.Format(Get(token), args); }
            catch (FormatException) { return Get(token); }
        }

        internal static bool TryGet(string languageName, string token, out string text)
        {
            string current = RoR2.Language.currentLanguageName;
            string name = !string.IsNullOrEmpty(current) ? current : languageName;
            var table = IsRussianName(name) ? Ru : En;
            return table.TryGetValue(token, out text);
        }
    }

    [HarmonyPatch(typeof(RoR2.Language))]
    internal static class LanguagePatches
    {
        // These patches run for every text the game looks up, so the check must be very cheap.
        private static bool IsOurToken(string token)
        {
            return token != null && token.Length > 12 && token[0] == 'H'
                && token.StartsWith(Loc.Prefix, StringComparison.Ordinal);
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(RoR2.Language.GetLocalizedStringByToken))]
        private static bool GetString(RoR2.Language __instance, string token, ref string __result)
        {
            if (IsOurToken(token) && Loc.TryGet(__instance.name, token, out string text))
            {
                __result = text;
                return false;
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(RoR2.Language.TokenIsRegistered))]
        private static bool IsRegistered(string token, ref bool __result)
        {
            if (IsOurToken(token))
            {
                __result = true;
                return false;
            }
            return true;
        }
    }
}
