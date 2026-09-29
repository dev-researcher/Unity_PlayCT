using System.Collections.Generic;
using System.Text;

namespace PlayCT.Research
{
    /// <summary>RFC 4180 CSV cells and rows with a fixed CRLF line ending, so the same data gives the same bytes on every platform.</summary>
    public static class CsvWriter
    {
        public const string NewLine = "\r\n";

        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        public static void AppendRow(StringBuilder sb, IEnumerable<string> cells)
        {
            var first = true;
            foreach (var cell in cells)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(Escape(cell));
            }
            sb.Append(NewLine);
        }
    }
}
