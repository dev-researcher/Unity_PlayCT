using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PlayCT.Tasks.Correo
{
    public enum CorreoHandleKind
    {
        Settlement,
        Package,
        Send,
        Clear,
    }

    /// <summary>
    /// Touch target on a settlement, a package, or one of the two wooden buttons. It turns a select from the existing XR
    /// interactors into a call on the task and holds no rules. Buttons dip briefly when pressed.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class CorreoHandle : MonoBehaviour
    {
        const float DipDepth = 0.004f;
        const float DipSeconds = 0.15f;

        CorreoHandleKind kind;
        Settlement settlement;
        CorreoPackage package;
        CorreoTask task;
        XRSimpleInteractable interactable;
        Coroutine dip;
        Vector3 restingPosition;

        public void Configure(CorreoTask owner, CorreoHandleKind handleKind, Settlement whichSettlement = Settlement.A, CorreoPackage whichPackage = CorreoPackage.P1)
        {
            task = owner;
            kind = handleKind;
            settlement = whichSettlement;
            package = whichPackage;
            restingPosition = transform.localPosition;
        }

        void OnEnable()
        {
            if (interactable == null) interactable = GetComponent<XRSimpleInteractable>();
            interactable.firstSelectEntered.AddListener(OnFirstSelectEntered);
        }

        void OnDisable()
        {
            if (interactable != null) interactable.firstSelectEntered.RemoveListener(OnFirstSelectEntered);
            if (dip != null)
            {
                StopCoroutine(dip);
                dip = null;
                transform.localPosition = restingPosition;
            }
        }

        void OnFirstSelectEntered(SelectEnterEventArgs args) => Press();

        /// <summary>The same action as a select by the participant; also used by scripted interaction.</summary>
        public void Press()
        {
            if (task == null) return;
            switch (kind)
            {
                case CorreoHandleKind.Settlement:
                    task.TapSettlement(settlement);
                    break;
                case CorreoHandleKind.Package:
                    task.TogglePackage(package);
                    break;
                case CorreoHandleKind.Send:
                    task.Confirm();
                    Dip();
                    break;
                case CorreoHandleKind.Clear:
                    task.ClearSelection();
                    Dip();
                    break;
            }
        }

        void Dip()
        {
            if (!isActiveAndEnabled) return;
            if (dip != null) StopCoroutine(dip);
            dip = StartCoroutine(DipRoutine());
        }

        IEnumerator DipRoutine()
        {
            var elapsed = 0f;
            while (elapsed < DipSeconds)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Sin(Mathf.Clamp01(elapsed / DipSeconds) * Mathf.PI);
                transform.localPosition = restingPosition + Vector3.down * (DipDepth * t);
                yield return null;
            }
            transform.localPosition = restingPosition;
            dip = null;
        }
    }
}
