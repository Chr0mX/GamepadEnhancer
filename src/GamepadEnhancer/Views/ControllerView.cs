using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace GamepadEnhancer
{
    /// <summary>
    /// Draws a controller from a <see cref="PadState"/>: an Xbox 360 pad or a DualShock 4, each with its own body, stick
    /// and button positions (not one layout with different labels). It only paints what it is given; the host decides
    /// which pipeline stage (physical / processed / final) to feed it. Painting is skipped when nothing changed.
    /// </summary>
    internal sealed class ControllerView : Control
    {
        /// <summary>Logical drawing size; the control scales this to whatever size it is given.</summary>
        public const double CanvasW = 440, CanvasH = 300;

        /// <summary>Return-to-centre easing of the stick knobs (movement away from the centre is never delayed).</summary>
        private const double StickReturnTauMs = 45.0;

        private static IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex));

        private static readonly IBrush Backdrop = B("#101218");
        private static readonly IBrush BodyFill = B("#22252E");
        private static readonly IBrush Idle = B("#343844");
        private static readonly IBrush IdleDark = B("#2A2D37");
        private static readonly IBrush TextDim = B("#9AA1B0");
        private static readonly IBrush TextBright = B("#F2F4F8");
        private static readonly IBrush Accent = B("#6C8CFF");
        private static readonly Pen BodyPen = new(B("#3A3E4A"), 2);
        private static readonly Pen IdlePen = new(B("#4A4F5E"), 1.5);
        private static readonly Pen AxisPen = new(B("#3B3F4C"), 1);
        private static readonly Pen ClickRingPen = new(Accent, 3);
        private static readonly IBrush Ink = B("#101218");
        private static readonly IBrush KnobEdge = B("#6A7186");
        private static readonly Pen KnobPen = new(KnobEdge, 2);
        private static readonly Pen KnobClickPen = new(TextBright, 2);

        // Face button colours (Xbox: A green, B red, X blue, Y yellow; DualShock 4: cross blue, circle red, square pink, triangle green).
        private static readonly IBrush XboxA = B("#4CD787"), XboxB = B("#FF5C5C"), XboxX = B("#5C8CFF"), XboxY = B("#FFD24C");
        private static readonly IBrush PsCross = B("#6C9CFF"), PsCircle = B("#FF6E6E"), PsSquare = B("#FF8AD4"), PsTriangle = B("#4CD7A8");
        private static readonly Dictionary<IBrush, Pen> GlyphPens = new()
        {
            [PsCross] = new Pen(PsCross, 2.4), [PsCircle] = new Pen(PsCircle, 2.4),
            [PsSquare] = new Pen(PsSquare, 2.4), [PsTriangle] = new Pen(PsTriangle, 2.4),
            [Ink] = new Pen(Ink, 2.4)
        };

        private readonly Dictionary<(string, double, IBrush), FormattedText> textCache = new();

        private PadFamily family = PadFamily.DualShock4;
        private bool showLabels = true;
        private bool transparentBackground;
        private bool dirty = true;
        private double dLx, dLy, dRx, dRy;   // displayed stick values (eased on return to centre)

        public PadState State { get; private set; } = PadState.Neutral;

        public PadFamily Family
        {
            get => family;
            set { if (family != value) { family = value; dirty = true; InvalidateVisual(); } }
        }

        /// <summary>Button names and trigger percentages. Off = compact drawing.</summary>
        public bool ShowLabels
        {
            get => showLabels;
            set { if (showLabels != value) { showLabels = value; dirty = true; InvalidateVisual(); } }
        }

        /// <summary>Skip the dark backdrop (overlay window mode).</summary>
        public bool TransparentBackground
        {
            get => transparentBackground;
            set { if (transparentBackground != value) { transparentBackground = value; dirty = true; InvalidateVisual(); } }
        }

        /// <summary>
        /// Feeds the next state. Repaints only if the state or an eased stick changed.
        /// Returns true while a stick is still easing back to its target (the host should keep ticking).
        /// </summary>
        public bool Update(PadState next, double dtMs)
        {
            next ??= PadState.Neutral;
            bool stateChanged = !next.SameAs(State);
            State = next;

            double nLx = PadStateMath.Relax(dLx, next.Lx, dtMs, StickReturnTauMs);
            double nLy = PadStateMath.Relax(dLy, next.Ly, dtMs, StickReturnTauMs);
            double nRx = PadStateMath.Relax(dRx, next.Rx, dtMs, StickReturnTauMs);
            double nRy = PadStateMath.Relax(dRy, next.Ry, dtMs, StickReturnTauMs);
            bool moved = nLx != dLx || nLy != dLy || nRx != dRx || nRy != dRy;
            dLx = nLx; dLy = nLy; dRx = nRx; dRy = nRy;

            if (stateChanged || moved || dirty)
            {
                dirty = false;
                InvalidateVisual();
            }
            return dLx != next.Lx || dLy != next.Ly || dRx != next.Rx || dRy != next.Ry;
        }

        // ── Drawing ─────────────────────────────────────────────────────────────

        public override void Render(DrawingContext context)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w < 2 || h < 2) return;
            double scale = Math.Min(w / CanvasW, h / CanvasH);
            double ox = (w - CanvasW * scale) / 2.0, oy = (h - CanvasH * scale) / 2.0;

            using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(ox, oy)))
            {
                if (!transparentBackground)
                    context.DrawRectangle(Backdrop, null, new Rect(0, 0, CanvasW, CanvasH), 14, 14);

                if (family == PadFamily.Xbox360) DrawXbox(context);
                else DrawDs4(context);

                if (showLabels)
                    DrawText(context, family == PadFamily.Xbox360 ? "XBOX 360" : "DUALSHOCK 4", 11, TextDim, new Point(14, 278), false);
            }
        }

        private bool Down(int action) => State.Has(action);

        private void DrawXbox(DrawingContext c)
        {
            // Body: wide centre with two long grips.
            c.DrawEllipse(BodyFill, null, new Point(112, 190), 52, 84);
            c.DrawEllipse(BodyFill, null, new Point(328, 190), 52, 84);
            c.DrawRectangle(BodyFill, BodyPen, new Rect(80, 60, 280, 140), 60, 60);

            DrawTrigger(c, new Rect(95, 14, 64, 30), 6, State.Lt, "LT");
            DrawTrigger(c, new Rect(281, 14, 64, 30), 7, State.Rt, "RT");
            DrawShoulder(c, new Rect(88, 48, 80, 16), 4, "LB");
            DrawShoulder(c, new Rect(272, 48, 80, 16), 5, "RB");

            DrawRound(c, new Point(220, 80), 13, 16, "X", 13);                 // Guide
            DrawRound(c, new Point(178, 104), 8, 8, ShowName("View"), 0);       // View
            DrawRound(c, new Point(262, 104), 8, 9, ShowName("Menu"), 0);       // Menu

            DrawStick(c, new Point(135, 118), 28, dLx, dLy, 10, "L3");
            DrawStick(c, new Point(262, 178), 28, dRx, dRy, 11, "R3");
            DrawDpad(c, new Point(178, 178));

            var f = new Point(322, 118);
            DrawFace(c, new Point(f.X, f.Y - 24), 13, 3, XboxY, "Y", GlyphKind.None);
            DrawFace(c, new Point(f.X + 24, f.Y), 13, 1, XboxB, "B", GlyphKind.None);
            DrawFace(c, new Point(f.X, f.Y + 24), 13, 0, XboxA, "A", GlyphKind.None);
            DrawFace(c, new Point(f.X - 24, f.Y), 13, 2, XboxX, "X", GlyphKind.None);
        }

        private void DrawDs4(DrawingContext c)
        {
            c.DrawEllipse(BodyFill, null, new Point(105, 205), 44, 78);
            c.DrawEllipse(BodyFill, null, new Point(335, 205), 44, 78);
            c.DrawRectangle(BodyFill, BodyPen, new Rect(75, 70, 290, 130), 42, 42);

            DrawTrigger(c, new Rect(95, 14, 64, 30), 6, State.Lt, "L2");
            DrawTrigger(c, new Rect(281, 14, 64, 30), 7, State.Rt, "R2");
            DrawShoulder(c, new Rect(90, 48, 80, 16), 4, "L1");
            DrawShoulder(c, new Rect(270, 48, 80, 16), 5, "R1");

            // Touchpad: drawn for recognisability only (the app does not drive it).
            c.DrawRectangle(IdleDark, IdlePen, new Rect(160, 76, 120, 62), 10, 10);

            DrawPill(c, new Rect(116, 78, 26, 12), 8, "Share");
            DrawPill(c, new Rect(298, 78, 26, 12), 9, "Options");
            DrawRound(c, new Point(220, 160), 9, 16, "PS", 11);

            DrawDpad(c, new Point(120, 125));
            DrawStick(c, new Point(178, 200), 28, dLx, dLy, 10, "L3");
            DrawStick(c, new Point(262, 200), 28, dRx, dRy, 11, "R3");

            var f = new Point(320, 122);
            DrawFace(c, new Point(f.X, f.Y - 24), 12, 3, PsTriangle, "", GlyphKind.Triangle);
            DrawFace(c, new Point(f.X + 24, f.Y), 12, 1, PsCircle, "", GlyphKind.Circle);
            DrawFace(c, new Point(f.X, f.Y + 24), 12, 0, PsCross, "", GlyphKind.Cross);
            DrawFace(c, new Point(f.X - 24, f.Y), 12, 2, PsSquare, "", GlyphKind.Square);
        }

        private string ShowName(string name) => showLabels ? name : "";

        private void DrawTrigger(DrawingContext c, Rect r, int action, double value, string label)
        {
            value = Math.Clamp(value, 0.0, 1.0);
            c.DrawRectangle(IdleDark, IdlePen, r, 7, 7);
            if (value > 0.0)
            {
                double fh = r.Height * value;
                c.DrawRectangle(Accent, null, new Rect(r.X + 1, r.Bottom - fh, r.Width - 2, Math.Max(0, fh - 1)), 6, 6);
            }
            DrawText(c, label, 12, value > 0.5 ? TextBright : TextDim, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), true);
            if (showLabels && value > 0.005)
                DrawText(c, ((int)Math.Round(value * 100)).ToString(CultureInfo.InvariantCulture) + "%", 10, TextBright,
                    new Point(r.X + r.Width / 2, r.Y - 6), true);
        }

        private void DrawShoulder(DrawingContext c, Rect r, int action, string label)
        {
            bool on = Down(action);
            c.DrawRectangle(on ? Accent : Idle, on ? null : IdlePen, r, 8, 8);
            DrawText(c, label, 11, on ? TextBright : TextDim, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), true);
        }

        private void DrawPill(DrawingContext c, Rect r, int action, string label)
        {
            bool on = Down(action);
            c.DrawRectangle(on ? Accent : Idle, on ? null : IdlePen, r, 5, 5);
            if (showLabels) DrawText(c, label, 9, on ? TextBright : TextDim, new Point(r.X + r.Width / 2, r.Bottom + 8), true);
        }

        /// <summary>A small round button (Guide / PS / View / Menu). <paramref name="text"/> is drawn centred inside (if any).</summary>
        private void DrawRound(DrawingContext c, Point center, double radius, int action, string text, double textSize)
        {
            bool on = Down(action);
            c.DrawEllipse(on ? Accent : Idle, on ? null : IdlePen, center, radius, radius);
            if (!string.IsNullOrEmpty(text) && textSize > 0)
                DrawText(c, text, textSize, on ? TextBright : TextDim, center, true);
            else if (!string.IsNullOrEmpty(text) && showLabels)
                DrawText(c, text, 9, on ? TextBright : TextDim, new Point(center.X, center.Y + radius + 8), true);
        }

        private void DrawStick(DrawingContext c, Point center, double radius, double x, double y, int clickAction, string label)
        {
            bool click = Down(clickAction);
            c.DrawEllipse(IdleDark, click ? ClickRingPen : IdlePen, center, radius, radius);
            c.DrawLine(AxisPen, new Point(center.X - radius + 4, center.Y), new Point(center.X + radius - 4, center.Y));
            c.DrawLine(AxisPen, new Point(center.X, center.Y - radius + 4), new Point(center.X, center.Y + radius - 4));

            double mag = Math.Sqrt(x * x + y * y);
            if (mag > 1.0) { x /= mag; y /= mag; }   // keep the knob inside the gate
            double travel = radius * 0.5;
            var knob = new Point(center.X + x * travel, center.Y - y * travel);   // +Y is up
            c.DrawEllipse(click ? Accent : Idle, click ? KnobClickPen : KnobPen, knob, radius * 0.55, radius * 0.55);
            if (showLabels) DrawText(c, label, 9, click ? TextBright : TextDim, knob, true);
        }

        private void DrawDpad(DrawingContext c, Point o)
        {
            DrawArm(c, new Rect(o.X - 7, o.Y - 22, 14, 15), PadStateMath.ActionDpadUp);
            DrawArm(c, new Rect(o.X - 7, o.Y + 7, 14, 15), PadStateMath.ActionDpadDown);
            DrawArm(c, new Rect(o.X - 22, o.Y - 7, 15, 14), PadStateMath.ActionDpadLeft);
            DrawArm(c, new Rect(o.X + 7, o.Y - 7, 15, 14), PadStateMath.ActionDpadRight);
            c.DrawRectangle(Idle, null, new Rect(o.X - 7, o.Y - 7, 14, 14));
        }

        private void DrawArm(DrawingContext c, Rect r, int action)
        {
            bool on = Down(action);
            c.DrawRectangle(on ? Accent : Idle, on ? null : IdlePen, r, 3, 3);
        }

        private enum GlyphKind { None, Cross, Circle, Square, Triangle }

        private void DrawFace(DrawingContext c, Point center, double r, int action, IBrush color, string letter, GlyphKind glyph)
        {
            bool on = Down(action);
            c.DrawEllipse(on ? color : Idle, on ? null : IdlePen, center, r, r);
            IBrush ink = on ? Ink : color;
            if (glyph == GlyphKind.None)
            {
                DrawText(c, letter, 14, on ? Ink : color, center, true);
                return;
            }
            Pen pen = GlyphPens[ink];
            double s = r * 0.5;
            switch (glyph)
            {
                case GlyphKind.Cross:
                    c.DrawLine(pen, new Point(center.X - s, center.Y - s), new Point(center.X + s, center.Y + s));
                    c.DrawLine(pen, new Point(center.X - s, center.Y + s), new Point(center.X + s, center.Y - s));
                    break;
                case GlyphKind.Circle:
                    c.DrawEllipse(null, pen, center, s * 1.1, s * 1.1);
                    break;
                case GlyphKind.Square:
                    c.DrawRectangle(null, pen, new Rect(center.X - s, center.Y - s, s * 2, s * 2));
                    break;
                case GlyphKind.Triangle:
                    var g = new StreamGeometry();
                    using (StreamGeometryContext gc = g.Open())
                    {
                        gc.BeginFigure(new Point(center.X, center.Y - s * 1.15), false);
                        gc.LineTo(new Point(center.X + s * 1.05, center.Y + s * 0.8));
                        gc.LineTo(new Point(center.X - s * 1.05, center.Y + s * 0.8));
                        gc.EndFigure(true);
                    }
                    c.DrawGeometry(null, pen, g);
                    break;
            }
        }

        private void DrawText(DrawingContext c, string text, double size, IBrush brush, Point at, bool centered)
        {
            if (string.IsNullOrEmpty(text)) return;
            var key = (text, size, brush);
            if (!textCache.TryGetValue(key, out FormattedText ft))
            {
                ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush);
                if (textCache.Count > 400) textCache.Clear();   // percentages and labels only; this is a safety net
                textCache[key] = ft;
            }
            c.DrawText(ft, centered ? new Point(at.X - ft.Width / 2, at.Y - ft.Height / 2) : at);
        }
    }
}
