using System;

namespace PlayCT.Research
{
    /// <summary>
    /// What the orchestrator needs from an experimental task, and nothing more. A task keeps its own rules,
    /// metrics and event logging; it is started with the experimental condition, raises <see cref="Completed"/>
    /// once, and is told to clean up with <see cref="EndTask"/>.
    /// </summary>
    public interface IExperimentTask
    {
        /// <summary>Stable identifier used in the task sequence and in logs (for example "Hanoi").</summary>
        string TaskId { get; }

        bool IsRunning { get; }
        bool IsCompleted { get; }

        event Action<IExperimentTask> Completed;

        void StartTask(ExperimentCondition condition);

        /// <summary>Stops the task and releases anything it holds. Must be safe to call after completion.</summary>
        void EndTask(string reason);
    }
}
