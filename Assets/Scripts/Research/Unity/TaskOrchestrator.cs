using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayCT.Research
{
    /// <summary>
    /// Experiment-level sequencing: session and condition are read from the existing SessionManager/ConditionManager,
    /// then the configured task IDs are run in order through <see cref="IExperimentTask"/>. Tasks are registered from
    /// the inspector list or with <see cref="Register"/>; adding a task needs no change here.
    /// </summary>
    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    public class TaskOrchestrator : MonoBehaviour
    {
        [Tooltip("Task IDs in the order they run. An ID with no registered task is skipped and logged.")]
        [SerializeField] string[] taskSequence = { "Hanoi" };
        [Tooltip("Components implementing IExperimentTask.")]
        [SerializeField] MonoBehaviour[] tasks = new MonoBehaviour[0];
        [SerializeField] bool beginOnStart = true;
        [SerializeField] SessionManager session;
        [SerializeField] ConditionManager conditionManager;
        [SerializeField] EventLogger eventLogger;

        readonly Dictionary<string, IExperimentTask> registry = new Dictionary<string, IExperimentTask>();
        ExperimentSequencer sequencer;
        bool initialized;

        public ExperimentSequencer Sequencer => sequencer;
        public ExperimentState State => sequencer != null ? sequencer.State : ExperimentState.NotStarted;
        public string CurrentTaskId => sequencer?.CurrentTaskId;
        public ConditionManager Conditions => conditionManager;

        public event Action<ExperimentSequencer> ExperimentFinished;

        void Awake() => Initialize();

        void Start()
        {
            if (beginOnStart && sequencer == null) BeginExperiment();
        }

        void OnApplicationQuit() => Abort("application_quit");

        void Initialize()
        {
            if (initialized) return;
            initialized = true;

            if (session == null) session = FindFirstObjectByType<SessionManager>();
            if (session == null) session = new GameObject("SessionManager").AddComponent<SessionManager>();
            if (eventLogger == null) eventLogger = FindFirstObjectByType<EventLogger>();
            if (eventLogger == null) eventLogger = new GameObject("EventLogger").AddComponent<EventLogger>();
            eventLogger.Session = session;
            if (conditionManager == null) conditionManager = FindFirstObjectByType<ConditionManager>();
            if (conditionManager == null) conditionManager = new GameObject("ConditionManager").AddComponent<ConditionManager>();
            conditionManager.Session = session;

            foreach (var behaviour in tasks)
            {
                if (behaviour == null) continue;
                if (behaviour is IExperimentTask task) Register(task);
                else Debug.LogError($"[TaskOrchestrator] {behaviour.name} does not implement IExperimentTask.");
            }
        }

        public void Register(IExperimentTask task)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            if (registry.TryGetValue(task.TaskId, out var existing) && !ReferenceEquals(existing, task))
                throw new InvalidOperationException($"A different task is already registered as '{task.TaskId}'.");
            registry[task.TaskId] = task;
        }

        public void SetSequence(IEnumerable<string> taskIds)
        {
            if (sequencer != null && sequencer.State == ExperimentState.Running)
                throw new InvalidOperationException("Cannot change the sequence while the experiment is running.");
            taskSequence = new List<string>(taskIds).ToArray();
            sequencer = null;
        }

        /// <summary>Starts the experiment with the session's current condition. Returns false if the condition is invalid.</summary>
        public bool BeginExperiment()
        {
            Initialize();
            if (sequencer != null && sequencer.State == ExperimentState.Running) return false;
            if (!session.HasSession) session.BeginSession();

            if (!conditionManager.TryGetCurrent(out var condition))
            {
                Debug.LogError($"[TaskOrchestrator] Unsupported condition '{session.Condition}'. Use Static or PreAdapted.");
                eventLogger.Log(new ResearchEvent(ExperimentSequencer.TaskName, "experiment_rejected")
                    .Add("reason", "invalid_condition")
                    .Add("condition_text", session.Condition));
                return false;
            }

            sequencer = new ExperimentSequencer(taskSequence, id => registry.TryGetValue(id, out var task) ? task : null,
                condition, eventLogger, new UnityClock());
            sequencer.Finished += s => ExperimentFinished?.Invoke(s);
            sequencer.Start();
            return true;
        }

        public void Abort(string reason) => sequencer?.Abort(reason);
    }
}
