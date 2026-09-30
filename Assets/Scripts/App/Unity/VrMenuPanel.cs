using System.Collections.Generic;
using UnityEngine;

namespace PlayCT.App
{
    /// <summary>
    /// A wooden-framed linen panel in world space that draws one <see cref="PanelLayoutResult"/>: its texts, field, markers and
    /// buttons. Its local -Z faces the participant, so text reads correctly when it is placed looking away from the head.
    /// </summary>
    public class VrMenuPanel : MonoBehaviour
    {
        const float FrameBorder = 0.06f;
        const float FaceDepth = 0.02f;
        const float FaceZ = 0f;
        const float TextZ = -FaceDepth * 0.5f - 0.0015f;

        readonly List<VrMenuButton> buttons = new List<VrMenuButton>();
        VrMenuStyle style;
        Transform content;
        GameObject chrome;
        float builtWidth;
        float builtHeight;

        public bool Visible => gameObject.activeSelf;
        public IReadOnlyList<VrMenuButton> Buttons => buttons;

        public static VrMenuPanel Create(string name, Transform parent, VrMenuStyle style)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var panel = go.AddComponent<VrMenuPanel>();
            panel.style = style;
            go.SetActive(false);
            return panel;
        }

        public void Show(PanelLayoutResult layout)
        {
            EnsureChrome(layout.Width, layout.Height);
            Clear();

            content = new GameObject("Content").transform;
            content.SetParent(transform, false);

            foreach (var rect in layout.Rects)
            {
                if (rect.Role == RectRole.FieldBox)
                {
                    style.Box("Field", content, new Vector3(rect.Width, rect.Height, 0.004f), new Vector3(rect.X, rect.Y, TextZ + 0.0015f), style.Surface(VrMenuStyle.FieldBox));
                    style.Box("FieldEdge", content, new Vector3(rect.Width + 0.008f, rect.Height + 0.008f, 0.003f), new Vector3(rect.X, rect.Y, TextZ + 0.0025f), style.Surface(VrMenuStyle.Darken(VrMenuStyle.FieldBox, 0.4f)));
                }
                else
                {
                    style.Box("Marker", content, new Vector3(rect.Width, rect.Height, 0.003f), new Vector3(rect.X, rect.Y, TextZ), style.Surface(VrMenuStyle.Clay));
                }
            }

            foreach (var text in layout.Texts) style.Text(text, content, TextZ - 0.004f, ColorFor(text.Role));

            var heroIndex = 0;
            foreach (var item in layout.Buttons)
            {
                var index = item.Spec.Kind == ButtonKind.Hero ? heroIndex++ : 0;
                buttons.Add(VrMenuButton.Create(content, style, item, index));
            }

            gameObject.SetActive(true);
        }

        public void Hide()
        {
            Clear();
            gameObject.SetActive(false);
        }

        public void PlaceAt(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
        }

        void Clear()
        {
            buttons.Clear();
            if (content == null) return;
            content.gameObject.SetActive(false);
            Destroy(content.gameObject);
            content = null;
        }

        void EnsureChrome(float width, float height)
        {
            if (chrome != null && Mathf.Approximately(width, builtWidth) && Mathf.Approximately(height, builtHeight)) return;
            if (chrome != null) Destroy(chrome);
            builtWidth = width;
            builtHeight = height;

            chrome = new GameObject("Chrome");
            chrome.transform.SetParent(transform, false);
            style.Box("Frame", chrome.transform, new Vector3(width + 2f * FrameBorder, height + 2f * FrameBorder, 0.05f), new Vector3(0f, 0f, 0.02f), style.Oak);
            var face = style.Box("Face", chrome.transform, new Vector3(width, height, FaceDepth), new Vector3(0f, 0f, FaceZ), style.Surface(VrMenuStyle.Face), collider: true);
            face.AddComponent<VrMenuTarget>();
        }

        static Color ColorFor(TextRole role)
        {
            switch (role)
            {
                case TextRole.Kicker: return VrMenuStyle.Clay;
                case TextRole.Notice: return VrMenuStyle.Clay;
                case TextRole.Footer: return VrMenuStyle.Muted;
                case TextRole.FieldLabel: return VrMenuStyle.Muted;
                default: return VrMenuStyle.Ink;
            }
        }
    }
}
