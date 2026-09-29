using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace PlayCT.Tasks.Gabinete
{
    /// <summary>
    /// One physical piece, picked up with an <see cref="XRGrabInteractable"/> that tracks both position and rotation. It
    /// reports grabs and releases (with its pose in board space) to the task and only moves itself when the task tells the
    /// view to seat or return it. It never decides whether a placement is right.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(XRGrabInteractable), typeof(Rigidbody))]
    public class GabinetePieceView : MonoBehaviour, IXRSelectFilter
    {
        XRGrabInteractable grab;
        GabineteTask task;
        Transform board;
        Vector3 homePosition;
        float homeYaw;
        Coroutine moveRoutine;
        bool filterRegistered;

        public string PieceId { get; private set; }
        public bool IsHeld { get; private set; }
        public bool IsAnimating { get; private set; }
        public bool Accessible { get; set; } = true;

        public bool canProcess => isActiveAndEnabled;

        public void Configure(GabineteTask owner, string pieceId, Transform boardFrame, Vector3 home, float startYaw)
        {
            task = owner;
            PieceId = pieceId;
            board = boardFrame;
            homePosition = home;
            homeYaw = startYaw;
        }

        void Awake()
        {
            grab = GetComponent<XRGrabInteractable>();
            var body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
        }

        void OnEnable()
        {
            if (grab == null) grab = GetComponent<XRGrabInteractable>();
            grab.firstSelectEntered.AddListener(OnFirstSelectEntered);
            grab.lastSelectExited.AddListener(OnLastSelectExited);
            grab.selectFilters.Add(this);
            filterRegistered = true;
        }

        void OnDisable()
        {
            if (grab != null)
            {
                grab.firstSelectEntered.RemoveListener(OnFirstSelectEntered);
                grab.lastSelectExited.RemoveListener(OnLastSelectExited);
                if (filterRegistered) grab.selectFilters.Remove(this);
            }
            filterRegistered = false;
            StopAnimation();
            IsHeld = false;
        }

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            if (IsHeld) return true;
            return Accessible && !IsAnimating;
        }

        void OnFirstSelectEntered(SelectEnterEventArgs args)
        {
            StopAnimation();
            IsHeld = true;
            task?.OnPieceGrabbed(PieceId, CurrentPose().YawDegrees);
        }

        void OnLastSelectExited(SelectExitEventArgs args)
        {
            IsHeld = false;
            task?.OnPieceReleased(PieceId, CurrentPose());
        }

        /// <summary>Position, yaw about the board's vertical axis, tilt and height above the board surface, all in board space.</summary>
        public PiecePose CurrentPose()
        {
            var local = board.InverseTransformPoint(transform.position);
            var forward = board.InverseTransformDirection(transform.forward);
            forward.y = 0f;
            double yaw;
            if (forward.sqrMagnitude > 0.09f)
            {
                yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            }
            else
            {
                var right = board.InverseTransformDirection(transform.right);
                yaw = Mathf.Atan2(-right.z, right.x) * Mathf.Rad2Deg;
            }
            var tilt = Vector3.Angle(transform.up, board.up);
            return new PiecePose(local.x, local.z, yaw, tilt, local.y);
        }

        public void ShowAtHome()
        {
            StopAnimation();
            transform.SetPositionAndRotation(WorldHome(), board.rotation * Quaternion.Euler(0f, homeYaw, 0f));
        }

        public void ReturnHome(float duration) => MoveTo(WorldHome(), board.rotation * Quaternion.Euler(0f, homeYaw, 0f), duration, null);

        public void Seat(Vector3 worldPosition, float yaw, float duration, Action done)
        {
            Accessible = false;
            CancelSelection();
            MoveTo(worldPosition, board.rotation * Quaternion.Euler(0f, yaw, 0f), duration, done);
        }

        Vector3 WorldHome() => board.parent != null ? board.parent.TransformPoint(homePosition) : homePosition;

        public void CancelSelection()
        {
            if (grab == null || !IsHeld) return;
            var manager = grab.interactionManager;
            if (manager != null) manager.CancelInteractableSelection((IXRSelectInteractable)grab);
            IsHeld = false;
        }

        void MoveTo(Vector3 position, Quaternion rotation, float duration, Action done)
        {
            StopAnimation();
            if (!isActiveAndEnabled || duration <= 0f)
            {
                transform.SetPositionAndRotation(position, rotation);
                done?.Invoke();
                return;
            }
            moveRoutine = StartCoroutine(Move(position, rotation, duration, done));
        }

        IEnumerator Move(Vector3 position, Quaternion rotation, float duration, Action done)
        {
            IsAnimating = true;
            var startPosition = transform.position;
            var startRotation = transform.rotation;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                transform.SetPositionAndRotation(Vector3.Lerp(startPosition, position, t), Quaternion.Slerp(startRotation, rotation, t));
                yield return null;
            }
            transform.SetPositionAndRotation(position, rotation);
            IsAnimating = false;
            moveRoutine = null;
            done?.Invoke();
        }

        void StopAnimation()
        {
            if (moveRoutine != null) StopCoroutine(moveRoutine);
            moveRoutine = null;
            IsAnimating = false;
        }
    }
}
