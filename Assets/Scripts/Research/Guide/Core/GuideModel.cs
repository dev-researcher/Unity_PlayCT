using System;
using System.Collections.Generic;

namespace PlayCT.Research
{
    /// <summary>The moments at which the shared guide may speak.</summary>
    public enum GuideMoment
    {
        TaskStart,
        TrialStart,
        HelpRequest,
        TrialEnd,
        TaskEnd,
    }

    public enum GuideOutcome
    {
        Any,
        Completed,
        Incomplete,
    }

    public static class GuideMoments
    {
        /// <summary>The value logged as <c>guide_type</c>.</summary>
        public static string Label(GuideMoment moment)
        {
            switch (moment)
            {
                case GuideMoment.TaskStart: return "task_start";
                case GuideMoment.TrialStart: return "trial_start";
                case GuideMoment.HelpRequest: return "help_request";
                case GuideMoment.TrialEnd: return "trial_end";
                case GuideMoment.TaskEnd: return "task_end";
                default: throw new ArgumentOutOfRangeException(nameof(moment));
            }
        }
    }

    /// <summary>
    /// One guide message: a stable ID (logged), the moment it belongs to and its Spanish text. Messages are questions or
    /// observations about the situation; they never give a move, a solution or a score.
    /// </summary>
    public sealed class GuideMessage
    {
        public string Id { get; }
        public GuideMoment Moment { get; }
        public GuideOutcome Outcome { get; }
        public string Text { get; }

        public GuideMessage(string id, GuideMoment moment, string text, GuideOutcome outcome = GuideOutcome.Any)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A message needs an ID.", nameof(id));
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("A message needs text.", nameof(text));
            Id = id;
            Moment = moment;
            Text = text;
            Outcome = outcome;
        }
    }

    /// <summary>
    /// What one task configures about the guide: which moments are active, its own messages for each moment (the shared
    /// messages are used for any moment it does not define), and how often help may be requested. It holds no game logic.
    /// </summary>
    public sealed class GuideProfile
    {
        readonly List<GuideMessage> messages = new List<GuideMessage>();
        readonly HashSet<GuideMoment> disabled = new HashSet<GuideMoment>();

        public string TaskId { get; }
        public bool HelpRequestsEnabled { get; set; } = true;

        /// <summary>Least time between two accepted help requests; requests sooner than this are logged and not answered.</summary>
        public double HelpCooldownSeconds { get; set; } = 15;

        /// <summary>Most help messages in one trial; 0 means no limit.</summary>
        public int MaxHelpPerTrial { get; set; }

        public IReadOnlyList<GuideMessage> Messages => messages;

        public GuideProfile(string taskId)
        {
            TaskId = taskId ?? throw new ArgumentNullException(nameof(taskId));
        }

        public GuideProfile Add(GuideMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (messages.Exists(m => m.Id == message.Id)) throw new ArgumentException($"Message '{message.Id}' is already defined.");
            messages.Add(message);
            return this;
        }

        public GuideProfile Add(string id, GuideMoment moment, string text, GuideOutcome outcome = GuideOutcome.Any) =>
            Add(new GuideMessage(id, moment, text, outcome));

        public GuideProfile Disable(GuideMoment moment)
        {
            disabled.Add(moment);
            return this;
        }

        public bool IsEnabled(GuideMoment moment) => !disabled.Contains(moment) && (moment != GuideMoment.HelpRequest || HelpRequestsEnabled);
    }
}
