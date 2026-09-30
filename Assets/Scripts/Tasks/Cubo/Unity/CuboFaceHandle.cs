using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PlayCT.Tasks.Cubo
{
    /// <summary>
    /// Touch target on one face. The selecting hand grips the face, moves, and lets go: the start and end points
    /// (in the cube's own space) are turned into a tap or a twist and handed to the task, which applies the rules.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class CuboFaceHandle : MonoBehaviour
    {
        CubeFace face;
        CuboTask task;
        Transform cubeRoot;
        XRSimpleInteractable interactable;
        Vector3 startLocal;
        bool selecting;

        public void Configure(CubeFace faceToHandle, CuboTask owner, Transform root)
        {
            face = faceToHandle;
            task = owner;
            cubeRoot = root;
        }

        void OnEnable()
        {
            if (interactable == null) interactable = GetComponent<XRSimpleInteractable>();
            interactable.selectEntered.AddListener(OnSelectEntered);
            interactable.selectExited.AddListener(OnSelectExited);
        }

        void OnDisable()
        {
            if (interactable != null)
            {
                interactable.selectEntered.RemoveListener(OnSelectEntered);
                interactable.selectExited.RemoveListener(OnSelectExited);
            }
            selecting = false;
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            startLocal = AttachPoint(args.interactorObject, args.interactableObject);
            selecting = true;
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (!selecting || task == null) return;
            selecting = false;

            var (nx, ny, nz) = CubeFaces.Normal(face);
            var normal = new Vector3(nx, ny, nz);
            var endLocal = AttachPoint(args.interactorObject, args.interactableObject);
            var start = Vector3.ProjectOnPlane(startLocal, normal);
            var end = Vector3.ProjectOnPlane(endLocal, normal);

            // Both points are in cube space, where the face centre is on the axis, so the angle between them
            // is the sweep around the face centre. Positive is clockwise seen from outside the face.
            var angle = start.sqrMagnitude > 1e-8f && end.sqrMagnitude > 1e-8f ? Vector3.SignedAngle(start, end, normal) : 0f;
            task.ReleaseFace(face, angle, (end - start).magnitude, start.magnitude);
        }

        Vector3 AttachPoint(UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor interactor, IXRSelectInteractable interacted)
        {
            var attach = interactor.GetAttachTransform(interacted);
            return cubeRoot.InverseTransformPoint(attach != null ? attach.position : transform.position);
        }
    }
}
