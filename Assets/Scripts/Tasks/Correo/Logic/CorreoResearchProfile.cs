using PlayCT.Research;

namespace PlayCT.Tasks.Correo
{
    /// <summary>What El Correo tells the shared aggregation and the shared guide. Configuration only; the task itself is untouched.</summary>
    public static class CorreoResearchProfile
    {
        public static ITaskMetricsAdapter Metrics { get; } = new MappedTaskMetricsAdapter(CorreoTrial.TaskName,
            attemptsField: "shipment_attempts", validActionsField: "valid_shipments", invalidActionsField: "invalid_shipments",
            movesField: "shipment_count", efficiencyField: null, actionSequenceField: "action_sequence",
            extraFields: new[]
            {
                "rejected_capacity_attempts", "rejected_constraint_attempts", "rejected_path_attempts", "rejected_selection_attempts",
                "final_locations", "p1_arrival_shipment", "p2_arrival_shipment", "p3_arrival_shipment", "p1_arrival_time_s",
                "p2_arrival_time_s", "p3_arrival_time_s", "priority_satisfied", "time_limit_s", "first_action_latency_s",
                "shipment_sequence", "end_reason",
            });

        public static GuideProfile Guide()
        {
            return new GuideProfile(CorreoTrial.TaskName)
                .Add("correo.help.1", GuideMoment.HelpRequest, "¿Qué observas sobre cuántos paquetes admite cada camino?")
                .Add("correo.help.2", GuideMoment.HelpRequest, "¿Qué restricción estás observando sobre los paquetes?")
                .Add("correo.help.3", GuideMoment.HelpRequest, "¿Qué cambia en el mapa cuando realizas un envío?")
                .Add("correo.help.4", GuideMoment.HelpRequest, "¿Te recuerda a algo de lo que hiciste antes?")
                .Add("correo.trial_start.1", GuideMoment.TrialStart, "¿Qué observas en los caminos y en los paquetes?");
        }
    }
}
