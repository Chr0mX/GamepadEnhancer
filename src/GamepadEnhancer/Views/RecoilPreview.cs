using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace GamepadEnhancer
{
    /// <summary>Scales to whatever width it is given; draws the recoil path on an X/Y grid.</summary>
    public sealed class RecoilPreview : Control
    {
        private IReadOnlyList<RecoilStep> steps = Array.Empty<RecoilStep>();

        public RecoilPreview()
        {
            ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        }

        public void SetSteps(IReadOnlyList<RecoilStep> value)
        {
            steps = value ?? Array.Empty<RecoilStep>();
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double w = double.IsInfinity(availableSize.Width) ? 320 : availableSize.Width;
            return new Size(w, Math.Clamp(w * 0.6, 200, 380));
        }

        public override void Render(DrawingContext context)
        {
            bool dark = ActualThemeVariant != ThemeVariant.Light;
            var card = Brush(dark ? "#21232A" : "#FFFFFF");
            var border = new Pen(Brush(dark ? "#373A44" : "#E2E5EA"), 1);
            var axis = new Pen(Brush(dark ? "#30333C" : "#EBEDF1"), 1);
            var line = new Pen(Brush(dark ? "#6C8CFF" : "#4C6FFF"), 2);
            var danger = Brush(dark ? "#FF6E6E" : "#D64545");
            var success = Brush(dark ? "#4CD787" : "#229954");
            var muted = Brush(dark ? "#9BA1AD" : "#6B7280");
            var text = Brush(dark ? "#E6E7EB" : "#1F2430");

            double w = Bounds.Width, h = Bounds.Height;
            context.DrawRectangle(card, border, new Rect(0.5, 0.5, Math.Max(0, w - 1), Math.Max(0, h - 1)), 6, 6);
            context.DrawLine(axis, new Point(w / 2, 0), new Point(w / 2, h));
            context.DrawLine(axis, new Point(0, h / 2), new Point(w, h / 2));
            DrawText(context, "X", muted, w - 16, h / 2 + 2);
            DrawText(context, "Y", muted, w / 2 + 4, 2);

            if (steps.Count == 0)
            {
                DrawText(context, "No steps defined", muted, 10, Math.Max(12, h / 2 - 8));
                return;
            }

            var pts = new Point[steps.Count];
            for (int i = 0; i < pts.Length; i++)
            {
                double x = (steps[i].X + 100) / 200.0 * (w - 1);
                double y = (100 - steps[i].Y) / 200.0 * (h - 1);
                pts[i] = new Point(Math.Clamp(x, 0, w), Math.Clamp(y, 0, h));
            }
            for (int i = 1; i < pts.Length; i++) context.DrawLine(line, pts[i - 1], pts[i]);
            for (int i = 0; i < pts.Length; i++)
            {
                context.DrawEllipse(i == 0 ? success : danger, null, pts[i], 4, 4);
                if (pts.Length <= 30)
                    DrawText(context, i.ToString(CultureInfo.InvariantCulture), text, pts[i].X + 6, pts[i].Y - 9);
            }
        }

        private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

        private static void DrawText(DrawingContext ctx, string s, IBrush brush, double x, double y)
        {
            var ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                Typeface.Default, 12, brush);
            ctx.DrawText(ft, new Point(x, y));
        }
    }
}
