using System.Collections.Generic;
using UnityEngine;

namespace PlayCT.App
{
    /// <summary>
    /// Colours, font and shared meshes of the VR menu. The palette is the laboratory's own (oak, linen, ink, sage, slate, clay): no
    /// saturated or neon colours. Surfaces use one emissive material so they read the same under any lighting.
    /// </summary>
    public sealed class VrMenuStyle
    {
        public const int FontSize = 96;

        public static readonly Color Face = new Color(0.93f, 0.89f, 0.81f);
        public static readonly Color FieldBox = new Color(0.84f, 0.79f, 0.69f);
        public static readonly Color Ink = new Color(0.17f, 0.20f, 0.25f);
        public static readonly Color Muted = new Color(0.36f, 0.38f, 0.40f);
        public static readonly Color Clay = new Color(0.66f, 0.40f, 0.28f);
        public static readonly Color OnDark = new Color(0.98f, 0.96f, 0.91f);
        public static readonly Color Sage = new Color(0.30f, 0.42f, 0.32f);
        public static readonly Color Slate = new Color(0.25f, 0.33f, 0.42f);
        public static readonly Color Linen = new Color(0.97f, 0.94f, 0.88f);
        public static readonly Color Secondary = new Color(0.80f, 0.75f, 0.65f);
        public static readonly Color Disabled = new Color(0.78f, 0.74f, 0.67f);
        public static readonly Color DisabledText = new Color(0.47f, 0.47f, 0.46f);
        public static readonly Color Highlight = new Color(0.88f, 0.66f, 0.30f);
        public static readonly Color Pressed = new Color(0.55f, 0.36f, 0.24f);

        readonly Material surfaceSource;
        readonly Dictionary<Color, Material> surfaces = new Dictionary<Color, Material>();
        Font font;
        Mesh cube;

        public Material Oak { get; }

        public VrMenuStyle(Material surface, Material oak)
        {
            surfaceSource = surface;
            Oak = oak;
        }

        public Font Font
        {
            get
            {
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        public Mesh Cube
        {
            get
            {
                if (cube == null)
                {
                    var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube = primitive.GetComponent<MeshFilter>().sharedMesh;
                    Object.Destroy(primitive);
                }
                return cube;
            }
        }

        public Material Surface(Color color)
        {
            if (surfaces.TryGetValue(color, out var material) && material != null) return material;
            material = surfaceSource != null ? new Material(surfaceSource) : new Material(Shader.Find("Standard"));
            material.color = color;
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 0.55f);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            surfaces[color] = material;
            return material;
        }

        public GameObject Box(string name, Transform parent, Vector3 size, Vector3 localPosition, Material material, bool collider = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = Cube;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (collider) go.AddComponent<BoxCollider>();
            return go;
        }

        public TextMesh Text(TextItem item, Transform parent, float z, Color color)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(item.X, item.Y, z);
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = item.Text;
            mesh.font = Font;
            mesh.fontSize = FontSize;
            mesh.characterSize = item.Em / (FontSize * 0.1f);
            mesh.anchor = item.Align == TextAlign.Left ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
            mesh.alignment = item.Align == TextAlign.Left ? TextAlignment.Left : TextAlignment.Center;
            mesh.color = color;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = Font.material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return mesh;
        }

        public static Color Lighten(Color color, float amount) => Color.Lerp(color, Color.white, amount);

        public static Color Darken(Color color, float amount) => Color.Lerp(color, Color.black, amount);

        public static Color TextColorFor(ButtonKind kind, int indexInRow)
        {
            switch (kind)
            {
                case ButtonKind.Hero:
                case ButtonKind.Primary:
                    return OnDark;
                default:
                    return Ink;
            }
        }

        public static Color FillFor(ButtonKind kind, int indexInRow)
        {
            switch (kind)
            {
                case ButtonKind.Hero: return indexInRow == 0 ? Sage : Slate;
                case ButtonKind.Primary: return Slate;
                case ButtonKind.Secondary: return Secondary;
                default: return Linen;
            }
        }
    }

    /// <summary>Measures text with the menu font so lines wrap at the real width.</summary>
    public sealed class UnityTextMeasure : ITextMeasure
    {
        readonly VrMenuStyle style;

        public UnityTextMeasure(VrMenuStyle style) => this.style = style;

        public float Width(string text, float em)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            var font = style.Font;
            font.RequestCharactersInTexture(text, VrMenuStyle.FontSize, FontStyle.Normal);
            var advance = 0f;
            foreach (var c in text)
            {
                advance += font.GetCharacterInfo(c, out var info, VrMenuStyle.FontSize, FontStyle.Normal)
                    ? info.advance
                    : VrMenuStyle.FontSize * 0.55f;
            }
            return advance / VrMenuStyle.FontSize * em;
        }
    }
}
