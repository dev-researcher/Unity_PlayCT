using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PlayCT.App
{
    /// <summary>
    /// Lets the participant point at the menu with either Quest controller and select with the trigger. It reads the controllers'
    /// aim pose and trigger through the Input System (the same devices the XR Interaction Toolkit uses), draws a thin ray and a
    /// small dot only while a menu is on screen, and presses the button under the ray. It is separate from the direct interactors
    /// that grab task objects, so it never moves a disk, piece or cube.
    /// </summary>
    public sealed class VrMenuPointer : System.IDisposable
    {
        const float MaxDistance = 6f;
        const float RayThickness = 0.004f;

        sealed class Hand
        {
            public string Name;
            public InputAction Position;
            public InputAction Rotation;
            public InputAction Trigger;
            public Transform Fallback;
            public Transform Ray;
            public Transform Dot;
            public VrMenuButton Hovered;
        }

        readonly Transform trackingSpace;
        readonly Transform root;

        readonly List<Hand> hands = new List<Hand>();
        readonly RaycastHit[] hits = new RaycastHit[24];
        readonly VrMenuStyle style;
        bool active;

        public bool Active
        {
            get => active;
            set
            {
                active = value;
                if (!active) ClearHover();
            }
        }

        /// <summary>False while playing: the ray then shows only when it is on the exit plaque, so it does not distract from the task.</summary>
        public bool AlwaysShowRay { get; set; } = true;

        public VrMenuPointer(VrMenuStyle menuStyle, Transform parent, Transform tracking, Transform left, Transform right)
        {
            style = menuStyle;
            trackingSpace = tracking;
            root = new GameObject("MenuPointer").transform;
            root.SetParent(parent, false);
            hands.Add(CreateHand("LeftHand", left));
            hands.Add(CreateHand("RightHand", right));
            foreach (var hand in hands)
            {
                hand.Position.Enable();
                hand.Rotation.Enable();
                hand.Trigger.Enable();
            }
        }

        Hand CreateHand(string name, Transform fallback)
        {
            var hand = new Hand
            {
                Name = name,
                Fallback = fallback,
                Position = new InputAction("Pointer Position " + name, InputActionType.Value, $"<XRController>{{{name}}}/pointerPosition"),
                Rotation = new InputAction("Pointer Rotation " + name, InputActionType.Value, $"<XRController>{{{name}}}/pointerRotation"),
                Trigger = new InputAction("Menu Select " + name, InputActionType.Button, $"<XRController>{{{name}}}/{{TriggerButton}}"),
            };
            return hand;
        }

        public void Dispose()
        {
            ClearHover();
            foreach (var hand in hands)
            {
                hand.Position.Dispose();
                hand.Rotation.Dispose();
                hand.Trigger.Dispose();
            }
            hands.Clear();
            if (root != null) Object.Destroy(root.gameObject);
        }

        /// <summary>Call once per frame.</summary>
        public void Tick()
        {
            foreach (var hand in hands)
            {
                if (!active)
                {
                    SetVisuals(hand, false, default, default);
                    continue;
                }

                if (!TryGetRay(hand, out var origin, out var direction))
                {
                    SetHover(hand, null);
                    SetVisuals(hand, false, default, default);
                    continue;
                }

                var end = origin + direction * 2.5f;
                VrMenuButton target = null;
                var count = Physics.RaycastNonAlloc(origin, direction, hits, MaxDistance, ~0, QueryTriggerInteraction.Collide);
                var best = float.MaxValue;
                var onPanel = false;
                for (var i = 0; i < count; i++)
                {
                    var hit = hits[i];
                    var marker = hit.collider.GetComponent<VrMenuTarget>();
                    if (marker == null || hit.distance >= best) continue;
                    best = hit.distance;
                    target = marker.Button;
                    end = hit.point;
                    onPanel = true;
                }

                SetHover(hand, target);
                SetVisuals(hand, AlwaysShowRay || onPanel, origin, end);
                if (target != null && hand.Trigger.WasPressedThisFrame()) target.Press();
            }
        }

        bool TryGetRay(Hand hand, out Vector3 origin, out Vector3 direction)
        {
            origin = default;
            direction = Vector3.forward;
            if (hand.Position.controls.Count > 0 && trackingSpace != null)
            {
                var position = hand.Position.ReadValue<Vector3>();
                var rotation = hand.Rotation.ReadValue<Quaternion>();
                if (position != Vector3.zero)
                {
                    origin = trackingSpace.TransformPoint(position);
                    direction = (trackingSpace.rotation * rotation) * Vector3.forward;
                    return true;
                }
            }
            if (hand.Fallback != null && hand.Fallback.gameObject.activeInHierarchy)
            {
                origin = hand.Fallback.position;
                direction = hand.Fallback.forward;
                return hand.Position.controls.Count == 0 && hand.Trigger.controls.Count > 0;
            }
            return false;
        }

        void SetHover(Hand hand, VrMenuButton target)
        {
            if (hand.Hovered == target) return;
            if (hand.Hovered != null) hand.Hovered.RemoveHover();
            hand.Hovered = target;
            if (target != null) target.AddHover();
        }

        void ClearHover()
        {
            foreach (var hand in hands) SetHover(hand, null);
        }

        void SetVisuals(Hand hand, bool visible, Vector3 origin, Vector3 end)
        {
            if (!visible)
            {
                if (hand.Ray != null) hand.Ray.gameObject.SetActive(false);
                if (hand.Dot != null) hand.Dot.gameObject.SetActive(false);
                return;
            }

            if (hand.Ray == null)
            {
                hand.Ray = style.Box("PointerRay_" + hand.Name, root, Vector3.one, Vector3.zero, style.Surface(VrMenuStyle.Highlight)).transform;
                hand.Dot = style.Box("PointerDot_" + hand.Name, root, Vector3.one * 0.018f, Vector3.zero, style.Surface(VrMenuStyle.Lighten(VrMenuStyle.Highlight, 0.35f))).transform;
            }

            var delta = end - origin;
            var length = Mathf.Max(0.01f, delta.magnitude);
            hand.Ray.gameObject.SetActive(true);
            hand.Ray.position = origin + delta * 0.5f;
            hand.Ray.rotation = Quaternion.LookRotation(delta.normalized);
            hand.Ray.localScale = new Vector3(RayThickness, RayThickness, length);
            hand.Dot.gameObject.SetActive(true);
            hand.Dot.position = end;
        }
    }
}
