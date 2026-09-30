using System.Text;

namespace PlayCT.App
{
    /// <summary>
    /// What an anonymised participant code may look like: letters, digits, '-' and '_', at most 16 characters. The limit and the
    /// character set keep names and other free text out of the ID and keep it safe inside file names and CSV cells.
    /// </summary>
    public static class ParticipantIdRules
    {
        public const int MaxLength = 16;

        public static bool IsAllowed(char c) => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '-' || c == '_';

        public static string Normalize(string text) => string.IsNullOrEmpty(text) ? string.Empty : text.Trim();

        public static bool IsValid(string text)
        {
            var id = Normalize(text);
            if (id.Length == 0 || id.Length > MaxLength) return false;
            foreach (var c in id)
                if (!IsAllowed(c)) return false;
            return true;
        }

        /// <summary>Keeps only the allowed characters, in upper case, up to the maximum length.</summary>
        public static string Sanitize(string text)
        {
            var sb = new StringBuilder();
            foreach (var c in Normalize(text))
            {
                if (!IsAllowed(c) || sb.Length >= MaxLength) continue;
                sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }
    }
}
