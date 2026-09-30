using UnityEngine;

namespace PlayCT.App
{
    /// <summary>Marks a collider the menu pointer can hit. A target without a button (the panel face) only blocks the ray.</summary>
    public class VrMenuTarget : MonoBehaviour
    {
        public VrMenuButton Button { get; set; }
    }

    /// <summary>
    /// One wooden button of the VR menu: a slab with a label that lifts and lights its edge when a pointer is on it, dips when
    /// pressed, and is dimmed when disabled. It holds no rules; a press just invokes the action the screen gave it.
    /// </summary>
    public class VrMenuButton : MonoBehaviour
    {
        const float RestZ = -0.018f;
        const float HoverLift = 0.010f;
        const float PressDip = 0.008f;
        const float LabelOffset = 0.0105f;
        const float PressFlashSeconds = 0.14f;
        const float CooldownSeconds = 0.35f;

        VrMenuStyle style;
        ButtonSpec spec;
        Transform slab;
        Renderer slabRenderer;
        Renderer outlineRenderer;
        TextMesh[] labelMeshes = new TextMesh[0];
        TextRole[] roles = new TextRole[0];
        int appliedState = -1;
        Color fill;
        Color textColor;
        int hoverCount;
        float pressedUntil;
        float cooldownUntil;
        bool built;

        public ButtonSpec Spec => spec;
        public bool IsHovered => hoverCount > 0;

        public static VrMenuButton Create(Transform parent, VrMenuStyle style, ButtonItem item, int indexInRow)
        {
            var go = new GameObject("Button_" + item.Spec.Id);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(item.X, item.Y, 0f);
            var button = go.AddComponent<VrMenuButton>();
            button.Build(style, item, indexInRow);
            return button;
        }

        void Build(VrMenuStyle menuStyle, ButtonItem item, int indexInRow)
        {
            style = menuStyle;
            spec = item.Spec;
            fill = VrMenuStyle.FillFor(spec.Kind, indexInRow);
            textColor = VrMenuStyle.TextColorFor(spec.Kind, indexInRow);

            var outline = style.Box("Outline", transform, new Vector3(item.Width + 0.014f, item.Height + 0.014f, 0.012f), new Vector3(0f, 0f, -0.016f), style.Surface(VrMenuStyle.Darken(fill, 0.45f)));
            outlineRenderer = outline.GetComponent<Renderer>();

            var slabObject = style.Box("Slab", transform, new Vector3(item.Width, item.Height, 0.016f), new Vector3(0f, 0f, RestZ), style.Surface(fill), collider: true);
            slab = slabObject.transform;
            slabRenderer = slabObject.GetComponent<Renderer>();
            var collider = slabObject.GetComponent<BoxCollider>();
            collider.isTrigger = true;
            slabObject.AddComponent<VrMenuTarget>().Button = this;

            var list = new TextMesh[item.Labels.Count];
            roles = new TextRole[item.Labels.Count];
            for (var i = 0; i < item.Labels.Count; i++)
            {
                var line = item.Labels[i];
                var local = new TextItem { Text = line.Text, X = line.X - item.X, Y = line.Y - item.Y, Em = line.Em, Align = line.Align, Role = line.Role };
                list[i] = style.Text(local, transform, RestZ - LabelOffset, textColor);
                roles[i] = line.Role;
            }
            labelMeshes = list;
            built = true;
            Apply();
        }

        public void AddHover() => hoverCount++;

        public void RemoveHover()
        {
            if (hoverCount > 0) hoverCount--;
        }

        /// <summary>Presses the button. False when it is disabled or was pressed a moment ago.</summary>
        public bool Press()
        {
            if (spec == null || !spec.Enabled || Time.unscaledTime < cooldownUntil) return false;
            cooldownUntil = Time.unscaledTime + CooldownSeconds;
            pressedUntil = Time.unscaledTime + PressFlashSeconds;
            spec.OnPress?.Invoke();
            return true;
        }

        void Update()
        {
            if (built) Apply();
        }

        void Apply()
        {
            if (slab == null || spec == null) return;
            var pressed = Time.unscaledTime < pressedUntil;
            var state = !spec.Enabled ? 3 : pressed ? 2 : IsHovered ? 1 : 0;
            if (state == appliedState) return;
            appliedState = state;

            Color face;
            Color edge;
            Color label;
            var z = RestZ;
            switch (state)
            {
                case 3:
                    face = VrMenuStyle.Disabled;
                    edge = VrMenuStyle.Darken(face, 0.2f);
                    label = VrMenuStyle.DisabledText;
                    break;
                case 2:
                    face = VrMenuStyle.Pressed;
                    edge = VrMenuStyle.Highlight;
                    label = VrMenuStyle.OnDark;
                    z = RestZ + PressDip;
                    break;
                case 1:
                    face = VrMenuStyle.Lighten(fill, 0.16f);
                    edge = VrMenuStyle.Highlight;
                    label = textColor;
                    z = RestZ - HoverLift;
                    break;
                default:
                    face = fill;
                    edge = VrMenuStyle.Darken(fill, 0.45f);
                    label = textColor;
                    break;
            }

            slabRenderer.sharedMaterial = style.Surface(face);
            outlineRenderer.sharedMaterial = style.Surface(edge);
            var position = slab.localPosition;
            position.z = z;
            slab.localPosition = position;
            for (var i = 0; i < labelMeshes.Length; i++)
            {
                var mesh = labelMeshes[i];
                if (mesh == null) continue;
                var p = mesh.transform.localPosition;
                p.z = z - LabelOffset;
                mesh.transform.localPosition = p;
                mesh.color = roles[i] == TextRole.ButtonDescription ? Color.Lerp(label, face, 0.15f) : label;
            }
        }
    }
}
