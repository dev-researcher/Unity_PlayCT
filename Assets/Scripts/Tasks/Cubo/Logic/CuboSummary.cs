using System.Collections.Generic;
using PlayCT.Research;

namespace PlayCT.Tasks.Cubo
{
    public sealed class CuboSummary
    {
        public int TrialIndex;
        public string MiniTask;
        public string Goal;
        public int ValidRotations;
        public int InvalidActions;
        public int AttemptCount;
        public int ReferenceMoves;
        public double TimeLimitSeconds;
        public bool Completed;
        public string Status;
        public double? CompletionTimeSeconds;
        public double? FirstActionLatencySeconds;
        public double ElapsedSeconds;
        public string AnswerFace;
        public string InitialState;
        public string FinalState;
        public List<string> RotationSequence = new List<string>();
        public List<string> ActionSequence = new List<string>();

        public void AddTo(ResearchEvent e)
        {
            e.Add("trial_index", TrialIndex)
                .Add("mini_task", MiniTask)
                .Add("goal", Goal)
                .Add("valid_rotations", ValidRotations)
                .Add("invalid_actions", InvalidActions)
                .Add("attempt_count", AttemptCount)
                .Add("reference_moves", ReferenceMoves)
                .Add("time_limit_s", TimeLimitSeconds)
                .Add("completion_status", Status)
                .Add("completion_time_s", CompletionTimeSeconds)
                .Add("first_action_latency_s", FirstActionLatencySeconds)
                .Add("elapsed_s", ElapsedSeconds)
                .Add("answer_face", AnswerFace)
                .Add("initial_state", InitialState)
                .Add("resulting_state", FinalState)
                .Add("rotation_sequence", RotationSequence)
                .Add("action_sequence", ActionSequence);
        }

        public string ToJson(ISessionInfo session)
        {
            var e = new ResearchEvent(CuboTrial.TaskName, "trial_summary");
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
