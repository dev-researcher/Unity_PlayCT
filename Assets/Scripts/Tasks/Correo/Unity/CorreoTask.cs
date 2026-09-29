using System;
using System.Collections;
using System.Collections.Generic;
using PlayCT.Research;
using UnityEngine;

namespace PlayCT.Tasks.Correo
{
    /// <summary>
    /// Runs El Correo in the scene as a common <see cref="IExperimentTask"/>: the orchestrator starts it, it runs its trials in
    /// order, raises <see cref="Completed"/> after the last one, and cleans up in <see cref="EndTask"/>. It connects the pure
    /// <see cref="CorreoTrial"/> logic to the physical network and the XR handles and logs through the shared EventLogger. It
    /// never starts or ends the session, and it never chooses or moves anything for the participant.
    /// </summary>
    [DisallowMultipleComponent]
    public class CorreoTask : MonoBehaviour, IExperimentTask
    {
        public const string Id = CorreoTrial.TaskName;

        [Tooltip("Trials in order. Leave empty for the default protocol (one trial).")]
        [SerializeField] CorreoTrialSpec[] trials = new CorreoTrialSpec[0];
        [Tooltip("Component implementing ICorreoView (the physical postal network).")]
        [SerializeField] MonoBehaviour view;
        [Tooltip("Objects of other tasks that are switched off while this task uses the table.")]
        [SerializeField] GameObject[] hideWhileActive = new GameObject[0];
        [SerializeField] float interTrialDelay = 1.0f;
        [SerializeField] SessionManager session;
        [SerializeField] EventLogger eventLogger;

        readonly List<CorreoTrialConfig> protocol = new List<CorreoTrialConfig>();
        readonly List<GameObject> hidden = new List<GameObject>();
        readonly List<CorreoSummary> summaries = new List<CorreoSummary>();
        readonly IClock clock = new UnityClock();
        ICorreoView mapView;
        CorreoTrial trial;
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
        public CorreoTrial Trial => trial;
        public int TrialCount => protocol.Count;
        public IReadOnlyList<CorreoSummary> Summaries => summaries;

        public event Action<IExperimentTask> Completed;
        public event Action<CorreoTrial> TrialStarted;
        public event Action<CorreoSummary> TrialClosed;

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
                mapView = view as ICorreoView;
                if (mapView == null) Debug.LogError($"[CorreoTask] {view.name} does not implement ICorreoView.");
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
            mapView?.SetVisible(true);
            BeginTrial(0);
        }

        public void EndTask(string reason)
        {
            if (!started || ended) return;
            ended = true;
            if (advanceRoutine != null) StopCoroutine(advanceRoutine);
            advanceRoutine = null;
            if (trial != null && trial.IsRunning) CloseTrial(reason);

            mapView?.SetVisible(false);
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
                protocol.AddRange(CorreoProtocol.Default());
            else
                foreach (var spec in trials) protocol.Add(spec.ToConfig());
        }

        void BeginTrial(int index)
        {
            var config = protocol[index];
            trial = new CorreoTrial(config, eventLogger, clock, ++trialCounter);
            mapView?.Show(config, trial.State);
            trial.Begin();
            TrialStarted?.Invoke(trial);
        }

        /// <summary>The participant touched a settlement.</summary>
        public void TapSettlement(Settlement settlement)
        {
            if (!CanAct()) return;
            if (trial.TapSettlement(settlement)) ShowSelection();
        }

        /// <summary>The participant touched a package.</summary>
        public void TogglePackage(CorreoPackage package)
        {
            if (!CanAct()) return;
            if (trial.TogglePackage(package)) ShowSelection();
        }

        public void ClearSelection()
        {
            if (!CanAct()) return;
            if (trial.ClearSelection()) ShowSelection();
        }

        /// <summary>The participant confirmed the shipment. The logic decides; the view then carries the packages or leaves them where they are.</summary>
        public void Confirm()
        {
            if (!CanAct()) return;
            var outcome = trial.Confirm();
            if (!outcome.Accepted) return;
            ShowSelection();

            if (!outcome.Valid)
            {
                mapView?.ShowNotShipped(outcome.Shipment);
                return;
            }

            var completed = outcome.Completed;
            if (mapView == null)
            {
                if (completed) CloseTrial();
                return;
            }
            mapView.AnimateShipment(outcome.Shipment, trial.State, () =>
            {
                if (completed) CloseTrial();
            });
        }

        bool CanAct() => trial != null && trial.IsRunning && (mapView == null || !mapView.IsAnimating);

        void ShowSelection() => mapView?.ShowSelection(trial.SelectedSource, trial.SelectedDestination, trial.SelectedPackages);

        void CloseTrial(string endReason = null)
        {
            if (trial == null) return;
            if (endReason != null) trial.End(endReason);

            var summary = trial.BuildSummary();
            summaries.Add(summary);
            eventLogger.WriteJsonFile($"correo_trial_{trial.TrialIndex:D2}_summary.json", summary.ToJson(session));
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
