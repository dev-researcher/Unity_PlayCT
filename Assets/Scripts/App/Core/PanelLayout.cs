using System;
using System.Collections.Generic;

namespace PlayCT.App
{
    /// <summary>Measures how wide a line of text is for a given em height (metres). The Unity side uses the real font; tests use an estimate.</summary>
    public interface ITextMeasure
    {
        float Width(string text, float em);
    }

    public enum TextAlign
    {
        Left,
        Center,
    }

    public enum TextRole
    {
        Kicker,
        Title,
        Body,
        Bullet,
        FieldLabel,
        FieldValue,
        Info,
        Notice,
        Footer,
        ButtonLabel,
        ButtonDescription,
    }

    /// <summary>One line of text. X and Y are the panel-local centre of the line (metres, Y up); for left-aligned lines X is the left edge.</summary>
    public sealed class TextItem
    {
        public string Text;
        public float X;
        public float Y;
        public float Em;
        public TextAlign Align;
        public TextRole Role;
    }

    public enum RectRole
    {
        BulletMarker,
        FieldBox,
    }

    public sealed class RectItem
    {
        public RectRole Role;
        public float X;
        public float Y;
        public float Width;
        public float Height;
    }

    public sealed class ButtonItem
    {
        public ButtonSpec Spec;
        public float X;
        public float Y;
        public float Width;
        public float Height;
        public readonly List<TextItem> Labels = new List<TextItem>();
    }

    public sealed class PanelLayoutResult
    {
        public float Width;
        public float Height;
        public bool Overflow;
        public readonly List<TextItem> Texts = new List<TextItem>();
        public readonly List<RectItem> Rects = new List<RectItem>();
        public readonly List<ButtonItem> Buttons = new List<ButtonItem>();
    }

    /// <summary>
    /// Places the texts and buttons of a <see cref="ScreenSpec"/> on a flat panel, in metres. It has no Unity types so the same
    /// layout can be checked off-engine: nothing overlaps, every line fits its width and the content fits the panel height.
    /// </summary>
    public static class PanelLayout
    {
        public const float PanelWidth = 1.8f;
        public const float PanelHeight = 1.3f;
        public const float Margin = 0.1f;
        public const float TopMargin = 0.08f;

        public const float KickerEm = 0.04f;
        public const float TitleEm = 0.11f;
        public const float BrandEm = 0.17f;
        public const float BodyEm = 0.046f;
        public const float BulletEm = 0.042f;
        public const float FooterEm = 0.03f;
        public const float HeroLabelEm = 0.085f;
        public const float HeroDescriptionEm = 0.036f;
        public const float CardLabelEm = 0.06f;
        public const float CardDescriptionEm = 0.036f;
        public const float ActionLabelEm = 0.055f;
        public const float KeyLabelEm = 0.055f;
        public const float MinimumEm = 0.03f;

        const float LineFactor = 1.4f;
        const float BlockGap = 0.03f;
        const float BulletGap = 0.022f;
        const float BulletIndent = 0.05f;
        const float WidthSafety = 1.05f;

        public static PanelLayoutResult Build(ScreenSpec spec, ITextMeasure measure)
        {
            var result = new PanelLayoutResult { Width = PanelWidth, Height = PanelHeight };
            var contentWidth = PanelWidth - 2f * Margin;
            var align = spec.Bullets.Count > 0 ? TextAlign.Left : TextAlign.Center;
            var leftEdge = -contentWidth * 0.5f;
            var textX = align == TextAlign.Left ? leftEdge : 0f;

            // Bottom block: footer hint and, when the last row is plain actions, that row.
            var bottom = -PanelHeight * 0.5f + 0.07f;
            if (!string.IsNullOrEmpty(spec.Footer))
            {
                result.Texts.Add(Line(spec.Footer, 0f, bottom, FooterEm, TextAlign.Center, TextRole.Footer));
                bottom += 0.07f;
            }

            var rows = new List<List<ButtonSpec>>(spec.ButtonRows);
            List<ButtonSpec> actionRow = null;
            if (rows.Count > 0 && IsActionRow(rows[rows.Count - 1]))
            {
                actionRow = rows[rows.Count - 1];
                rows.RemoveAt(rows.Count - 1);
            }

            var regionBottom = bottom;
            if (actionRow != null)
            {
                const float actionHeight = 0.13f;
                PlaceActionRow(result, actionRow, bottom + actionHeight * 0.5f, actionHeight, contentWidth, measure);
                regionBottom = bottom + actionHeight + 0.05f;
            }

            // Flowing block, laid out from y = 0 downwards and shifted afterwards.
            var flow = new FlowBuilder(result);
            var cursor = 0f;

            if (!string.IsNullOrEmpty(spec.Kicker))
            {
                cursor = flow.AddText(spec.Kicker, textX, cursor, KickerEm, contentWidth, align, TextRole.Kicker, 1, measure) - 0.005f;
            }
            if (!string.IsNullOrEmpty(spec.Title))
            {
                var em = spec.State == AppState.Welcome ? BrandEm : TitleEm;
                cursor = flow.AddText(spec.Title, textX, cursor, em, contentWidth, align, TextRole.Title, 1, measure) - BlockGap;
            }
            foreach (var paragraph in spec.Paragraphs)
            {
                cursor = flow.AddText(paragraph, textX, cursor, BodyEm, contentWidth, align, TextRole.Body, int.MaxValue, measure) - BlockGap;
            }
            foreach (var bullet in spec.Bullets)
            {
                var start = cursor;
                cursor = flow.AddText(bullet, textX + BulletIndent, cursor, BulletEm, contentWidth - BulletIndent, TextAlign.Left, TextRole.Bullet, int.MaxValue, measure);
                var markerSize = 0.014f;
                flow.Rects.Add(new RectItem { Role = RectRole.BulletMarker, X = textX + BulletIndent * 0.4f, Y = start - BulletEm * LineFactor * 0.5f, Width = markerSize, Height = markerSize });
                cursor -= BulletGap;
            }
            if (!string.IsNullOrEmpty(spec.FieldLabel))
            {
                cursor = flow.AddText(spec.FieldLabel, 0f, cursor, 0.038f, contentWidth, TextAlign.Center, TextRole.FieldLabel, 1, measure) - 0.008f;
                const float boxWidth = 0.95f;
                const float boxHeight = 0.12f;
                var centerY = cursor - boxHeight * 0.5f;
                flow.Rects.Add(new RectItem { Role = RectRole.FieldBox, X = 0f, Y = centerY, Width = boxWidth, Height = boxHeight });
                var value = (spec.FieldValue ?? string.Empty) + "_";
                var valueEm = FitEm(value, 0.075f, boxWidth - 0.08f, measure);
                flow.Texts.Add(Line(value, 0f, centerY, valueEm, TextAlign.Center, TextRole.FieldValue));
                cursor -= boxHeight + BlockGap;
            }
            if (!string.IsNullOrEmpty(spec.Info))
            {
                cursor = flow.AddText(spec.Info, 0f, cursor, BodyEm, contentWidth, TextAlign.Center, TextRole.Info, 1, measure) - BlockGap;
            }
            if (!string.IsNullOrEmpty(spec.Notice))
            {
                cursor = flow.AddText(spec.Notice, 0f, cursor, BodyEm, contentWidth, TextAlign.Center, TextRole.Notice, 2, measure) - BlockGap;
            }
            foreach (var row in spec.KeyRows)
            {
                cursor = flow.AddKeyRow(row, cursor, contentWidth, measure) - 0.012f;
            }
            if (spec.KeyRows.Count > 0) cursor -= 0.01f;
            foreach (var row in rows)
            {
                cursor = flow.AddButtonRow(row, cursor, contentWidth, measure) - 0.025f;
            }

            var height = -cursor;
            var available = (PanelHeight * 0.5f - TopMargin) - regionBottom;
            float shift;
            if (height > available)
            {
                result.Overflow = true;
                shift = PanelHeight * 0.5f - TopMargin;
            }
            else
            {
                shift = PanelHeight * 0.5f - TopMargin - (available - height) * 0.5f;
            }
            flow.Commit(shift);
            return result;
        }

        static bool IsActionRow(List<ButtonSpec> row)
        {
            foreach (var button in row)
            {
                if (button.Kind != ButtonKind.Primary && button.Kind != ButtonKind.Secondary) return false;
            }
            return row.Count > 0;
        }

        static void PlaceActionRow(PanelLayoutResult result, List<ButtonSpec> row, float centerY, float height, float contentWidth, ITextMeasure measure)
        {
            const float gap = 0.08f;
            var em = ActionLabelEm;
            var widths = new float[row.Count];
            for (var attempt = 0; attempt < 6; attempt++)
            {
                var total = gap * (row.Count - 1);
                for (var i = 0; i < row.Count; i++)
                {
                    widths[i] = Math.Max(0.5f, measure.Width(row[i].Label, em) * WidthSafety + 0.16f);
                    total += widths[i];
                }
                if (total <= contentWidth) break;
                em *= 0.9f;
            }

            var sum = gap * (row.Count - 1);
            foreach (var w in widths) sum += w;
            var x = -sum * 0.5f;
            for (var i = 0; i < row.Count; i++)
            {
                var item = new ButtonItem { Spec = row[i], X = x + widths[i] * 0.5f, Y = centerY, Width = widths[i], Height = height };
                item.Labels.Add(Line(row[i].Label, item.X, centerY, em, TextAlign.Center, TextRole.ButtonLabel));
                result.Buttons.Add(item);
                x += widths[i] + gap;
            }
        }

        public static float FitEm(string text, float em, float maxWidth, ITextMeasure measure)
        {
            var width = measure.Width(text, em) * WidthSafety;
            if (width <= maxWidth || width <= 0f) return em;
            return Math.Max(MinimumEm, em * maxWidth / width);
        }

        public static List<string> Wrap(string text, float em, float maxWidth, ITextMeasure measure)
        {
            var lines = new List<string>();
            var words = (text ?? string.Empty).Split(' ');
            var current = string.Empty;
            foreach (var word in words)
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && measure.Width(candidate, em) * WidthSafety > maxWidth)
                {
                    lines.Add(current);
                    current = word;
                }
                else
                {
                    current = candidate;
                }
            }
            if (current.Length > 0) lines.Add(current);
            return lines;
        }

        static TextItem Line(string text, float x, float y, float em, TextAlign align, TextRole role) =>
            new TextItem { Text = text, X = x, Y = y, Em = em, Align = align, Role = role };

        /// <summary>Collects items with relative Y (0 at the top, negative downwards) and moves them into place once the total height is known.</summary>
        sealed class FlowBuilder
        {
            readonly PanelLayoutResult result;
            public readonly List<TextItem> Texts = new List<TextItem>();
            public readonly List<RectItem> Rects = new List<RectItem>();
            readonly List<ButtonItem> buttons = new List<ButtonItem>();

            public FlowBuilder(PanelLayoutResult result) => this.result = result;

            /// <summary>Adds wrapped text starting at <paramref name="top"/> and returns the new cursor below it.</summary>
            public float AddText(string text, float x, float top, float em, float maxWidth, TextAlign align, TextRole role, int maxLines, ITextMeasure measure = null)
            {
                em = measure != null && maxLines == 1 ? FitEm(text, em, maxWidth, measure) : em;
                var lines = measure != null ? Wrap(text, em, maxWidth, measure) : new List<string> { text };
                var lineHeight = em * LineFactor;
                foreach (var line in lines)
                {
                    Texts.Add(Line(line, x, top - lineHeight * 0.5f, em, align, role));
                    top -= lineHeight;
                }
                return top;
            }

            public float AddKeyRow(List<ButtonSpec> row, float top, float contentWidth, ITextMeasure measure)
            {
                const float keyHeight = 0.09f;
                const float gap = 0.014f;
                var widths = new float[row.Count];
                var total = gap * (row.Count - 1);
                for (var i = 0; i < row.Count; i++)
                {
                    widths[i] = row[i].Label.Length == 1 ? 0.13f : Math.Max(0.22f, measure.Width(row[i].Label, KeyLabelEm) * WidthSafety + 0.08f);
                    total += widths[i];
                }
                var x = -total * 0.5f;
                for (var i = 0; i < row.Count; i++)
                {
                    var item = new ButtonItem { Spec = row[i], X = x + widths[i] * 0.5f, Y = top - keyHeight * 0.5f, Width = widths[i], Height = keyHeight };
                    item.Labels.Add(Line(row[i].Label, item.X, item.Y, FitEm(row[i].Label, KeyLabelEm, widths[i] - 0.03f, measure), TextAlign.Center, TextRole.ButtonLabel));
                    buttons.Add(item);
                    x += widths[i] + gap;
                }
                return top - keyHeight;
            }

            public float AddButtonRow(List<ButtonSpec> row, float top, float contentWidth, ITextMeasure measure)
            {
                var first = row[0].Kind;
                var hero = first == ButtonKind.Hero;
                var gap = hero ? 0.07f : 0.06f;
                var width = (contentWidth - gap * (row.Count - 1)) / row.Count;
                var height = hero ? 0.5f : 0.26f;
                var labelEm = hero ? HeroLabelEm : CardLabelEm;
                var descriptionEm = hero ? HeroDescriptionEm : CardDescriptionEm;
                var x = -contentWidth * 0.5f + width * 0.5f;
                foreach (var spec in row)
                {
                    var item = new ButtonItem { Spec = spec, X = x, Y = top - height * 0.5f, Width = width, Height = height };
                    var innerWidth = width - 0.12f;
                    var labelTop = top - (hero ? 0.07f : 0.035f);
                    var labelEmFit = FitEm(spec.Label, labelEm, innerWidth, measure);
                    var labelHeight = labelEmFit * LineFactor;
                    item.Labels.Add(Line(spec.Label, x, labelTop - labelHeight * 0.5f, labelEmFit, TextAlign.Center, TextRole.ButtonLabel));
                    if (!string.IsNullOrEmpty(spec.Description))
                    {
                        var lineTop = labelTop - labelHeight - 0.02f;
                        foreach (var line in Wrap(spec.Description, descriptionEm, innerWidth, measure))
                        {
                            item.Labels.Add(Line(line, x, lineTop - descriptionEm * LineFactor * 0.5f, descriptionEm, TextAlign.Center, TextRole.ButtonDescription));
                            lineTop -= descriptionEm * LineFactor;
                        }
                    }
                    buttons.Add(item);
                    x += width + gap;
                }
                return top - height;
            }

            public void Commit(float shift)
            {
                foreach (var t in Texts) t.Y += shift;
                foreach (var r in Rects) r.Y += shift;
                foreach (var b in buttons)
                {
                    b.Y += shift;
                    foreach (var l in b.Labels) l.Y += shift;
                }
                result.Texts.AddRange(Texts);
                result.Rects.AddRange(Rects);
                result.Buttons.AddRange(buttons);
            }
        }
    }
}
