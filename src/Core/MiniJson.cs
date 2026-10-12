using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HighCommand
{
    /// <summary>
    /// A very small JSON reader/writer for the mod's own data files. (Unity's JsonUtility cannot be used: the game's
    /// serialization does not know the types of a plugin assembly.) Objects are Dictionary&lt;string, object&gt;,
    /// arrays List&lt;object&gt;, numbers double, plus string, bool and null.
    /// </summary>
    internal static class MiniJson
    {
        // ---- parsing -----------------------------------------------------------------------------------------

        internal static object Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            int i = 0;
            object value = ReadValue(json, ref i);
            SkipWhite(json, ref i);
            if (i != json.Length) throw new FormatException("unexpected text after the JSON value at " + i);
            return value;
        }

        private static void SkipWhite(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { i++; continue; }
                // comments are welcome in a hand-edited file
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                {
                    while (i < s.Length && s[i] != '\n') i++;
                    continue;
                }
                break;
            }
        }

        private static object ReadValue(string s, ref int i)
        {
            SkipWhite(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end of JSON");
            char c = s[i];
            switch (c)
            {
                case '{': return ReadObject(s, ref i);
                case '[': return ReadArray(s, ref i);
                case '"': return ReadString(s, ref i);
                case 't': Expect(s, ref i, "true"); return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null"); return null;
                default: return ReadNumber(s, ref i);
            }
        }

        private static void Expect(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw new FormatException("expected '" + word + "' at " + i);
            i += word.Length;
        }

        private static Dictionary<string, object> ReadObject(string s, ref int i)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            i++; // {
            SkipWhite(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return result; }
            while (true)
            {
                SkipWhite(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException("property name expected at " + i);
                string key = ReadString(s, ref i);
                SkipWhite(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("':' expected at " + i);
                i++;
                result[key] = ReadValue(s, ref i);
                SkipWhite(s, ref i);
                if (i >= s.Length) throw new FormatException("unterminated object");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return result; }
                throw new FormatException("',' or '}' expected at " + i);
            }
        }

        private static List<object> ReadArray(string s, ref int i)
        {
            var result = new List<object>();
            i++; // [
            SkipWhite(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return result; }
            while (true)
            {
                result.Add(ReadValue(s, ref i));
                SkipWhite(s, ref i);
                if (i >= s.Length) throw new FormatException("unterminated array");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return result; }
                throw new FormatException("',' or ']' expected at " + i);
            }
        }

        private static string ReadString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++; // opening quote
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("bad \\u escape");
                        sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                        i += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("unterminated string");
        }

        private static double ReadNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (start == i) throw new FormatException("unexpected character '" + s[i] + "' at " + i);
            return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
        }

        // ---- reading helpers ---------------------------------------------------------------------------------

        internal static float Float(Dictionary<string, object> o, string key, float fallback)
        {
            return o != null && o.TryGetValue(key, out object v) && v is double d ? (float)d : fallback;
        }

        internal static int Int(Dictionary<string, object> o, string key, int fallback)
        {
            return o != null && o.TryGetValue(key, out object v) && v is double d ? (int)Math.Round(d) : fallback;
        }

        internal static string String(Dictionary<string, object> o, string key, string fallback)
        {
            return o != null && o.TryGetValue(key, out object v) && v is string str ? str : fallback;
        }

        // ---- writing -----------------------------------------------------------------------------------------

        internal static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        internal static string Number(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
