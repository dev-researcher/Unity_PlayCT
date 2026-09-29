using System;
using UnityEngine;

namespace PlayCT.Research
{
    /// <summary>
    /// Typed view of the experimental condition. The value itself stays in the SessionManager (so it is logged
    /// with every event and can come from session_config.json); this only parses and validates it.
    /// </summary>
    [DisallowMultipleComponent]
    public class ConditionManager : MonoBehaviour
    {
        [SerializeField] SessionManager session;

        public SessionManager Session
        {
            get
            {
                if (session == null) session = FindFirstObjectByType<SessionManager>();
                if (session == null) session = new GameObject("SessionManager").AddComponent<SessionManager>();
                return session;
            }
            set => session = value;
        }

        public string RawCondition => Session.Condition;
        public bool IsValid => ExperimentConditions.TryParse(Session.Condition, out _);

        /// <summary>The current condition. Throws if the session's condition text is not a supported condition.</summary>
        public ExperimentCondition Current
        {
            get
            {
                if (!TryGetCurrent(out var condition))
                    throw new InvalidOperationException($"Unsupported experimental condition '{Session.Condition}'. Use Static or PreAdapted.");
                return condition;
            }
        }

        public event Action<ExperimentCondition> Changed;

        public bool TryGetCurrent(out ExperimentCondition condition) => ExperimentConditions.TryParse(Session.Condition, out condition);

        public void Set(ExperimentCondition condition)
        {
            Session.SetCondition(ExperimentConditions.ToLabel(condition));
            Changed?.Invoke(condition);
        }

        void Awake()
        {
            // Store the canonical name (for example turn the legacy "baseline" into "Static") so logs use one spelling.
            if (TryGetCurrent(out var condition) && Session.Condition != ExperimentConditions.ToLabel(condition))
                Session.SetCondition(ExperimentConditions.ToLabel(condition));
        }
    }
}
