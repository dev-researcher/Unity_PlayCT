using System.Collections.Generic;
using PlayCT.Research;

namespace PlayCT.Tasks.Gabinete
{
    public sealed class GabineteSummary
    {
        public int TrialIndex;
        public int ShapeCount;
        public int PlacementAttempts;
        public int ValidPlacements;
        public int InvalidAttempts;
        public int WrongOpeningAttempts;
        public int WrongOrientationAttempts;
        public int MisalignedAttempts;
        public int OtherInvalidAttempts;
        public int OffTargetReleases;
        public int PickUps;
        public int RotationCount;
        public double TotalRotationDegrees;
        public int CompletedPieces;
        public double TimeLimitSeconds;
        public bool Completed;
        public string Status;
        public double? CompletionTimeSeconds;
        public double? FirstActionLatencySeconds;
        public double ElapsedSeconds;
        public string FinalState;
        public List<string> PlacementSequence = new List<string>();
        public List<string> ActionSequence = new List<string>();

        public void AddTo(ResearchEvent e)
        {
            e.Add("trial_index", TrialIndex)
                .Add("shape_count", ShapeCount)
                .Add("placement_attempts", PlacementAttempts)
                .Add("valid_placements", ValidPlacements)
                .Add("invalid_attempts", InvalidAttempts)
                .Add("wrong_opening_attempts", WrongOpeningAttempts)
                .Add("wrong_orientation_attempts", WrongOrientationAttempts)
                .Add("misaligned_attempts", MisalignedAttempts)
                .Add("other_invalid_attempts", OtherInvalidAttempts)
                .Add("off_target_releases", OffTargetReleases)
                .Add("pick_ups", PickUps)
                .Add("rotation_count", RotationCount)
                .Add("total_rotation_deg", TotalRotationDegrees)
                .Add("completed_pieces", CompletedPieces)
                .Add("time_limit_s", TimeLimitSeconds)
                .Add("completion_status", Status)
                .Add("completion_time_s", CompletionTimeSeconds)
                .Add("first_action_latency_s", FirstActionLatencySeconds)
                .Add("elapsed_s", ElapsedSeconds)
                .Add("resulting_state", FinalState)
                .Add("placement_sequence", PlacementSequence)
                .Add("action_sequence", ActionSequence);
        }

        public string ToJson(ISessionInfo session)
        {
            var e = new ResearchEvent(GabineteTrial.TaskName, "trial_summary");
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
