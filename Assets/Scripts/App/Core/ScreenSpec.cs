using System;
using System.Collections.Generic;

namespace PlayCT.App
{
    public enum ButtonKind
    {
        /// <summary>The two large choices of the start screen.</summary>
        Hero,

        /// <summary>A game card.</summary>
        Card,

        /// <summary>The main action of a screen.</summary>
        Primary,

        Secondary,

        /// <summary>A key of the participant code keypad.</summary>
        Key,
    }

    public sealed class ButtonSpec
    {
        public string Id { get; }
        public string Label { get; }
        public string Description { get; set; }
        public ButtonKind Kind { get; }
        public bool Enabled { get; set; } = true;
        public Action OnPress { get; }

        public ButtonSpec(string id, string label, ButtonKind kind, Action onPress)
        {
            Id = id;
            Label = label;
            Kind = kind;
            OnPress = onPress;
        }
    }

    /// <summary>
    /// Everything one screen shows, as plain data: texts and buttons. The VR panel draws it; it contains no Unity types and no
    /// layout, so the content of each screen can be checked without the engine.
    /// </summary>
    public sealed class ScreenSpec
    {
        public AppState State { get; set; }
        public string Kicker { get; set; }
        public string Title { get; set; }
        public List<string> Paragraphs { get; } = new List<string>();
        public List<string> Bullets { get; } = new List<string>();
        public string Notice { get; set; }
        public string Info { get; set; }
        public string FieldLabel { get; set; }
        public string FieldValue { get; set; }
        public List<List<ButtonSpec>> KeyRows { get; } = new List<List<ButtonSpec>>();
        public List<List<ButtonSpec>> ButtonRows { get; } = new List<List<ButtonSpec>>();
        public string Footer { get; set; }

        public IEnumerable<ButtonSpec> AllButtons()
        {
            foreach (var row in KeyRows)
                foreach (var button in row) yield return button;
            foreach (var row in ButtonRows)
                foreach (var button in row) yield return button;
        }
    }
}
