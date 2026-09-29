using System;
using System.Collections;
using System.Collections.Generic;
using PlayCT.Research;
using UnityEngine;

namespace PlayCT.Tasks.Gabinete
{
    /// <summary>
    /// Runs the Gabinete de Formas task in the scene as a common <see cref="IExperimentTask"/>: the orchestrator starts it,
    /// it runs its trials (4, 6 and 8 shapes by default) in order, raises <see cref="Completed"/> after the last one, and
    /// cleans up in <see cref="EndTask"/>. It connects the pure <see cref="GabineteTrial"/> logic to the visual cabinet and
    /// the XR pieces and logs through the shared EventLogger. It never starts or ends the session.
    /// </summary>
    [DisallowMultipleComponent]
    public class GabineteTask : MonoBehaviour, IExperimentTask
    {
        public const string Id = GabineteTrial.TaskName;

        [Tooltip("Trials in order. Leave empty for the default protocol (cabinets of 4, 6 and 8 shapes).")]
        [SerializeField] GabineteTrialSpec[] trials = new GabineteTrialSpec[0];
        [Tooltip("Component implementing IGabineteView (the visual cabinet).")]
        [SerializeField] MonoBehaviour view;
        [Tooltip("Objects of other tasks that are switched off while this task uses the table.")]
        [SerializeField] GameObject[] hideWhileActive = new GameObject[0];
        [SerializeField] float interTrialDelay = 1.0f;
        [SerializeField] GabineteRules rules = GabineteRules.Default;
        [SerializeField] SessionManager session;
        [SerializeField] EventLogger eventLogger;

        readonly List<GabineteTrialConfig> protocol = new List<GabineteTrialConfig>();
        readonly List<GameObject> hidden = new List<GameObject>();
        readonly List<GabineteSummary> summaries = new List<GabineteSummary>();
        readonly IClock clock = new UnityClock();
        IGabineteView cabinetView;
        GabineteTrial trial;
        Coroutine advanceRoutine;
        int trialCounter;
        bool initialized;
        bool started;
        bool finished;
        bool ended;

        public string TaskId => Id;
        public bool IsRunning => started && !ended && !finished;
        public bool IsCompleted => finished;
        public ExperimentCondition? Condition { get; private set; }
        public GabineteTrial Trial => trial;
        public int TrialCount => protocol.Count;
        public IReadOnlyList<GabineteSummary> Summaries => summaries;

        public event Action<IExperimentTask> Completed;
        public event Action<GabineteTrial> TrialStarted;
        public event Action<GabineteSummary> TrialClosed;

        void Awake() => Initialize();

        void Update()
        {
            if (trial != null && trial.IsRunning && trial.CheckTimeLimit()) CloseTrial();
        }

        void OnApplicationQuit()
        {
            if (IsRunning) EndTask("application_quit");
        }

        void Initialize()
        {
            if (initialized) return;
            initialized = true;

            if (session == null) session = FindFirstObjectByType<SessionManager>();
            if (session == null) session = new GameObject("SessionManager").AddComponent<SessionManager>();
            if (eventLogger == null) eventLogger = FindFirstObjectByType<EventLogger>();
            if (eventLogger == null) eventLogger = new GameObject("EventLogger").AddComponent<EventLogger>();
            eventLogger.Session = session;

            if (view != null)
            {
                cabinetView = view as IGabineteView;
                if (cabinetView == null) Debug.LogError($"[GabineteTask] {view.name} does not implement IGabineteView.");
            }
        }

        public void StartTask(ExperimentCondition condition)
        {
            Initialize();
            if (IsRunning) EndTask("restarted");

            Condition = condition;
            BuildProtocol();
            summaries.Clear();
            trialCounter = 0;
            started = true;
            finished = false;
            ended = false;

            foreach (var target in hideWhileActive)
            {
                if (target != null && target.activeSelf)
                {
                    target.SetActive(false);
                    hidden.Add(target);
                }
            }
            cabinetView?.SetVisible(true);
            BeginTrial(0);
        }

        public void EndTask(string reason)
        {
            if (!started || ended) return;
            ended = true;
            if (advanceRoutine != null) StopCoroutine(advanceRoutine);
            advanceRoutine = null;
            if (trial != null && trial.IsRunning) CloseTrial(reason);

            cabinetView?.LockPieces();
            cabinetView?.SetVisible(false);
            foreach (var target in hidden)
            {
                if (target != null) target.SetActive(true);
            }
            hidden.Clear();
        }

        void BuildProtocol()
        {
            protocol.Clear();
            if (trials == null || trials.Length == 0)
                protocol.AddRange(GabineteProtocol.Default());
            else
                foreach (var spec in trials) protocol.Add(spec.ToConfig());
            foreach (var config in protocol)
            {
                config.Rules = rules;
                config.Validate();
            }
        }

        void BeginTrial(int index)
        {
            var config = protocol[index];
            trial = new GabineteTrial(config, eventLogger, clock, ++trialCounter);
            cabinetView?.Show(config);
            trial.Begin();
            TrialStarted?.Invoke(trial);
        }

        /// <summary>The participant picked up a piece; <paramref name="yawDegrees"/> is its orientation in board space.</summary>
        public void OnPieceGrabbed(string pieceId, double yawDegrees)
        {
            if (trial == null || !trial.IsRunning) return;
            trial.PickUp(pieceId, yawDegrees);
        }

        /// <summary>The participant let go of a piece. The logic decides; the view then seats it or puts it back.</summary>
        public void OnPieceReleased(string pieceId, PiecePose pose)
        {
            if (trial == null || !trial.IsRunning) return;
            var outcome = trial.Release(pieceId, pose);
            if (!outcome.Accepted) return;

            if (outcome.Valid)
            {
                var completed = outcome.Completed;
                if (cabinetView == null)
                {
                    if (completed) CloseTrial();
                    return;
                }
                cabinetView.SeatPiece(pieceId, outcome.OpeningIndex, () =>
                {
                    if (completed) CloseTrial();
                });
            }
            else
            {
                cabinetView?.ReturnPiece(pieceId);
            }
        }

        void CloseTrial(string endReason = null)
        {
            if (trial == null) return;
            if (endReason != null) trial.End(endReason);
            cabinetView?.LockPieces();

            var summary = trial.BuildSummary();
            summaries.Add(summary);
            eventLogger.WriteJsonFile($"gabinete_trial_{trial.TrialIndex:D2}_summary.json", summary.ToJson(session));
            TrialClosed?.Invoke(summary);

            if (ended) return;
            if (trialCounter >= protocol.Count)
            {
                finished = true;
                Completed?.Invoke(this);
            }
            else
            {
                advanceRoutine = StartCoroutine(AdvanceAfterDelay(trialCounter));
            }
        }

        IEnumerator AdvanceAfterDelay(int nextIndex)
        {
            var until = clock.MonotonicSeconds + interTrialDelay;
            while (clock.MonotonicSeconds < until) yield return null;
            advanceRoutine = null;
            if (!ended) BeginTrial(nextIndex);
        }
    }
}
