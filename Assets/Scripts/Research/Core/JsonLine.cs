using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PlayCT.Research
{
    public static class JsonLine
    {
        public static string Serialize(IEnumerable<KeyValuePair<string, object>> fields)
        {
            var sb = new StringBuilder(256);
            sb.Append('{');
            var first = true;
            foreach (var field in fields)
            {
                if (!first) sb.Append(',');
                first = false;
                AppendString(sb, field.Key);
                sb.Append(':');
                AppendValue(sb, field.Value);
            }
            sb.Append('}');
            return sb.ToString();
        }

        public static void AppendValue(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case string s:
                    AppendString(sb, s);
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case long l:
                    sb.Append(l.ToString(CultureInfo.InvariantCulture));
                    break;
                case float f:
                    AppendDouble(sb, f);
                    break;
                case double d:
                    AppendDouble(sb, d);
                    break;
                case DateTime dt:
                    AppendString(sb, dt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
                    break;
                case IEnumerable seq:
                    sb.Append('[');
                    var first = true;
                    foreach (var item in seq)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        AppendValue(sb, item);
                    }
                    sb.Append(']');
                    break;
                default:
                    AppendString(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
            }
        }

        static void AppendDouble(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d))
                sb.Append("null");
            else
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        static void AppendString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
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
            sb.Append('"');
        }
    }
}
