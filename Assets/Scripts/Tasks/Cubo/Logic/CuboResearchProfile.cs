using PlayCT.Research;

namespace PlayCT.Tasks.Cubo
{
    /// <summary>What Cubo de Relaciones tells the shared aggregation and the shared guide. Configuration only; the task itself is untouched.</summary>
    public static class CuboResearchProfile
    {
        public static ITaskMetricsAdapter Metrics { get; } = new MappedTaskMetricsAdapter(CuboTrial.TaskName,
            attemptsField: "attempt_count", validActionsField: "valid_rotations", invalidActionsField: "invalid_actions",
            movesField: "valid_rotations", efficiencyField: null, actionSequenceField: "action_sequence",
            extraFields: new[]
            {
                "mini_task", "goal", "reference_moves", "time_limit_s", "first_action_latency_s", "answer_face", "initial_state",
                "resulting_state", "rotation_sequence", "end_reason",
            });

        public static GuideProfile Guide()
        {
            return new GuideProfile(CuboTrial.TaskName)
                .Add("cubo.help.1", GuideMoment.HelpRequest, "¿Qué cambia y qué permanece igual cuando giras una cara?")
                .Add("cubo.help.2", GuideMoment.HelpRequest, "¿Qué observas en las piezas que no se mueven?")
                .Add("cubo.help.3", GuideMoment.HelpRequest, "¿Qué restricción estás observando en los giros que puedes hacer?")
                .Add("cubo.help.4", GuideMoment.HelpRequest, "¿Qué podrías probar con un solo giro?");
        }
    }
}
