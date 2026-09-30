using PlayCT.Research;

namespace PlayCT.Tasks.Hanoi
{
    /// <summary>What Hanoi tells the shared aggregation and the shared guide. Configuration only; the task itself is untouched.</summary>
    public static class HanoiResearchProfile
    {
        public static ITaskMetricsAdapter Metrics { get; } = new MappedTaskMetricsAdapter(HanoiTrial.TaskName,
            attemptsField: "attempt_count", validActionsField: "total_moves", invalidActionsField: "invalid_attempts",
            movesField: "total_moves", efficiencyField: "efficiency", actionSequenceField: "attempt_sequence",
            extraFields: new[]
            {
                "disk_count", "optimal_moves", "illegal_placements", "off_peg_releases", "time_from_first_grab_s", "first_grab_latency_s",
                "excess_moves", "resulting_state", "move_sequence", "end_reason",
            });

        public static GuideProfile Guide()
        {
            return new GuideProfile(HanoiTrial.TaskName)
                .Add("hanoi.help.1", GuideMoment.HelpRequest, "¿Qué observas sobre el tamaño de los discos y el lugar donde se apoyan?")
                .Add("hanoi.help.2", GuideMoment.HelpRequest, "¿Qué restricción estás observando al soltar un disco?")
                .Add("hanoi.help.3", GuideMoment.HelpRequest, "¿Qué cambia en los tres postes cuando mueves un disco?")
                .Add("hanoi.help.4", GuideMoment.HelpRequest, "¿Qué crees que permanecerá igual mientras mueves los discos?");
        }
    }
}
