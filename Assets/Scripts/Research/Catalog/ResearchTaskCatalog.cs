using System.Collections.Generic;
using PlayCT.Tasks.Correo;
using PlayCT.Tasks.Cubo;
using PlayCT.Tasks.Gabinete;
using PlayCT.Tasks.Hanoi;

namespace PlayCT.Research
{
    /// <summary>
    /// The one place that lists the experimental tasks for the shared guide and the shared aggregation. A new task adds its
    /// adapter and guide profile here (or registers them at runtime); the engines themselves know no task.
    /// </summary>
    public static class ResearchTaskCatalog
    {
        public static IReadOnlyList<ITaskMetricsAdapter> MetricsAdapters() => new[]
        {
            HanoiResearchProfile.Metrics,
            CuboResearchProfile.Metrics,
            GabineteResearchProfile.Metrics,
            CorreoResearchProfile.Metrics,
        };

        public static IReadOnlyList<GuideProfile> GuideProfiles() => new[]
        {
            HanoiResearchProfile.Guide(),
            CuboResearchProfile.Guide(),
            GabineteResearchProfile.Guide(),
            CorreoResearchProfile.Guide(),
        };

        public static SessionAggregator CreateAggregator() => new SessionAggregator(MetricsAdapters());
    }
}
