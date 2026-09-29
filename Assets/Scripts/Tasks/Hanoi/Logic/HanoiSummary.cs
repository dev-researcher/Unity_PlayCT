using System.Collections.Generic;
using PlayCT.Research;

namespace PlayCT.Tasks.Hanoi
{
    public sealed class HanoiSummary
    {
        public int TrialIndex;
        public int DiskCount;
        public int OptimalMoves;
        public int TotalMoves;
        public int InvalidAttempts;
        public int IllegalPlacements;
        public int OffPegReleases;
        public int AttemptCount;
        public bool Completed;
        public string Status;
        public double? CompletionTimeSeconds;
        public double? TimeFromFirstGrabSeconds;
        public double? FirstGrabLatencySeconds;
        public double ElapsedSeconds;
        public double? Efficiency;
        public int? ExcessMoves;
        public string FinalState;
        public List<string> MoveSequence = new List<string>();
        public List<string> AttemptSequence = new List<string>();

        public void AddTo(ResearchEvent e)
        {
            e.Add("trial_index", TrialIndex)
                .Add("disk_count", DiskCount)
                .Add("optimal_moves", OptimalMoves)
                .Add("total_moves", TotalMoves)
                .Add("invalid_attempts", InvalidAttempts)
                .Add("illegal_placements", IllegalPlacements)
                .Add("off_peg_releases", OffPegReleases)
                .Add("attempt_count", AttemptCount)
                .Add("completion_status", Status)
                .Add("completion_time_s", CompletionTimeSeconds)
                .Add("time_from_first_grab_s", TimeFromFirstGrabSeconds)
                .Add("first_grab_latency_s", FirstGrabLatencySeconds)
                .Add("elapsed_s", ElapsedSeconds)
                .Add("efficiency", Efficiency)
                .Add("excess_moves", ExcessMoves)
                .Add("resulting_state", FinalState)
                .Add("move_sequence", MoveSequence)
                .Add("attempt_sequence", AttemptSequence);
        }

        public string ToJson(ISessionInfo session)
        {
            var e = new ResearchEvent(HanoiTrial.TaskName, "trial_summary");
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
