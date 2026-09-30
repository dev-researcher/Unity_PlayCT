using UnityEngine;

namespace PlayCT.Research
{
    /// <summary>
    /// Connects the shared <see cref="GuideEngine"/> to the scene: it feeds it every event the EventLogger writes (so it follows
    /// the orchestrator and whichever task is running without touching them), logs guide messages through the same EventLogger
    /// and hands them to a presenter, normally the wooden guide card. <see cref="RequestHelp"/> is what the participant's help
    /// button calls.
    /// </summary>
    [DisallowMultipleComponent]
    public class GuideDirector : MonoBehaviour
    {
        [SerializeField] EventLogger eventLogger;
        [Tooltip("Component implementing IGuidePresenter (the physical guide card). Optional; messages are still logged.")]
        [SerializeField] MonoBehaviour presenter;
        [SerializeField] bool guideEnabled = true;

        GuideEngine engine;
        bool subscribed;

        public GuideEngine Engine
        {
            get
            {
                Build();
                return engine;
            }
        }

        void Awake() => Build();

        void OnEnable()
        {
            Build();
            if (subscribed || eventLogger == null) return;
            eventLogger.EventLogged += engine.OnEvent;
            subscribed = true;
        }

        void OnDisable()
        {
            if (!subscribed || eventLogger == null) return;
            eventLogger.EventLogged -= engine.OnEvent;
            subscribed = false;
        }

        void Build()
        {
            if (engine != null) return;
            if (eventLogger == null) eventLogger = FindFirstObjectByType<EventLogger>();
            if (eventLogger == null) eventLogger = new GameObject("EventLogger").AddComponent<EventLogger>();

            IGuidePresenter guidePresenter = null;
            if (presenter != null)
            {
                guidePresenter = presenter as IGuidePresenter;
                if (guidePresenter == null) Debug.LogError($"[GuideDirector] {presenter.name} does not implement IGuidePresenter.");
            }
            engine = new GuideEngine(eventLogger, new UnityClock(), guidePresenter, ResearchTaskCatalog.GuideProfiles())
            {
                Enabled = guideEnabled,
            };
        }

        /// <summary>The participant asked for help.</summary>
        public HelpResult RequestHelp() => Engine.RequestHelp();
    }
}
