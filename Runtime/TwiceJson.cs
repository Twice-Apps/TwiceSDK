using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TwiceSDK.Analytics
{
    /// <summary>
    /// Minimal, dependency-free JSON writer. Supports exactly the value types the
    /// Twice analytics backend accepts: numbers (invariant '.' decimal), bool,
    /// escaped string, null, and nested flat objects (used for event params).
    /// No reflection, no third-party deps, WebGL-safe.
    /// </summary>
    internal static class TwiceJson
    {
        /// <summary>Serializes a flat dictionary into a JSON object string, e.g. {"k":1,"s":"v"}.</summary>
        public static string SerializeObject(IDictionary<string, object> dict)
        {
            var sb = new StringBuilder(128);
            AppendObject(sb, dict);
            return sb.ToString();
        }

        public static void AppendObject(StringBuilder sb, IDictionary<string, object> dict)
        {
            sb.Append('{');
            if (dict != null)
            {
                bool first = true;
                foreach (var kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    AppendString(sb, kv.Key);
                    sb.Append(':');
                    AppendValue(sb, kv.Value);
                }
            }
            sb.Append('}');
        }

        public static void AppendValue(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case string s:
                    AppendString(sb, s);
                    break;
                case float f:
                    sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case double d:
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case decimal m:
                    sb.Append(m.ToString(CultureInfo.InvariantCulture));
                    break;
                case sbyte _:
                case byte _:
                case short _:
                case ushort _:
                case int _:
                case uint _:
                case long _:
                case ulong _:
                    sb.Append(System.Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
                case IDictionary<string, object> nested:
                    AppendObject(sb, nested);
                    break;
                default:
                    // Unknown type: fall back to its invariant string form, quoted.
                    AppendString(sb, System.Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
            }
        }

        public static void AppendString(StringBuilder sb, string s)
        {
            sb.Append('"');
            if (s != null)
            {
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\f': sb.Append("\\f"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < 0x20)
                                sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            else
                                sb.Append(c);
                            break;
                    }
                }
            }
            sb.Append('"');
        }
    }
}
