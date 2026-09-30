using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PlayCT.Tasks.Cubo
{
    /// <summary>
    /// The cube body, held by the stabilizing hand. While any hand grips it, face actions are allowed;
    /// the cube itself never moves.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class CuboBodyHandle : MonoBehaviour
    {
        CuboTask task;
        XRSimpleInteractable interactable;

        public void Configure(CuboTask owner) => task = owner;

        void OnEnable()
        {
            if (interactable == null) interactable = GetComponent<XRSimpleInteractable>();
            interactable.firstSelectEntered.AddListener(OnFirstSelectEntered);
            interactable.lastSelectExited.AddListener(OnLastSelectExited);
        }

        void OnDisable()
        {
            if (interactable != null)
            {
                interactable.firstSelectEntered.RemoveListener(OnFirstSelectEntered);
                interactable.lastSelectExited.RemoveListener(OnLastSelectExited);
            }
            if (task != null) task.SetStabilized(false);
        }

        void OnFirstSelectEntered(UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs args) => task?.SetStabilized(true);

        void OnLastSelectExited(UnityEngine.XR.Interaction.Toolkit.SelectExitEventArgs args) => task?.SetStabilized(false);
    }
}
