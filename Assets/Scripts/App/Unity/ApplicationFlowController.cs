using System.Collections;
using System.Collections.Generic;
using PlayCT.Research;
using PlayCT.Tasks.Correo;
using PlayCT.Tasks.Cubo;
using PlayCT.Tasks.Gabinete;
using PlayCT.Tasks.Hanoi;
using UnityEngine;

namespace PlayCT.App
{
    /// <summary>
    /// The application layer of the Laboratory scene. It owns the screens (welcome, games, experiment) and connects them to the
    /// systems that already exist: free play starts one task directly with research logging off, and the experiment creates the
    /// session through <see cref="SessionManager"/>, turns logging on and runs the normal <see cref="TaskOrchestrator"/> sequence,
    /// with each task wrapped so the participant reads its instructions before it starts and sees a completion screen after it.
    /// It adds no rules, sessions, conditions or logs of its own.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    [DisallowMultipleComponent]
    public class ApplicationFlowController : MonoBehaviour, IAppHost, IExperimentTaskGate
    {
        [Header("Existing systems")]
        [SerializeField] TaskOrchestrator orchestrator;
        [SerializeField] SessionManager session;
        [SerializeField] EventLogger eventLogger;
        [Tooltip("The task components (IExperimentTask) the application can offer, in any order.")]
        [SerializeField] MonoBehaviour[] games = new MonoBehaviour[0];
        [Tooltip("The wooden guide card. It is shown only during the experiment.")]
        [SerializeField] GameObject guideRoot;

        [Header("XR rig")]
        [SerializeField] Transform xrOrigin;
        [SerializeField] Transform trackingSpace;
        [SerializeField] Transform head;
        [SerializeField] Transform leftController;
        [SerializeField] Transform rightController;

        [Header("Menu look")]
        [SerializeField] Material surfaceMaterial;
        [SerializeField] Material oakMaterial;
        [SerializeField] float panelDistance = 1.5f;
        [SerializeField] float panelBelowEyes = 0.05f;
        [SerializeField] float completionDelaySeconds = 0.8f;

        readonly Dictionary<string, IExperimentTask> rawTasks = new Dictionary<string, IExperimentTask>();
        readonly Dictionary<string, GatedExperimentTask> gatedTasks = new Dictionary<string, GatedExperimentTask>();
        readonly Dictionary<string, MonoBehaviour> taskBehaviours = new Dictionary<string, MonoBehaviour>();

        ApplicationFlow flow;
        VrMenuStyle style;
        UnityTextMeasure measure;
        VrMenuPanel panel;
        VrMenuPanel exitPanel;
        VrMenuPointer pointer;
        IExperimentTask freePlayTask;
        Coroutine completion;
        bool panelPlaced;

        public ApplicationFlow Flow => flow;
        public VrMenuPanel Panel => panel;

        void Awake()
        {
            if (eventLogger == null) eventLogger = FindFirstObjectByType<EventLogger>();
            if (session == null) session = FindFirstObjectByType<SessionManager>();
            if (orchestrator == null) orchestrator = FindFirstObjectByType<TaskOrchestrator>();
            if (head == null && Camera.main != null) head = Camera.main.transform;

            // Nothing is recorded until an experiment starts.
            eventLogger.Recording = false;

            foreach (var behaviour in games)
            {
                if (behaviour == null) continue;
                if (!(behaviour is IExperimentTask task))
                {
                    Debug.LogError($"[ApplicationFlowController] {behaviour.name} does not implement IExperimentTask.");
                    continue;
                }
                rawTasks[task.TaskId] = task;
                taskBehaviours[task.TaskId] = behaviour;
                var gated = new GatedExperimentTask(task, this);
                gatedTasks[task.TaskId] = gated;
                orchestrator.Register(gated);
            }

            flow = new ApplicationFlow(this, eventLogger);
        }

        void Start()
        {
            style = new VrMenuStyle(surfaceMaterial, oakMaterial);
            measure = new UnityTextMeasure(style);
            panel = VrMenuPanel.Create("Menu_Panel", null, style);
            exitPanel = VrMenuPanel.Create("Menu_Exit", null, style);
            pointer = new VrMenuPointer(style, transform, trackingSpace, leftController, rightController);

            HideAllGames();
            if (guideRoot != null) guideRoot.SetActive(false);

            flow.StateChanged += OnStateChanged;
            flow.DataChanged += OnDataChanged;
            orchestrator.ExperimentFinished += OnExperimentFinished;

            RecenterRig();
            OnStateChanged(flow.State);
            StartCoroutine(RecenterWhileWelcome());
        }

        void OnDestroy()
        {
            if (flow != null)
            {
                flow.StateChanged -= OnStateChanged;
                flow.DataChanged -= OnDataChanged;
            }
            if (orchestrator != null) orchestrator.ExperimentFinished -= OnExperimentFinished;
            pointer?.Dispose();
        }

        void Update() => pointer?.Tick();

        // ---- Screens -----------------------------------------------------------------------------------------------

        void OnStateChanged(AppState state)
        {
            pointer.Active = flow.MenuVisible || state == AppState.FreePlay;
            pointer.AlwaysShowRay = flow.MenuVisible;
            if (flow.MenuVisible)
            {
                exitPanel.Hide();
                if (state == AppState.Welcome)
                {
                    RecenterRig();
                    PlacePanel();
                }
                ShowScreen();
            }
            else
            {
                panel.Hide();
                if (state == AppState.FreePlay) ShowExitButton();
                else exitPanel.Hide();
            }
        }

        void OnDataChanged()
        {
            if (flow.MenuVisible) ShowScreen();
        }

        void ShowScreen()
        {
            var spec = ScreenCatalog.Build(flow);
            if (spec == null) return;
            var layout = PanelLayout.Build(spec, measure);
            if (layout.Overflow) Debug.LogWarning($"[ApplicationFlowController] The '{flow.State}' screen does not fit its panel.");
            EnsurePanelInView();
            panel.Show(layout);
        }

        void ShowExitButton()
        {
            var layout = new PanelLayoutResult { Width = 0.56f, Height = 0.22f };
            var spec = new ButtonSpec("exit", "Salir al menú", ButtonKind.Secondary, flow.LeaveGame);
            var item = new ButtonItem { Spec = spec, X = 0f, Y = 0f, Width = 0.44f, Height = 0.13f };
            item.Labels.Add(new TextItem { Text = spec.Label, X = 0f, Y = 0f, Em = 0.05f, Align = TextAlign.Center, Role = TextRole.ButtonLabel });
            layout.Buttons.Add(item);

            var origin = head.position;
            var forward = FlatForward();
            var right = Vector3.Cross(Vector3.up, forward);
            var position = origin + forward * 0.7f - right * 0.8f;
            position.y = origin.y - 0.4f;
            var away = Vector3.ProjectOnPlane(position - origin, Vector3.up).normalized;
            exitPanel.PlaceAt(position, Quaternion.LookRotation(away, Vector3.up) * Quaternion.Euler(20f, 0f, 0f));
            exitPanel.Show(layout);
        }

        Vector3 FlatForward()
        {
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(xrOrigin != null ? xrOrigin.forward : Vector3.forward, Vector3.up);
            return forward.normalized;
        }

        void EnsurePanelInView()
        {
            if (panelPlaced)
            {
                var toPanel = panel.transform.position - head.position;
                if (Vector3.Angle(head.forward, toPanel) <= 30f && toPanel.magnitude > 1.0f && toPanel.magnitude < 2.1f) return;
            }
            PlacePanel();
        }

        void PlacePanel()
        {
            var forward = FlatForward();
            var position = head.position + forward * panelDistance;
            position.y = Mathf.Clamp(head.position.y - panelBelowEyes, 1.05f, 1.75f);
            panel.PlaceAt(position, Quaternion.LookRotation(forward, Vector3.up));
            panelPlaced = true;
        }

        /// <summary>
        /// Moves the XR Origin so the participant's head is over the laboratory's start point and looks toward the table, whatever
        /// the physical position and direction the headset was started in. Only the origin moves; the table and tasks do not.
        /// </summary>
        void RecenterRig()
        {
            if (xrOrigin == null || head == null) return;
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) return;
            var angle = Vector3.SignedAngle(forward, Vector3.forward, Vector3.up);
            xrOrigin.RotateAround(head.position, Vector3.up, angle);
            var offset = new Vector3(-head.position.x, 0f, -head.position.z);
            xrOrigin.position += offset;
        }

        IEnumerator RecenterWhileWelcome()
        {
            foreach (var wait in new[] { 0.6f, 1.4f })
            {
                yield return new WaitForSecondsRealtime(wait);
                if (flow.State != AppState.Welcome) yield break;
                RecenterRig();
                PlacePanel();
                ShowScreen();
            }
        }

        // ---- IAppHost: free play -----------------------------------------------------------------------------------

        public void StartFreePlayGame(string taskId)
        {
            if (!rawTasks.TryGetValue(taskId, out var task)) return;
            eventLogger.Recording = false;
            HideAllGames();
            StopFreePlayTask("replaced");

            freePlayTask = task;
            task.Completed += OnFreePlayTaskCompleted;
            SetGameVisible(taskId, true);
            task.StartTask(ExperimentCondition.Static);
            if (task.IsCompleted) OnFreePlayTaskCompleted(task);
        }

        public void StopFreePlayGame(string taskId, string reason)
        {
            CancelCompletion();
            StopFreePlayTask(reason);
            HideAllGames();
        }

        void StopFreePlayTask(string reason)
        {
            if (freePlayTask == null) return;
            var task = freePlayTask;
            freePlayTask = null;
            task.Completed -= OnFreePlayTaskCompleted;
            task.EndTask(reason);
        }

        void OnFreePlayTaskCompleted(IExperimentTask task)
        {
            if (task != freePlayTask) return;
            var id = task.TaskId;
            ShowCompletionAfterDelay(id, () => flow.NotifyGameCompleted(id));
        }

        // ---- IAppHost: experiment ----------------------------------------------------------------------------------

        public string PrepareSession(string participantId)
        {
            // The condition is not chosen here: it stays whatever the researcher configured (session_config.json or the scene).
            session.BeginSession(participantId);
            return session.SessionId;
        }

        public bool StartExperiment()
        {
            HideAllGames();
            if (guideRoot != null) guideRoot.SetActive(true);
            eventLogger.Recording = true;
            if (orchestrator.BeginExperiment()) return true;

            eventLogger.Recording = false;
            if (guideRoot != null) guideRoot.SetActive(false);
            return false;
        }

        public void StartExperimentTask(string taskId)
        {
            if (gatedTasks.TryGetValue(taskId, out var gated)) gated.BeginInner();
        }

        public void AcknowledgeExperimentTask(string taskId)
        {
            CancelCompletion();
            if (gatedTasks.TryGetValue(taskId, out var gated)) gated.Release();
        }

        public void EndExperiment()
        {
            CancelCompletion();
            orchestrator.Abort("application_reset");
            eventLogger.Recording = false;
            if (guideRoot != null) guideRoot.SetActive(false);
            HideAllGames();
        }

        void OnExperimentFinished(ExperimentSequencer sequencer)
        {
            flow.NotifyExperimentFinished(sequencer.State == ExperimentState.Completed);
        }

        // ---- IExperimentTaskGate -----------------------------------------------------------------------------------

        public void TaskOffered(string taskId)
        {
            HideAllGames();
            var sequencer = orchestrator.Sequencer;
            flow.OfferExperimentTask(taskId, sequencer != null ? sequencer.CurrentIndex : 0, sequencer != null ? sequencer.Sequence.Count : 1);
        }

        public void InnerStarting(string taskId)
        {
            HideAllGames();
            SetGameVisible(taskId, true);
        }

        public void InnerCompleted(string taskId)
        {
            ShowCompletionAfterDelay(taskId, () => flow.NotifyExperimentTaskCompleted(taskId));
        }

        public void TaskClosed(string taskId)
        {
            HideGame(taskId);
        }

        // ---- Games' visuals ----------------------------------------------------------------------------------------

        void ShowCompletionAfterDelay(string taskId, System.Action notify)
        {
            CancelCompletion();
            completion = StartCoroutine(CompletionRoutine(taskId, notify));
        }

        IEnumerator CompletionRoutine(string taskId, System.Action notify)
        {
            yield return new WaitForSecondsRealtime(completionDelaySeconds);
            completion = null;
            HideGame(taskId);
            notify();
        }

        void CancelCompletion()
        {
            if (completion == null) return;
            StopCoroutine(completion);
            completion = null;
        }

        void HideAllGames()
        {
            foreach (var id in taskBehaviours.Keys) HideGame(id);
        }

        void HideGame(string taskId) => SetGameVisible(taskId, false);

        /// <summary>Hanoi is shown by activating its object; the other three show and hide their own view through their task.</summary>
        void SetGameVisible(string taskId, bool visible)
        {
            if (!taskBehaviours.TryGetValue(taskId, out var behaviour) || behaviour == null) return;
            if (taskId == HanoiTrial.TaskName)
            {
                behaviour.gameObject.SetActive(visible);
                return;
            }
            if (visible) return;
            foreach (var component in behaviour.GetComponents<MonoBehaviour>())
            {
                if (component is ICuboView cubo) cubo.SetVisible(false);
                else if (component is IGabineteView gabinete) gabinete.SetVisible(false);
                else if (component is ICorreoView correo) correo.SetVisible(false);
            }
        }
    }
}
