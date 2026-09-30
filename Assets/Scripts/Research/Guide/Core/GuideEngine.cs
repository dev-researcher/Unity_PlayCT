using System;
using System.Collections.Generic;

namespace PlayCT.Research
{
    /// <summary>Shows a guide message to the participant. The engine decides what and when; the presenter only displays it.</summary>
    public interface IGuidePresenter
    {
        void Present(GuideMessage message, string taskId, int? trial);
    }

    public enum HelpResult
    {
        Shown,
        NoActiveTrial,
        Disabled,
        Cooldown,
        LimitReached,
    }

    /// <summary>
    /// The shared guide logic. It watches the research events the orchestrator and the tasks already log (task_started,
    /// trial_started, trial_summary, task_ended), so it needs nothing from the tasks themselves, and answers them with the
    /// task's configured messages or the standard ones. Message choice is deterministic: each moment cycles through its
    /// messages in order. Every message shown is logged as <c>guide_shown</c> with the guide type and the message ID.
    /// </summary>
    public sealed class GuideEngine
    {
        public const string ExperimentTask = "Experiment";
        public const string ShownEvent = "guide_shown";
        public const string HelpRequestedEvent = "guide_help_requested";

        readonly IResearchEventSink sink;
        readonly IClock clock;
        readonly IGuidePresenter presenter;
        readonly Dictionary<string, GuideProfile> profiles = new Dictionary<string, GuideProfile>();
        readonly Dictionary<(string, GuideMoment), int> shownCounts = new Dictionary<(string, GuideMoment), int>();
        int helpInTrial;
        double lastHelpTime = double.NegativeInfinity;

        public bool Enabled { get; set; } = true;
        public string ActiveTaskId { get; private set; }
        public int? ActiveTrial { get; private set; }
        public bool TrialRunning { get; private set; }
        public int ShownCount { get; private set; }

        public GuideEngine(IResearchEventSink sink, IClock clock, IGuidePresenter presenter, IEnumerable<GuideProfile> profiles = null)
        {
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.presenter = presenter;
            if (profiles == null) return;
            foreach (var profile in profiles) Register(profile);
        }

        /// <summary>Sets a task's guide configuration, replacing an earlier one for the same task.</summary>
        public void Register(GuideProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            profiles[profile.TaskId] = profile;
        }

        /// <summary>Feed every logged research event here. Guide events are ignored.</summary>
        public void OnEvent(ResearchEvent e)
        {
            if (e == null || e.EventType.StartsWith("guide_", StringComparison.Ordinal)) return;

            if (e.Task == ExperimentTask)
            {
                if (e.EventType == "task_started") OnTaskStarted(Text(e, "task_id"));
                else if (e.EventType == "task_ended") OnTaskEnded(Text(e, "task_id"), Text(e, "status") == "completed");
                return;
            }
            if (e.Task != ActiveTaskId) return;

            if (e.EventType == "trial_started")
            {
                ActiveTrial = Integer(e, "trial_index");
                TrialRunning = true;
                helpInTrial = 0;
                Show(ActiveTaskId, GuideMoment.TrialStart, GuideOutcome.Any);
            }
            else if (e.EventType == "trial_summary" && TrialRunning)
            {
                TrialRunning = false;
                var outcome = Text(e, "completion_status") == "completed" ? GuideOutcome.Completed : GuideOutcome.Incomplete;
                Show(ActiveTaskId, GuideMoment.TrialEnd, outcome);
            }
        }

        /// <summary>The participant asked for help. The request is always logged; a message is shown only if the task allows it now.</summary>
        public HelpResult RequestHelp()
        {
            if (!Enabled || ActiveTaskId == null || !TrialRunning) return HelpResult.NoActiveTrial;
            var profile = ProfileFor(ActiveTaskId);
            var result = HelpResult.Shown;
            if (!profile.IsEnabled(GuideMoment.HelpRequest)) result = HelpResult.Disabled;
            else if (clock.MonotonicSeconds - lastHelpTime < profile.HelpCooldownSeconds) result = HelpResult.Cooldown;
            else if (profile.MaxHelpPerTrial > 0 && helpInTrial >= profile.MaxHelpPerTrial) result = HelpResult.LimitReached;

            sink.Log(new ResearchEvent(ActiveTaskId, HelpRequestedEvent)
                .Add("trial_index", ActiveTrial)
                .Add("answered", result == HelpResult.Shown)
                .Add("result", ResultLabel(result)));
            if (result != HelpResult.Shown) return result;

            lastHelpTime = clock.MonotonicSeconds;
            helpInTrial++;
            Show(ActiveTaskId, GuideMoment.HelpRequest, GuideOutcome.Any);
            return result;
        }

        void OnTaskStarted(string taskId)
        {
            if (taskId == null) return;
            ActiveTaskId = taskId;
            ActiveTrial = null;
            TrialRunning = false;
            helpInTrial = 0;
            lastHelpTime = double.NegativeInfinity;
            Show(taskId, GuideMoment.TaskStart, GuideOutcome.Any);
        }

        void OnTaskEnded(string taskId, bool completed)
        {
            if (taskId == null) return;
            if (completed) Show(taskId, GuideMoment.TaskEnd, GuideOutcome.Any);
            if (taskId != ActiveTaskId) return;
            ActiveTaskId = null;
            ActiveTrial = null;
            TrialRunning = false;
        }

        void Show(string taskId, GuideMoment moment, GuideOutcome outcome)
        {
            if (!Enabled) return;
            var profile = ProfileFor(taskId);
            if (!profile.IsEnabled(moment)) return;

            var pool = GuideCatalog.ForMoment(profile.Messages, moment, outcome);
            if (pool.Count == 0) pool = GuideCatalog.ForMoment(GuideCatalog.Common, moment, outcome);
            if (pool.Count == 0) return;

            var key = (taskId, moment);
            shownCounts.TryGetValue(key, out var count);
            shownCounts[key] = count + 1;
            var message = pool[count % pool.Count];

            ShownCount++;
            sink.Log(new ResearchEvent(taskId, ShownEvent)
                .Add("trial_index", moment == GuideMoment.TaskStart || moment == GuideMoment.TaskEnd ? null : ActiveTrial)
                .Add("guide_type", GuideMoments.Label(moment))
                .Add("message_id", message.Id)
                .Add("text", message.Text)
                .Add("sequence", ShownCount));
            presenter?.Present(message, taskId, ActiveTrial);
        }

        GuideProfile ProfileFor(string taskId) => profiles.TryGetValue(taskId, out var profile) ? profile : new GuideProfile(taskId);

        static string ResultLabel(HelpResult result)
        {
            switch (result)
            {
                case HelpResult.Shown: return "shown";
                case HelpResult.Cooldown: return "cooldown";
                case HelpResult.LimitReached: return "limit_reached";
                case HelpResult.Disabled: return "disabled";
                default: return "no_active_trial";
            }
        }

        static string Text(ResearchEvent e, string key) => Find(e, key) as string;

        static int? Integer(ResearchEvent e, string key) => Find(e, key) is int i ? i : (int?)null;

        static object Find(ResearchEvent e, string key)
        {
            foreach (var field in e.Fields)
                if (field.Key == key) return field.Value;
            return null;
        }
    }
}
