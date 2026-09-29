using PlayCT.Research;

namespace PlayCT.Tasks.Gabinete
{
    /// <summary>What Gabinete de Formas tells the shared aggregation and the shared guide. Configuration only; the task itself is untouched.</summary>
    public static class GabineteResearchProfile
    {
        public static ITaskMetricsAdapter Metrics { get; } = new MappedTaskMetricsAdapter(GabineteTrial.TaskName,
            attemptsField: "placement_attempts", validActionsField: "valid_placements", invalidActionsField: "invalid_attempts",
            movesField: "valid_placements", efficiencyField: null, actionSequenceField: "action_sequence",
            extraFields: new[]
            {
                "shape_count", "wrong_opening_attempts", "wrong_orientation_attempts", "misaligned_attempts", "other_invalid_attempts",
                "off_target_releases", "pick_ups", "rotation_count", "total_rotation_deg", "completed_pieces", "time_limit_s",
                "first_action_latency_s", "resulting_state", "placement_sequence", "end_reason",
            });

        public static GuideProfile Guide()
        {
            return new GuideProfile(GabineteTrial.TaskName)
                .Add("gabinete.help.1", GuideMoment.HelpRequest, "¿Qué observas en el contorno de la pieza y en el de la abertura?")
                .Add("gabinete.help.2", GuideMoment.HelpRequest, "¿Qué cambia cuando giras la pieza?")
                .Add("gabinete.help.3", GuideMoment.HelpRequest, "¿Qué restricción estás observando?")
                .Add("gabinete.help.4", GuideMoment.HelpRequest, "¿Qué crees que permanecerá igual cuando cambias la posición de la pieza?");
        }
    }
}
