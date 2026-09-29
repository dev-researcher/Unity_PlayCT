using System.Collections.Generic;
using PlayCT.Research;

namespace PlayCT.Tasks.Correo
{
    public sealed class CorreoSummary
    {
        public int TrialIndex;
        public int ShipmentAttempts;
        public int ValidShipments;
        public int InvalidShipments;
        public int ShipmentCount;
        public int RejectedCapacityAttempts;
        public int RejectedConstraintAttempts;
        public int RejectedPathAttempts;
        public int RejectedSelectionAttempts;
        public string FinalLocations;
        public int? P1ArrivalShipment;
        public int? P2ArrivalShipment;
        public int? P3ArrivalShipment;
        public double? P1ArrivalTimeSeconds;
        public double? P2ArrivalTimeSeconds;
        public double? P3ArrivalTimeSeconds;
        public bool PrioritySatisfied;
        public double TimeLimitSeconds;
        public bool Completed;
        public string Status;
        public double? CompletionTimeSeconds;
        public double? FirstActionLatencySeconds;
        public double ElapsedSeconds;
        public List<string> ShipmentSequence = new List<string>();
        public List<string> ActionSequence = new List<string>();

        public void AddTo(ResearchEvent e)
        {
            e.Add("trial_index", TrialIndex)
                .Add("shipment_attempts", ShipmentAttempts)
                .Add("valid_shipments", ValidShipments)
                .Add("invalid_shipments", InvalidShipments)
                .Add("shipment_count", ShipmentCount)
                .Add("rejected_capacity_attempts", RejectedCapacityAttempts)
                .Add("rejected_constraint_attempts", RejectedConstraintAttempts)
                .Add("rejected_path_attempts", RejectedPathAttempts)
                .Add("rejected_selection_attempts", RejectedSelectionAttempts)
                .Add("final_locations", FinalLocations)
                .Add("p1_arrival_shipment", P1ArrivalShipment)
                .Add("p2_arrival_shipment", P2ArrivalShipment)
                .Add("p3_arrival_shipment", P3ArrivalShipment)
                .Add("p1_arrival_time_s", P1ArrivalTimeSeconds)
                .Add("p2_arrival_time_s", P2ArrivalTimeSeconds)
                .Add("p3_arrival_time_s", P3ArrivalTimeSeconds)
                .Add("priority_satisfied", PrioritySatisfied)
                .Add("time_limit_s", TimeLimitSeconds)
                .Add("completion_status", Status)
                .Add("completion_time_s", CompletionTimeSeconds)
                .Add("first_action_latency_s", FirstActionLatencySeconds)
                .Add("elapsed_s", ElapsedSeconds)
                .Add("shipment_sequence", ShipmentSequence)
                .Add("action_sequence", ActionSequence);
        }

        public string ToJson(ISessionInfo session)
        {
            var e = new ResearchEvent(CorreoTrial.TaskName, "trial_summary");
            AddTo(e);
            var fields = new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>("session_id", session.SessionId),
                new KeyValuePair<string, object>("participant_id", session.ParticipantId),
                new KeyValuePair<string, object>("condition", session.Condition),
                new KeyValuePair<string, object>("task", e.Task),
            };
            fields.AddRange(e.Fields);
            return JsonLine.Serialize(fields);
        }
    }
}
