using System;
using System.Collections;
using System.Collections.Generic;
using PlayCT.Research;
using UnityEngine;

namespace PlayCT.Tasks.Cubo
{
    /// <summary>
    /// Runs the Cubo de Relaciones task in the scene as a common <see cref="IExperimentTask"/>: the orchestrator starts
    /// it, it runs its mini-task trials in order, raises <see cref="Completed"/> after the last one, and cleans up in
    /// <see cref="EndTask"/>. It connects the pure <see cref="CuboTrial"/> logic to the visual cube and the XR handles and
    /// logs through the shared EventLogger.
    /// </summary>
    [DisallowMultipleComponent]
    public class CuboTask : MonoBehaviour, IExperimentTask
    {
        public const string Id = CuboTrial.TaskName;

        [Tooltip("Mini-task trials in order. Leave empty for the default protocol (Cruz clara, Corregir una pieza, Elegir una secuencia, Qué permanece).")]
        [SerializeField] CuboTrialSpec[] trials = new CuboTrialSpec[0];
        [Tooltip("Component implementing ICuboView (the visual cube).")]
        [SerializeField] MonoBehaviour view;
        [Tooltip("Objects of other tasks that are switched off while this task uses the table.")]
        [SerializeField] GameObject[] hideWhileActive = new GameObject[0];
        [SerializeField] float interTrialDelay = 1.0f;
        [SerializeField] TwistGestureSettings gesture = TwistGestureSettings.Default;
        [SerializeField] SessionManager session;
        [SerializeField] EventLogger eventLogger;

        readonly List<CuboTrialConfig> protocol = new List<CuboTrialConfig>();
        readonly List<GameObject> hidden = new List<GameObject>();
        readonly List<CuboSummary> summaries = new List<CuboSummary>();
        readonly IClock clock = new UnityClock();
        ICuboView cubeView;
        CuboTrial trial;
        Coroutine advanceRoutine;
        int trialCounter;
        bool initialized;
        bool started;
        bool finished;
        bool ended;
        bool stabilized;

        public string TaskId => Id;
        public bool IsRunning => started && !ended && !finished;
        public bool IsCompleted => finished;
        public ExperimentCondition? Condition { get; private set; }
        public CuboTrial Trial => trial;
        public int TrialCount => protocol.Count;
        public IReadOnlyList<CuboSummary> Summaries => summaries;

        public event Action<IExperimentTask> Completed;
        public event Action<CuboTrial> TrialStarted;
        public event Action<CuboSummary> TrialClosed;

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
                cubeView = view as ICuboView;
                if (cubeView == null) Debug.LogError($"[CuboTask] {view.name} does not implement ICuboView.");
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
            cubeView?.SetVisible(true);
            BeginTrial(0);
        }

        public void EndTask(string reason)
        {
            if (!started || ended) return;
            ended = true;
            if (advanceRoutine != null) StopCoroutine(advanceRoutine);
            advanceRoutine = null;
            if (trial != null && trial.IsRunning) CloseTrial(reason);

            cubeView?.SetVisible(false);
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
            {
                protocol.AddRange(CuboProtocol.Default());
                return;
            }
            foreach (var spec in trials) protocol.Add(spec.ToConfig());
        }

        void BeginTrial(int index)
        {
            var config = protocol[index];
            trial = new CuboTrial(config, eventLogger, clock, ++trialCounter);
            cubeView?.Show(config.InitialState);
            trial.Begin();
            trial.SetStabilized(stabilized);
            TrialStarted?.Invoke(trial);
        }

        /// <summary>The stabilizing hand started or stopped holding the cube.</summary>
        public void SetStabilized(bool held)
        {
            stabilized = held;
            if (trial != null && trial.IsRunning) trial.SetStabilized(held);
        }

        /// <summary>The selecting hand touched a face without twisting.</summary>
        public void SelectFace(CubeFace face)
        {
            if (!CanAct()) return;
            var outcome = trial.SelectFace(face);
            if (outcome.Completed) CloseTrial();
        }

        /// <summary>The selecting hand let go of a face after moving; the movement decides tap, quarter turn, or nothing.</summary>
        public void ReleaseFace(CubeFace face, float signedAngleDegrees, float travel, float startRadius)
        {
            switch (TwistGesture.Classify(signedAngleDegrees, travel, startRadius, gesture))
            {
                case GestureKind.Tap:
                    SelectFace(face);
                    break;
                case GestureKind.Clockwise:
                    Rotate(new CubeMove(face, CubeTurn.Clockwise));
                    break;
                case GestureKind.CounterClockwise:
                    Rotate(new CubeMove(face, CubeTurn.CounterClockwise));
                    break;
            }
        }

        public void Rotate(CubeMove move)
        {
            if (!CanAct()) return;
            var outcome = trial.TryRotate(move);
            if (!outcome.Accepted || !outcome.StateChanged) return;

            var completed = outcome.Completed;
            if (cubeView == null)
            {
                if (completed) CloseTrial();
                return;
            }
            cubeView.AnimateTurn(move, trial.State, () =>
            {
                if (completed) CloseTrial();
            });
        }

        bool CanAct() => trial != null && trial.IsRunning && (cubeView == null || !cubeView.IsAnimating);

        void CloseTrial(string endReason = null)
        {
            if (trial == null) return;
            if (endReason != null) trial.End(endReason);

            var summary = trial.BuildSummary();
            summaries.Add(summary);
            eventLogger.WriteJsonFile($"cubo_trial_{trial.TrialIndex:D2}_summary.json", summary.ToJson(session));
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
