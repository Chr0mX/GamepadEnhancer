using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace GamepadEnhancer
{
    /// <summary>Shared drawing helpers for the small measurement views (colours come from the theme tokens).</summary>
    internal abstract class InstrumentView : Control
    {
        private static readonly Typeface Mono = new(new FontFamily("avares://GamepadEnhancer/Assets/Fonts#JetBrains Mono, Cascadia Code, Consolas, monospace"));

        protected IBrush Res(string key, IBrush fallback) =>
            this.TryFindResource(key, ActualThemeVariant, out object v) && v is IBrush b ? b : fallback;

        protected FormattedText Text(string s, double size, IBrush brush) =>
            new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, size, brush);

        protected Pen PenOf(IBrush brush, double thickness, bool dashed = false) =>
            new(brush, thickness, dashed ? new DashStyle(new double[] { 3, 3 }, 0) : null);
    }

    /// <summary>
    /// The mouse-look response curve: stick output against mouse speed, drawn with the same <see cref="MouseLook.Shape"/> the
    /// engine uses, so it always shows what the current curve and game dead zone really do. Both axes are percent of full tilt.
    /// </summary>
    internal sealed class CurvePlot : InstrumentView
    {
        private double curve = 1, deadZone;

        public double Curve { get => curve; set { if (curve != value) { curve = value; InvalidateVisual(); } } }
        /// <summary>Game dead zone as a fraction (0..0.5).</summary>
        public double DeadZone { get => deadZone; set { if (deadZone != value) { deadZone = value; InvalidateVisual(); } } }

        public override void Render(DrawingContext c)
        {
            IBrush bg = Res("CanvasBrush", Brushes.Black), line = Res("BorderBrushDefault", Brushes.Gray), grid = Res("BorderBrushSubtle", Brushes.DimGray),
                   dim = Res("MutedTextBrush", Brushes.Gray), accent = Res("AccentBrush", Brushes.DodgerBlue), faint = Res("FaintTextBrush", Brushes.Gray);
            var r = new Rect(34, 8, Math.Max(10, Bounds.Width - 46), Math.Max(10, Bounds.Height - 30));
            c.DrawRectangle(bg, PenOf(line, 1), r);
            foreach (double f in new[] { 0.25, 0.5, 0.75 })
            {
                c.DrawLine(PenOf(grid, 1), new Point(r.Left + r.Width * f, r.Top), new Point(r.Left + r.Width * f, r.Bottom));
                c.DrawLine(PenOf(grid, 1), new Point(r.Left, r.Bottom - r.Height * f), new Point(r.Right, r.Bottom - r.Height * f));
            }
            c.DrawLine(PenOf(faint, 1, true), new Point(r.Left, r.Bottom), new Point(r.Right, r.Top));   // linear reference

            var g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                ctx.BeginFigure(new Point(r.Left, r.Bottom), false);
                for (int i = 1; i <= 100; i++)
                {
                    double y = MouseLook.Shape(i, curve, deadZone);
                    ctx.LineTo(new Point(r.Left + r.Width * i / 100.0, r.Bottom - r.Height * y / 100.0));
                }
                ctx.EndFigure(false);
            }
            c.DrawGeometry(null, PenOf(accent, 1.8), g);

            foreach (string t in new[] { "0", "50", "100" })
            {
                double f = double.Parse(t, CultureInfo.InvariantCulture) / 100.0;
                FormattedText ft = Text(t, 10, dim);
                c.DrawText(ft, new Point(r.Left + r.Width * f - ft.Width / 2, r.Bottom + 3));
                FormattedText fy = Text(t, 10, dim);
                c.DrawText(fy, new Point(r.Left - fy.Width - 4, r.Bottom - r.Height * f - fy.Height / 2));
            }
            c.DrawText(Text("mouse speed x sensitivity (%)", 10, faint), new Point(r.Left, r.Bottom + 15));
            if (deadZone > 0)
            {
                double y = r.Bottom - r.Height * deadZone;
                c.DrawLine(PenOf(faint, 1, true), new Point(r.Left, y), new Point(r.Left + r.Width * 0.12, y));
            }
        }
    }

    /// <summary>A stick gate: crosshair, the game dead-zone ring and the current stick position (-1..1; +Y is down, as in the monitor).</summary>
    internal sealed class StickScope : InstrumentView
    {
        private double x, y, deadZone;

        public double DeadZone { get => deadZone; set { if (deadZone != value) { deadZone = value; InvalidateVisual(); } } }

        /// <summary>Sets the stick position; returns true when it changed (so the caller can skip redundant work).</summary>
        public bool SetPosition(double nx, double ny)
        {
            if (nx == x && ny == y) return false;
            x = nx; y = ny; InvalidateVisual();
            return true;
        }

        public override void Render(DrawingContext c)
        {
            IBrush bg = Res("CanvasBrush", Brushes.Black), line = Res("BorderBrushDefault", Brushes.Gray), grid = Res("BorderBrushSubtle", Brushes.DimGray),
                   dim = Res("MutedTextBrush", Brushes.Gray), accent = Res("AccentBrush", Brushes.DodgerBlue), faint = Res("FaintTextBrush", Brushes.Gray);
            double size = Math.Min(Bounds.Width, Bounds.Height - 18);
            if (size < 20) return;
            var centre = new Point(Bounds.Width / 2, size / 2 + 2);
            double radius = size / 2 - 3;
            c.DrawEllipse(bg, PenOf(line, 1), centre, radius, radius);
            c.DrawLine(PenOf(grid, 1), new Point(centre.X - radius, centre.Y), new Point(centre.X + radius, centre.Y));
            c.DrawLine(PenOf(grid, 1), new Point(centre.X, centre.Y - radius), new Point(centre.X, centre.Y + radius));
            c.DrawEllipse(null, PenOf(grid, 1), centre, radius / 2, radius / 2);
            if (deadZone > 0) c.DrawEllipse(null, PenOf(faint, 1, true), centre, Math.Max(1, radius * deadZone * 2), Math.Max(1, radius * deadZone * 2));

            double mag = Math.Sqrt(x * x + y * y), px = x, py = y;
            if (mag > 1) { px /= mag; py /= mag; }
            var dot = new Point(centre.X + px * radius, centre.Y + py * radius);
            c.DrawLine(PenOf(accent, 1.2), centre, dot);
            c.DrawEllipse(accent, null, dot, 4, 4);
            string s = string.Format(CultureInfo.InvariantCulture, "X {0:+0.00;-0.00;+0.00}   Y {1:+0.00;-0.00;+0.00}", x, y);
            FormattedText ft = Text(s, 11, dim);
            c.DrawText(ft, new Point((Bounds.Width - ft.Width) / 2, size + 4));
        }
    }

    /// <summary>A horizontal bar meter with its percentage written on it (trigger travel).</summary>
    internal sealed class BarMeter : InstrumentView
    {
        private double fraction;
        private string colorKey = "AccentBrush";

        public double Fraction { get => fraction; set { double v = Math.Clamp(double.IsNaN(value) ? 0 : value, 0, 1); if (v != fraction) { fraction = v; InvalidateVisual(); } } }
        public string ColorKey { get => colorKey; set { if (colorKey != value) { colorKey = value; InvalidateVisual(); } } }

        public override void Render(DrawingContext c)
        {
            var r = new Rect(0, 0, Bounds.Width, Bounds.Height);
            c.DrawRectangle(Res("CanvasBrush", Brushes.Black), PenOf(Res("BorderBrushDefault", Brushes.Gray), 1), r, 6, 6);
            if (fraction > 0) c.DrawRectangle(Res(colorKey, Brushes.DodgerBlue), null, new Rect(1, 1, Math.Max(0, (r.Width - 2) * fraction), r.Height - 2), 5, 5);
            FormattedText t = Text((fraction * 100).ToString("0.0", CultureInfo.InvariantCulture) + " %", 10, Res("TextBrush", Brushes.White));
            c.DrawText(t, new Point((r.Width - t.Width) / 2, (r.Height - t.Height) / 2));
        }
    }

    /// <summary>
    /// A square stick plot: gate circle, grid, a fading trail of the most recent positions and the current point. Fed with the
    /// final stick output (-1..1, +Y down as in the monitor). Positions are kept in a small ring so memory is fixed.
    /// </summary>
    internal sealed class StickPlot : InstrumentView
    {
        private const int Capacity = 180;
        private readonly (double X, double Y)[] trail = new (double, double)[Capacity];
        private int head, count;
        private double x, y;

        public void Add(double nx, double ny)
        {
            x = nx; y = ny;
            if (count > 0)
            {
                var last = trail[(head - 1 + Capacity) % Capacity];
                if (last.X == nx && last.Y == ny && nx == 0 && ny == 0) { InvalidateVisual(); return; }   // centred and still: nothing to add
            }
            trail[head] = (nx, ny);
            head = (head + 1) % Capacity;
            if (count < Capacity) count++;
            InvalidateVisual();
        }

        public void Clear() { head = count = 0; x = y = 0; InvalidateVisual(); }

        public override void Render(DrawingContext c)
        {
            IBrush bg = Res("CanvasBrush", Brushes.Black), line = Res("BorderBrushDefault", Brushes.Gray), dim = Res("MutedTextBrush", Brushes.Gray),
                   accent = Res("AccentBrush", Brushes.DodgerBlue), red = Res("DangerBrush", Brushes.Red);
            double label = 22;
            double size = Math.Min(Bounds.Width, Bounds.Height - label);
            if (size < 40) return;
            double left = (Bounds.Width - size) / 2;
            var box = new Rect(left, 0, size, size);
            c.DrawRectangle(bg, null, box, 8, 8);
            var centre = new Point(left + size / 2, size / 2);
            double r = size / 2 - 6;
            var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)), 1);
            c.DrawLine(gridPen, new Point(centre.X - r, centre.Y), new Point(centre.X + r, centre.Y));
            c.DrawLine(gridPen, new Point(centre.X, centre.Y - r), new Point(centre.X, centre.Y + r));
            c.DrawEllipse(null, gridPen, centre, r / 2, r / 2);
            c.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(102, 79, 140, 255)), 1.2), centre, r, r);

            for (int i = 0; i < count; i++)
            {
                var p = trail[(head - count + i + Capacity) % Capacity];
                double a = 0.15 + 0.6 * i / Math.Max(1, count - 1);
                double mag = Math.Sqrt(p.X * p.X + p.Y * p.Y), px = p.X, py = p.Y;
                if (mag > 1) { px /= mag; py /= mag; }
                c.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(255 * a), 79, 140, 255)), null, new Point(centre.X + px * r, centre.Y + py * r), 1.6, 1.6);
            }
            double m = Math.Sqrt(x * x + y * y), cx = x, cy = y;
            if (m > 1) { cx /= m; cy /= m; }
            c.DrawEllipse(red, null, new Point(centre.X + cx * r, centre.Y + cy * r), 4, 4);

            string t = string.Format(CultureInfo.InvariantCulture, "X: {0:+0.000;-0.000;+0.000}   Y: {1:+0.000;-0.000;+0.000}", x, y);
            FormattedText ft = Text(t, 11, dim);
            c.DrawText(ft, new Point(left + (size - ft.Width) / 2, size + 5));
        }
    }
}
