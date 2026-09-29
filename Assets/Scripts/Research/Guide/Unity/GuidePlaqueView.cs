using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PlayCT.Research
{
    /// <summary>
    /// The physical face of the shared guide: a linen card in a small oak stand at the far edge of the table that shows one
    /// short Spanish message at a time for a few seconds, and a wooden Ayuda button beside it. It shows only the text the guide
    /// engine gives it, nothing else (no score, timer or progress), and is not a floating HUD.
    /// </summary>
    public class GuidePlaqueView : MonoBehaviour, IGuidePresenter
    {
        [SerializeField] GuideDirector director;
        [SerializeField] Material woodMaterial;
        [SerializeField] Material cardMaterial;
        [SerializeField] Material inkMaterial;
        [SerializeField] float displaySeconds = 10f;
        [SerializeField] int maxLineCharacters = 40;
        [SerializeField] float textScale = 1f;

        const float CardWidth = 0.46f;
        const float CardHeight = 0.10f;
        const float LineHeight = 0.02f;

        readonly Queue<string> pending = new Queue<string>();
        Transform root;
        Transform card;
        TextMesh text;
        Font font;
        Coroutine showing;
        bool built;

        public bool IsShowing => showing != null;

        void Awake() => Build();

        void OnDisable()
        {
            if (showing != null)
            {
                StopCoroutine(showing);
                showing = null;
            }
            pending.Clear();
            if (card != null) card.gameObject.SetActive(false);
        }

        public void Present(GuideMessage message, string taskId, int? trial)
        {
            Build();
            pending.Enqueue(message.Text);
            if (showing == null && isActiveAndEnabled) showing = StartCoroutine(ShowQueue());
        }

        IEnumerator ShowQueue()
        {
            while (pending.Count > 0)
            {
                text.text = Wrap(pending.Dequeue(), maxLineCharacters);
                card.gameObject.SetActive(true);
                var until = Time.time + displaySeconds;
                while (Time.time < until) yield return null;
                card.gameObject.SetActive(false);
                yield return new WaitForSeconds(0.4f);
            }
            showing = null;
        }

        void Build()
        {
            if (built) return;
            built = true;
            root = new GameObject("GuideCard").transform;
            root.SetParent(transform, false);

            var stand = MakeBox("Stand", root, new Vector3(CardWidth + 0.02f, 0.014f, 0.07f), new Vector3(0f, 0.007f, 0f), woodMaterial);
            card = MakeBox("Card", root, new Vector3(CardWidth, CardHeight, 0.01f), new Vector3(0f, 0.014f + CardHeight * 0.5f * 0.94f, 0.012f), cardMaterial).transform;
            card.localRotation = Quaternion.Euler(20f, 0f, 0f);
            text = MakeText(card, "", LineHeight, new Vector3(0f, 0f, -0.0058f));
            card.gameObject.SetActive(false);

            var button = MakeBox("Button_Ayuda", root, new Vector3(0.11f, 0.024f, 0.055f), new Vector3(CardWidth * 0.5f + 0.11f, 0.012f, 0f), woodMaterial);
            var collider = button.GetComponent<BoxCollider>();
            if (collider == null) collider = button.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            var body = button.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            button.AddComponent<XRSimpleInteractable>();
            button.AddComponent<GuideHelpHandle>().Configure(director);
            MakeText(button.transform, "Ayuda", 0.022f, new Vector3(0f, 0.0125f, 0f)).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        GameObject MakeBox(string name, Transform parent, Vector3 size, Vector3 localPosition, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        TextMesh MakeText(Transform parent, string content, float height, Vector3 localPosition)
        {
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = content;
            mesh.font = font;
            mesh.fontSize = 64;
            mesh.characterSize = height / 6.4f * textScale;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = inkMaterial != null ? inkMaterial.color : Color.black;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return mesh;
        }

        /// <summary>Breaks the text into lines of at most <paramref name="maxCharacters"/> characters without splitting words.</summary>
        public static string Wrap(string value, int maxCharacters)
        {
            var sb = new StringBuilder();
            var lineLength = 0;
            foreach (var word in value.Split(' '))
            {
                if (lineLength > 0 && lineLength + 1 + word.Length > maxCharacters)
                {
                    sb.Append('\n');
                    lineLength = 0;
                }
                else if (lineLength > 0)
                {
                    sb.Append(' ');
                    lineLength++;
                }
                sb.Append(word);
                lineLength += word.Length;
            }
            return sb.ToString();
        }
    }
}
