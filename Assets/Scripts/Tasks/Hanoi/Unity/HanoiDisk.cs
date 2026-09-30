using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace PlayCT.Tasks.Hanoi
{
    /// <summary>
    /// One physical disk. Size 1 is the smallest. It is picked up with an <see cref="XRGrabInteractable"/>;
    /// this component reports grabs and releases to the task and moves the disk when the task places or returns it.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(XRGrabInteractable), typeof(Rigidbody))]
    public class HanoiDisk : MonoBehaviour, IXRSelectFilter
    {
        [SerializeField, Range(1, 5)] int size = 1;
        [SerializeField] float thickness = 0.022f;
        [SerializeField] float smallestDiameter = 0.10f;
        [SerializeField] float diameterStep = 0.03f;
        [SerializeField] float holeDiameter = 0.028f;

        XRGrabInteractable grab;
        Rigidbody body;
        Coroutine moveRoutine;
        bool filterRegistered;

        public int Size => size;
        public float Thickness => thickness;
        public float OuterRadius => (smallestDiameter + diameterStep * (size - 1)) * 0.5f;
        public float HoleRadius => holeDiameter * 0.5f;
        public bool IsHeld { get; private set; }
        public bool IsAnimating { get; private set; }
        public bool Accessible { get; set; } = true;
        public XRGrabInteractable Interactable => grab != null ? grab : GetComponent<XRGrabInteractable>();

        /// <summary>Extra condition the task can impose on grabbing (for example: nothing else is being held).</summary>
        public Func<HanoiDisk, bool> GrabPolicy { get; set; }

        public event Action<HanoiDisk> Grabbed;
        public event Action<HanoiDisk> Released;

        public bool canProcess => isActiveAndEnabled;

        void Awake()
        {
            grab = GetComponent<XRGrabInteractable>();
            body = GetComponent<Rigidbody>();
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
            if (!Accessible || IsAnimating) return false;
            return GrabPolicy == null || GrabPolicy(this);
        }

        void OnFirstSelectEntered(SelectEnterEventArgs args)
        {
            StopAnimation();
            IsHeld = true;
            Grabbed?.Invoke(this);
        }

        void OnLastSelectExited(SelectExitEventArgs args)
        {
            IsHeld = false;
            Released?.Invoke(this);
        }

        public void CancelSelection()
        {
            if (grab == null || !IsHeld) return;
            var manager = grab.interactionManager;
            if (manager != null) manager.CancelInteractableSelection((IXRSelectInteractable)grab);
            IsHeld = false;
        }

        /// <summary>Notifies subscribers as if the participant had grabbed the disk (used by scripted interaction).</summary>
        public void RaiseGrabbed()
        {
            IsHeld = true;
            Grabbed?.Invoke(this);
        }

        public void RaiseReleased()
        {
            IsHeld = false;
            Released?.Invoke(this);
        }

        public void PlaceImmediately(Vector3 position)
        {
            StopAnimation();
            transform.position = position;
        }

        public void MoveAlong(IList<Vector3> path, float duration, Action done = null)
        {
            StopAnimation();
            IsAnimating = true;
            moveRoutine = StartCoroutine(Animate(path, duration, done));
        }

        void StopAnimation()
        {
            if (moveRoutine != null) StopCoroutine(moveRoutine);
            moveRoutine = null;
            IsAnimating = false;
        }

        IEnumerator Animate(IList<Vector3> path, float duration, Action done)
        {
            IsAnimating = true;
            var lengths = new float[path.Count - 1];
            var total = 0f;
            for (var i = 0; i < lengths.Length; i++)
            {
                lengths[i] = Vector3.Distance(path[i], path[i + 1]);
                total += lengths[i];
            }

            if (total > 1e-4f && duration > 0f)
            {
                var elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                    transform.position = Sample(path, lengths, total, t);
                    yield return null;
                }
            }

            transform.position = path[path.Count - 1];
            moveRoutine = null;
            IsAnimating = false;
            done?.Invoke();
        }

        static Vector3 Sample(IList<Vector3> path, float[] lengths, float total, float t)
        {
            var distance = t * total;
            for (var i = 0; i < lengths.Length; i++)
            {
                if (distance <= lengths[i] || i == lengths.Length - 1)
                {
                    var f = lengths[i] > 1e-6f ? Mathf.Clamp01(distance / lengths[i]) : 1f;
                    return Vector3.Lerp(path[i], path[i + 1], f);
                }
                distance -= lengths[i];
            }
            return path[path.Count - 1];
        }
    }
}
