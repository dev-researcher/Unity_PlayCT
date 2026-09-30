using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PlayCT.Research
{
    /// <summary>Touch target of the wooden Ayuda button. A select from the existing XR interactors asks the guide for help; it holds no logic.</summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class GuideHelpHandle : MonoBehaviour
    {
        GuideDirector director;
        XRSimpleInteractable interactable;

        public void Configure(GuideDirector owner) => director = owner;

        void OnEnable()
        {
            if (interactable == null) interactable = GetComponent<XRSimpleInteractable>();
            interactable.firstSelectEntered.AddListener(OnFirstSelectEntered);
        }

        void OnDisable()
        {
            if (interactable != null) interactable.firstSelectEntered.RemoveListener(OnFirstSelectEntered);
        }

        void OnFirstSelectEntered(UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs args) => Press();

        public void Press() => director?.RequestHelp();
    }
}
