using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PlayCT.App;

namespace PlayCT.Tests
{
    public class PanelLayoutTests
    {
        /// <summary>A cautious estimate of a sans-serif font: wider than Arial on average, so a layout that fits here fits the real font.</summary>
        sealed class EstimatedMeasure : ITextMeasure
        {
            public float Width(string text, float em) => text.Sum(c => char.IsUpper(c) || char.IsDigit(c) ? 0.68f : c == ' ' ? 0.28f : 0.54f) * em;
        }

        static readonly ITextMeasure Measure = new EstimatedMeasure();

        struct Box
        {
            public float Left, Right, Bottom, Top;
            public bool Overlaps(Box o, float gap = 0f) => Left < o.Right - gap && Right > o.Left + gap && Bottom < o.Top - gap && Top > o.Bottom + gap;
        }

        static Box TextBox(TextItem t)
        {
            var width = Measure.Width(t.Text, t.Em);
            var left = t.Align == TextAlign.Left ? t.X : t.X - width * 0.5f;
            return new Box { Left = left, Right = left + width, Bottom = t.Y - t.Em * 0.55f, Top = t.Y + t.Em * 0.55f };
        }

        static Box ButtonBox(ButtonItem b) =>
            new Box { Left = b.X - b.Width * 0.5f, Right = b.X + b.Width * 0.5f, Bottom = b.Y - b.Height * 0.5f, Top = b.Y + b.Height * 0.5f };

        static List<PanelLayoutResult> Layouts() => ApplicationFlowTests.AllScreens().Select(s => PanelLayout.Build(s, Measure)).ToList();

        [Test]
        public void EveryScreenFitsItsPanel()
        {
            foreach (var layout in Layouts())
            {
                Assert.IsFalse(layout.Overflow, "a screen has more content than the panel can hold");
                var half = PanelLayout.PanelWidth * 0.5f - 0.05f;
                var halfHeight = PanelLayout.PanelHeight * 0.5f - 0.03f;
                foreach (var t in layout.Texts.Concat(layout.Buttons.SelectMany(b => b.Labels)))
                {
                    var box = TextBox(t);
                    Assert.That(box.Left, Is.GreaterThanOrEqualTo(-half), t.Text);
                    Assert.That(box.Right, Is.LessThanOrEqualTo(half), t.Text);
                    Assert.That(box.Top, Is.LessThanOrEqualTo(halfHeight), t.Text);
                    Assert.That(box.Bottom, Is.GreaterThanOrEqualTo(-halfHeight), t.Text);
                }
                foreach (var b in layout.Buttons)
                {
                    var box = ButtonBox(b);
                    Assert.That(box.Left, Is.GreaterThanOrEqualTo(-half), b.Spec.Label);
                    Assert.That(box.Right, Is.LessThanOrEqualTo(half), b.Spec.Label);
                    Assert.That(box.Top, Is.LessThanOrEqualTo(halfHeight), b.Spec.Label);
                    Assert.That(box.Bottom, Is.GreaterThanOrEqualTo(-halfHeight), b.Spec.Label);
                }
            }
        }

        [Test]
        public void NothingOverlaps()
        {
            foreach (var layout in Layouts())
            {
                var buttons = layout.Buttons.Select(ButtonBox).ToList();
                for (var i = 0; i < buttons.Count; i++)
                    for (var j = i + 1; j < buttons.Count; j++)
                        Assert.IsFalse(buttons[i].Overlaps(buttons[j]), $"'{layout.Buttons[i].Spec.Label}' overlaps '{layout.Buttons[j].Spec.Label}'");

                var free = layout.Texts.Select(TextBox).ToList();
                for (var i = 0; i < free.Count; i++)
                {
                    for (var j = i + 1; j < free.Count; j++)
                        Assert.IsFalse(free[i].Overlaps(free[j], 0.004f), $"'{layout.Texts[i].Text}' overlaps '{layout.Texts[j].Text}'");
                    for (var k = 0; k < buttons.Count; k++)
                        Assert.IsFalse(free[i].Overlaps(buttons[k]), $"'{layout.Texts[i].Text}' overlaps button '{layout.Buttons[k].Spec.Label}'");
                }

                foreach (var b in layout.Buttons)
                {
                    var box = ButtonBox(b);
                    foreach (var t in b.Labels)
                    {
                        var tb = TextBox(t);
                        Assert.That(tb.Left, Is.GreaterThanOrEqualTo(box.Left), $"label '{t.Text}' leaves its button");
                        Assert.That(tb.Right, Is.LessThanOrEqualTo(box.Right), $"label '{t.Text}' leaves its button");
                        Assert.That(tb.Bottom, Is.GreaterThanOrEqualTo(box.Bottom), $"label '{t.Text}' leaves its button");
                        Assert.That(tb.Top, Is.LessThanOrEqualTo(box.Top), $"label '{t.Text}' leaves its button");
                    }
                }
            }
        }

        [Test]
        public void TextIsLargeEnoughToReadInTheHeadset()
        {
            foreach (var layout in Layouts())
            {
                foreach (var t in layout.Texts.Concat(layout.Buttons.SelectMany(b => b.Labels)))
                {
                    var minimum = t.Role == TextRole.Footer ? 0.028f : 0.034f;
                    Assert.That(t.Em, Is.GreaterThanOrEqualTo(minimum), $"'{t.Text}' is too small");
                }
                foreach (var t in layout.Texts.Where(t => t.Role == TextRole.Body || t.Role == TextRole.Bullet))
                    Assert.That(t.Em, Is.GreaterThanOrEqualTo(0.04f), t.Text);
            }
        }

        [Test]
        public void ButtonsAreLargeEnoughToPointAt()
        {
            foreach (var layout in Layouts())
            {
                foreach (var b in layout.Buttons)
                {
                    Assert.That(b.Height, Is.GreaterThanOrEqualTo(0.09f), b.Spec.Label);
                    Assert.That(b.Width, Is.GreaterThanOrEqualTo(0.12f), b.Spec.Label);
                    if (b.Spec.Kind == ButtonKind.Hero || b.Spec.Kind == ButtonKind.Card) Assert.That(b.Width, Is.GreaterThanOrEqualTo(0.6f), b.Spec.Label);
                }
            }
        }

        [Test]
        public void TheWelcomeScreenIsCenteredAndShowsTheTwoOptionsSideBySide()
        {
            var layout = Layouts()[0];
            var hero = layout.Buttons.Where(b => b.Spec.Kind == ButtonKind.Hero).ToList();
            Assert.AreEqual(2, hero.Count);
            Assert.AreEqual(hero[0].Y, hero[1].Y, 1e-4);
            Assert.That(hero[0].X, Is.LessThan(0f));
            Assert.That(hero[1].X, Is.GreaterThan(0f));
            Assert.AreEqual(0f, hero[0].X + hero[1].X, 1e-4, "the two options are symmetric about the centre");
            Assert.AreEqual(hero[0].Width, hero[1].Width, 1e-4);

            var all = layout.Texts.Select(TextBox).Concat(layout.Buttons.Select(ButtonBox)).ToList();
            var top = all.Max(b => b.Top);
            var bottom = all.Min(b => b.Bottom);
            Assert.That((top + bottom) * 0.5f, Is.InRange(-0.12f, 0.12f), "the content sits around the middle of the panel");
        }

        [Test]
        public void TheKeypadKeepsEveryKeyOfTheCodeReachable()
        {
            var layout = Layouts().First(l => l.Buttons.Any(b => b.Spec.Kind == ButtonKind.Key));
            var keys = layout.Buttons.Where(b => b.Spec.Kind == ButtonKind.Key).Select(b => b.Spec.Label).ToList();
            foreach (var c in "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-") CollectionAssert.Contains(keys, c.ToString());
            CollectionAssert.Contains(keys, "Borrar");
            CollectionAssert.Contains(keys, "Limpiar");
            Assert.AreEqual(keys.Count, keys.Distinct().Count());
        }

        [Test]
        public void WrappingNeverSplitsWordsAndKeepsEveryWord()
        {
            var text = "Sujeta el cubo con una mano para estabilizarlo. Con la otra, toca una cara para seleccionarla y gírala.";
            var lines = PanelLayout.Wrap(text, 0.042f, 0.8f, Measure);
            Assert.Greater(lines.Count, 1);
            Assert.AreEqual(text, string.Join(" ", lines));
            foreach (var line in lines) Assert.That(Measure.Width(line, 0.042f), Is.LessThanOrEqualTo(0.8f));
        }
    }
}
