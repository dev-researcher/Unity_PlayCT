using System;

namespace PlayCT.Research
{
    public enum ExperimentCondition
    {
        Static,
        PreAdapted,
    }

    public static class ExperimentConditions
    {
        public static string ToLabel(ExperimentCondition condition) => condition.ToString();

        /// <summary>
        /// Parses the condition text stored by the SessionManager (case, spaces, '-' and '_' are ignored).
        /// "baseline" is accepted as a legacy alias of Static, the value earlier scenes were saved with.
        /// </summary>
        public static bool TryParse(string text, out ExperimentCondition condition)
        {
            condition = ExperimentCondition.Static;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var key = text.Replace("-", "").Replace("_", "").Replace(" ", "").ToLowerInvariant();
            switch (key)
            {
                case "static":
                case "baseline":
                    condition = ExperimentCondition.Static;
                    return true;
                case "preadapted":
                    condition = ExperimentCondition.PreAdapted;
                    return true;
                default:
                    return false;
            }
        }
    }
}
