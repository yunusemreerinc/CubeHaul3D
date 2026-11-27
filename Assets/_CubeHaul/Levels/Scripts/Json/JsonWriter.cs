using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CubeHaul.Levels.Json
{
    /// <summary>
    /// Writes JSON exactly as JavaScript's JSON.stringify does, so text written here matches text written by the
    /// TypeScript tools byte for byte: numbers as Number.prototype.toString prints them, the same string escapes, and
    /// the same key order (integer-like keys first, ascending, then the rest in insertion order).
    /// </summary>
    public static class JsonWriter
    {
        /// <summary>JSON.stringify(value).</summary>
        public static string Stringify(JsonValue value)
        {
            var sb = new StringBuilder();
            Write(sb, value, null, "");
            return sb.ToString();
        }

        /// <summary>JSON.stringify(value, null, indent).</summary>
        public static string Stringify(JsonValue value, int indent)
        {
            var sb = new StringBuilder();
            Write(sb, value, new string(' ', indent), "");
            return sb.ToString();
        }

        static void Write(StringBuilder sb, JsonValue v, string gap, string indent)
        {
            switch (v.Kind)
            {
                case JsonKind.Null: sb.Append("null"); return;
                case JsonKind.Boolean: sb.Append(v.Boolean ? "true" : "false"); return;
                case JsonKind.Number: sb.Append(FormatNumber(v.Number)); return;
                case JsonKind.String: WriteString(sb, v.String); return;
                case JsonKind.Array:
                {
                    if (v.Items.Count == 0)
                    {
                        sb.Append("[]");
                        return;
                    }
                    string inner = gap == null ? null : indent + gap;
                    sb.Append('[');
                    for (int i = 0; i < v.Items.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        if (gap != null) sb.Append('\n').Append(inner);
                        Write(sb, v.Items[i], gap, inner);
                    }
                    if (gap != null) sb.Append('\n').Append(indent);
                    sb.Append(']');
                    return;
                }
                default:
                {
                    List<KeyValuePair<string, JsonValue>> members = OrderedMembers(v);
                    if (members.Count == 0)
                    {
                        sb.Append("{}");
                        return;
                    }
                    string inner = gap == null ? null : indent + gap;
                    sb.Append('{');
                    for (int i = 0; i < members.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        if (gap != null) sb.Append('\n').Append(inner);
                        WriteString(sb, members[i].Key);
                        sb.Append(gap == null ? ":" : ": ");
                        Write(sb, members[i].Value, gap, inner);
                    }
                    if (gap != null) sb.Append('\n').Append(indent);
                    sb.Append('}');
                    return;
                }
            }
        }

        /// <summary>JavaScript property order: array-index keys ascending, then the others as inserted.</summary>
        static List<KeyValuePair<string, JsonValue>> OrderedMembers(JsonValue v)
        {
            var index = new List<KeyValuePair<string, JsonValue>>();
            var rest = new List<KeyValuePair<string, JsonValue>>();
            foreach (KeyValuePair<string, JsonValue> m in v.Members) (IsArrayIndex(m.Key) ? index : rest).Add(m);
            index.Sort((a, b) => ulong.Parse(a.Key, CultureInfo.InvariantCulture).CompareTo(ulong.Parse(b.Key, CultureInfo.InvariantCulture)));
            index.AddRange(rest);
            return index;
        }

        static bool IsArrayIndex(string key)
        {
            if (key.Length == 0 || key.Length > 10) return false;
            if (key.Length > 1 && key[0] == '0') return false;
            foreach (char c in key)
                if (c < '0' || c > '9') return false;
            return ulong.Parse(key, CultureInfo.InvariantCulture) < 4294967295UL;
        }

        /// <summary>JSON.stringify of a string: quotes, the short escapes, \u00xx for other controls and lone surrogates.</summary>
        public static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); continue;
                    case '\\': sb.Append("\\\\"); continue;
                    case '\b': sb.Append("\\b"); continue;
                    case '\f': sb.Append("\\f"); continue;
                    case '\n': sb.Append("\\n"); continue;
                    case '\r': sb.Append("\\r"); continue;
                    case '\t': sb.Append("\\t"); continue;
                }
                if (c < 0x20)
                {
                    sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                }
                else if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    sb.Append(c).Append(s[i + 1]);
                    i++;
                }
                else if (char.IsSurrogate(c))
                {
                    sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                }
                else sb.Append(c);
            }
            sb.Append('"');
        }

        /// <summary>
        /// Number.prototype.toString / JSON.stringify of a number: the shortest digits that read back as the same double,
        /// then JavaScript's layout rules (plain up to 1e21, exponent below 1e-6). Found by trying 1..17 significant
        /// digits with the "E" format, read back with <see cref="JsonNumber"/> (Mono's double.Parse can be a last bit off).
        /// </summary>
        public static string FormatNumber(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "null";
            if (d == 0) return "0"; // also -0
            string sign = d < 0 ? "-" : "";
            double a = Math.Abs(d);
            string digits = null;
            int exponent = 0;
            for (int p = 1; p <= 17; p++)
            {
                string e = a.ToString("E" + (p - 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                if (JsonNumber.Parse(e) != a && p < 17) continue; // Mono's double.Parse is not always correctly rounded
                int at = e.IndexOf('E');
                digits = e.Substring(0, at).Replace(".", "");
                exponent = int.Parse(e.Substring(at + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                break;
            }
            digits = digits.TrimEnd('0');
            if (digits.Length == 0) digits = "0";
            int k = digits.Length;
            int n = exponent + 1; // value = 0.digits * 10^n
            if (k <= n && n <= 21) return sign + digits + new string('0', n - k);
            if (0 < n && n <= 21) return sign + digits.Substring(0, n) + "." + digits.Substring(n);
            if (-6 < n && n <= 0) return sign + "0." + new string('0', -n) + digits;
            string exp = (n - 1 >= 0 ? "+" : "-") + Math.Abs(n - 1).ToString(CultureInfo.InvariantCulture);
            return sign + (k == 1 ? digits : digits.Substring(0, 1) + "." + digits.Substring(1)) + "e" + exp;
        }
    }
}
