using System;
using PlayCT.Research;

namespace PlayCT.App
{
    /// <summary>What the application needs to know while an experimental task waits for the participant (instructions, completion screen).</summary>
    public interface IExperimentTaskGate
    {
        /// <summary>The orchestrator started the task: show its instructions. The task itself has not started yet.</summary>
        void TaskOffered(string taskId);

        /// <summary>The participant pressed Comenzar: the real task is about to start.</summary>
        void InnerStarting(string taskId);

        /// <summary>The real task reported completion: show the completion screen.</summary>
        void InnerCompleted(string taskId);

        /// <summary>The orchestrator ended the task (completed, skipped or aborted): tidy the scene.</summary>
        void TaskClosed(string taskId);
    }

    /// <summary>
    /// Sits between the existing <see cref="ExperimentSequencer"/> and one real task so the experiment can show the task's
    /// instructions before it starts and a completion screen after it ends. It is an <see cref="IExperimentTask"/> with the real
    /// task's ID: the orchestrator starts it, it lets the participant begin, it starts the real task with the same condition, and
    /// it reports completion to the orchestrator only when the participant continues. The sequence, the logging and the task
    /// itself are the existing ones; nothing is duplicated.
    /// </summary>
    public sealed class GatedExperimentTask : IExperimentTask
    {
        readonly IExperimentTask inner;
        readonly IExperimentTaskGate gate;
        ExperimentCondition condition;
        bool offered;
        bool innerStarted;
        bool innerDone;
        bool released;

        public string TaskId => inner.TaskId;
        public bool IsRunning => offered && !released;
        public bool IsCompleted => released;
        public bool InnerStarted => innerStarted;
        public bool InnerCompleted => innerDone;

        public event Action<IExperimentTask> Completed;

        public GatedExperimentTask(IExperimentTask inner, IExperimentTaskGate gate)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.gate = gate ?? throw new ArgumentNullException(nameof(gate));
        }

        public void StartTask(ExperimentCondition experimentCondition)
        {
            condition = experimentCondition;
            offered = true;
            innerStarted = false;
            innerDone = false;
            released = false;
            gate.TaskOffered(TaskId);
        }

        /// <summary>The participant pressed Comenzar tarea.</summary>
        public void BeginInner()
        {
            if (!offered || innerStarted || released) return;
            innerStarted = true;
            inner.Completed += OnInnerCompleted;
            gate.InnerStarting(TaskId);
            inner.StartTask(condition);
            if (inner.IsCompleted) OnInnerCompleted(inner);
        }

        /// <summary>The participant pressed Continuar after the completion screen.</summary>
        public void Release()
        {
            if (!innerDone || released) return;
            released = true;
            Completed?.Invoke(this);
        }

        public void EndTask(string reason)
        {
            if (!offered) return;
            offered = false;
            if (innerStarted)
            {
                inner.Completed -= OnInnerCompleted;
                inner.EndTask(reason);
            }
            innerStarted = false;
            gate.TaskClosed(TaskId);
        }

        void OnInnerCompleted(IExperimentTask task)
        {
            if (innerDone) return;
            innerDone = true;
            inner.Completed -= OnInnerCompleted;
            gate.InnerCompleted(TaskId);
        }
    }
}
