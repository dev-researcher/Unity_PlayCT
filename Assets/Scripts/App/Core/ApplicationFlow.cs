using System;
using System.Collections.Generic;
using PlayCT.Research;

namespace PlayCT.App
{
    public enum AppState
    {
        Welcome,
        GameSelection,
        GameInstructions,
        FreePlay,
        GameCompleted,
        ExperimentIntroduction,
        ParticipantSetup,
        ExperimentReady,
        ExperimentTaskInstructions,
        ExperimentRunning,
        ExperimentTaskCompleted,
        ExperimentCompleted,
    }

    /// <summary>What the flow asks of the scene. Implemented by the Unity controller, which owns the existing managers and tasks.</summary>
    public interface IAppHost
    {
        /// <summary>Free play: starts one game on its own. No session, condition or log is involved.</summary>
        void StartFreePlayGame(string taskId);

        void StopFreePlayGame(string taskId, string reason);

        /// <summary>Experiment: starts a new session for the participant and returns its ID.</summary>
        string PrepareSession(string participantId);

        /// <summary>Experiment: turns research logging on and begins the experiment through the TaskOrchestrator. False if it could not start.</summary>
        bool StartExperiment();

        /// <summary>Experiment: the participant pressed Comenzar tarea.</summary>
        void StartExperimentTask(string taskId);

        /// <summary>Experiment: the participant pressed Continuar after a task; the orchestrator moves on.</summary>
        void AcknowledgeExperimentTask(string taskId);

        /// <summary>Experiment: leaves the experiment (logging off) and clears the scene.</summary>
        void EndExperiment();
    }

    /// <summary>
    /// The application's screens and how a participant moves between them. It is a pure state machine: it decides which screen is
    /// shown and what happens on each button, and asks <see cref="IAppHost"/> to do the real work with the existing session,
    /// condition, orchestrator, logger and tasks. Free play and the experiment are separate paths that reuse the same games.
    /// </summary>
    public sealed class ApplicationFlow
    {
        public const string ExperimentTaskName = ExperimentSequencer.TaskName;

        readonly IAppHost host;
        readonly IResearchEventSink sink;

        public AppState State { get; private set; } = AppState.Welcome;
        public string SelectedGameId { get; private set; }
        public string ParticipantId { get; private set; } = string.Empty;
        public string SessionId { get; private set; }
        public string StartError { get; private set; }

        public string ExperimentTaskId { get; private set; }
        public int ExperimentTaskIndex { get; private set; }
        public int ExperimentTaskCount { get; private set; }

        /// <summary>Raised after the screen changes.</summary>
        public event Action<AppState> StateChanged;

        /// <summary>Raised when the same screen has new data (participant ID typed, error text).</summary>
        public event Action DataChanged;

        public ApplicationFlow(IAppHost host, IResearchEventSink sink)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        public bool IsParticipantIdValid => ParticipantIdRules.IsValid(ParticipantId);

        public bool InExperiment =>
            State == AppState.ExperimentReady || State == AppState.ExperimentTaskInstructions || State == AppState.ExperimentRunning ||
            State == AppState.ExperimentTaskCompleted || State == AppState.ExperimentCompleted;

        /// <summary>True while the screen is a menu the participant points at; false while a task is being played.</summary>
        public bool MenuVisible => State != AppState.FreePlay && State != AppState.ExperimentRunning;

        // ---- Welcome ----------------------------------------------------------------------------------------------

        public void ChooseFreePlay()
        {
            if (State == AppState.Welcome) Go(AppState.GameSelection);
        }

        public void ChooseExperiment()
        {
            if (State != AppState.Welcome) return;
            StartError = null;
            Go(AppState.ExperimentIntroduction);
        }

        // ---- Free play --------------------------------------------------------------------------------------------

        public void SelectGame(string taskId)
        {
            if (State != AppState.GameSelection || !GameCatalog.TryGet(taskId, out _)) return;
            SelectedGameId = taskId;
            Go(AppState.GameInstructions);
        }

        public void BeginSelectedGame()
        {
            if (State != AppState.GameInstructions || SelectedGameId == null) return;
            Go(AppState.FreePlay);
            host.StartFreePlayGame(SelectedGameId);
        }

        /// <summary>The participant leaves a game that is still running (the Salir button).</summary>
        public void LeaveGame()
        {
            if (State != AppState.FreePlay) return;
            host.StopFreePlayGame(SelectedGameId, "exited");
            Go(AppState.GameSelection);
        }

        /// <summary>The host reports that the free-play game reached its completion.</summary>
        public void NotifyGameCompleted(string taskId)
        {
            if (State != AppState.FreePlay || taskId != SelectedGameId) return;
            Go(AppState.GameCompleted);
        }

        public void BackToGames()
        {
            if (State == AppState.GameCompleted) host.StopFreePlayGame(SelectedGameId, "completed");
            else if (State != AppState.GameInstructions) return;
            Go(AppState.GameSelection);
        }

        public void BackToStart()
        {
            switch (State)
            {
                case AppState.GameCompleted:
                    host.StopFreePlayGame(SelectedGameId, "completed");
                    break;
                case AppState.GameSelection:
                case AppState.ExperimentIntroduction:
                case AppState.ParticipantSetup:
                case AppState.ExperimentReady:
                    break;
                default:
                    return;
            }
            Go(AppState.Welcome);
        }

        // ---- Experiment: introduction and participant --------------------------------------------------------------

        public void ContinueFromIntroduction()
        {
            if (State == AppState.ExperimentIntroduction) Go(AppState.ParticipantSetup);
        }

        public void BackToIntroduction()
        {
            if (State == AppState.ParticipantSetup) Go(AppState.ExperimentIntroduction);
        }

        public void SetParticipantId(string text)
        {
            ParticipantId = ParticipantIdRules.Sanitize(text);
            DataChanged?.Invoke();
        }

        public void AppendParticipantCharacter(char c)
        {
            if (State != AppState.ParticipantSetup || !ParticipantIdRules.IsAllowed(c) || ParticipantId.Length >= ParticipantIdRules.MaxLength) return;
            ParticipantId += char.ToUpperInvariant(c);
            DataChanged?.Invoke();
        }

        public void BackspaceParticipant()
        {
            if (State != AppState.ParticipantSetup || ParticipantId.Length == 0) return;
            ParticipantId = ParticipantId.Substring(0, ParticipantId.Length - 1);
            DataChanged?.Invoke();
        }

        public void ClearParticipant()
        {
            if (State != AppState.ParticipantSetup || ParticipantId.Length == 0) return;
            ParticipantId = string.Empty;
            DataChanged?.Invoke();
        }

        /// <summary>Accepts the participant code and creates the session through the existing SessionManager.</summary>
        public void ConfirmParticipant()
        {
            if (State != AppState.ParticipantSetup || !IsParticipantIdValid) return;
            SessionId = host.PrepareSession(ParticipantIdRules.Normalize(ParticipantId));
            StartError = null;
            Go(AppState.ExperimentReady);
        }

        public void BackToParticipantSetup()
        {
            if (State == AppState.ExperimentReady) Go(AppState.ParticipantSetup);
        }

        // ---- Experiment: run ---------------------------------------------------------------------------------------

        public void BeginExperiment()
        {
            if (State != AppState.ExperimentReady) return;
            StartError = null;
            if (!host.StartExperiment())
            {
                StartError = "No se pudo iniciar el experimento. Avisa al equipo de investigación.";
                DataChanged?.Invoke();
            }
        }

        /// <summary>The orchestrator started an experimental task; its instructions are shown before it begins.</summary>
        public void OfferExperimentTask(string taskId, int index, int count)
        {
            ExperimentTaskId = taskId;
            ExperimentTaskIndex = index;
            ExperimentTaskCount = count;
            sink.Log(ExperimentEvent("task_instructions_shown", taskId, index));
            Go(AppState.ExperimentTaskInstructions);
        }

        public void BeginExperimentTask()
        {
            if (State != AppState.ExperimentTaskInstructions) return;
            sink.Log(ExperimentEvent("task_begin_confirmed", ExperimentTaskId, ExperimentTaskIndex));
            Go(AppState.ExperimentRunning);
            host.StartExperimentTask(ExperimentTaskId);
        }

        public void NotifyExperimentTaskCompleted(string taskId)
        {
            if (State != AppState.ExperimentRunning || taskId != ExperimentTaskId) return;
            sink.Log(ExperimentEvent("task_completion_shown", taskId, ExperimentTaskIndex));
            Go(AppState.ExperimentTaskCompleted);
        }

        public void ContinueAfterTask()
        {
            if (State != AppState.ExperimentTaskCompleted) return;
            sink.Log(ExperimentEvent("task_continue_confirmed", ExperimentTaskId, ExperimentTaskIndex));
            host.AcknowledgeExperimentTask(ExperimentTaskId);
        }

        /// <summary>The orchestrator finished the experiment. Completed shows the closing screen; anything else goes back to the start.</summary>
        public void NotifyExperimentFinished(bool completed)
        {
            if (!InExperiment) return;
            if (completed)
            {
                Go(AppState.ExperimentCompleted);
                return;
            }
            host.EndExperiment();
            ResetExperimentData();
            Go(AppState.Welcome);
        }

        public void FinishExperiment()
        {
            if (State != AppState.ExperimentCompleted) return;
            host.EndExperiment();
            ResetExperimentData();
            Go(AppState.Welcome);
        }

        void ResetExperimentData()
        {
            ParticipantId = string.Empty;
            SessionId = null;
            StartError = null;
            ExperimentTaskId = null;
            ExperimentTaskIndex = 0;
            ExperimentTaskCount = 0;
        }

        void Go(AppState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }

        static ResearchEvent ExperimentEvent(string type, string taskId, int index) =>
            new ResearchEvent(ExperimentTaskName, type).Add("task_id", taskId).Add("task_index", index);
    }
}
