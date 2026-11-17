using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace GamepadEnhancer
{
    /// <summary>
    /// The interactive controller configuration diagram: a drawn pad (DualShock 4 or Xbox 360) with a marker on every control, a label
    /// chip showing its current mapping, and a rear view with the four paddles. Click (or arrow-key) a marker or chip to select it;
    /// the host shows the editor. Pressed controls light up, the selected one is outlined and a just-remapped one flashes. It only paints
    /// what the host gives it and scales to whatever size it is placed in.
    /// </summary>
    internal sealed class ControllerConfigView : Control
    {
        private static IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex));
        private static IBrush A(string hex, byte alpha) { Color c = Color.Parse(hex); return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B)); }

        private static LinearGradientBrush Vertical(string top, string bottom) => new()
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops { new GradientStop(Color.Parse(top), 0), new GradientStop(Color.Parse(bottom), 1) }
        };

        private static readonly IBrush BackdropTop = B("#23273B"), TextDim = B("#9AA3C0"), TextBright = B("#F4F6FC"), Accent = B("#4CC9F0"), Good = B("#4CD787");
        private static readonly IBrush BackdropFill = Vertical("#2B3047", "#1E2236");
        private static readonly IBrush BodyFill = Vertical("#4C5474", "#33384F");
        private static readonly IBrush PartFill = Vertical("#5A6385", "#41486A");
        private static readonly IBrush WellFill = B("#1C2033");
        private static readonly IBrush CapFill = Vertical("#6A7398", "#464E72");
        private static readonly IBrush PadFill = Vertical("#262A3F", "#323853");
        private static readonly IBrush ChipFill = Vertical("#262B42", "#1F2337");
        private static readonly IBrush Transparent = Brushes.Transparent;
        private static readonly IBrush ShadowBrush = A("#000000", 38);
        private static readonly IBrush DotBrush = A("#8E9AC8", 90);

        private static readonly Color DimLine = Color.Parse("#5B6590");
        private static readonly IBrush DimBrush = B("#5B6590"), BoundBrush = B("#4CC9F0"), WhiteBrush = B("#FFFFFF");
        private static readonly IBrush KeyBrush = B("#6C9CFF"), MouseBrush = B("#4CD787"), PadBrush = B("#FFB84C");

        private static readonly Typeface Bold = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
        private readonly Dictionary<(IBrush, double, bool), Pen> penCache = new();
        private readonly Dictionary<(IBrush, double), Pen> dottedCache = new();

        private Pen PenFor(IBrush brush, double thickness, bool round = false)
        {
            var key = (brush, thickness, round);
            if (!penCache.TryGetValue(key, out Pen pen))
                penCache[key] = pen = new Pen(brush, thickness, null, round ? PenLineCap.Round : PenLineCap.Flat, round ? PenLineJoin.Round : PenLineJoin.Miter);
            return pen;
        }

        private Pen Dotted(IBrush brush, double thickness)
        {
            var key = (brush, thickness);
            if (!dottedCache.TryGetValue(key, out Pen pen))
                dottedCache[key] = pen = new Pen(brush, thickness, new DashStyle(new double[] { 2, 3 }, 0), PenLineCap.Round);
            return pen;
        }

        // DualShock 4 outline (right half, clockwise from the top centre; the left half is the mirror image).
        private static readonly (Point P0, Point C1, Point C2, Point P1)[] Ds4Half =
        {
            (new Point(440, 150), new Point(485, 146), new Point(535, 146), new Point(572, 152)),
            (new Point(572, 152), new Point(596, 155), new Point(612, 166), new Point(620, 184)),
            (new Point(620, 184), new Point(640, 232), new Point(662, 300), new Point(664, 350)),
            (new Point(664, 350), new Point(666, 392), new Point(650, 424), new Point(624, 426)),
            (new Point(624, 426), new Point(596, 428), new Point(582, 398), new Point(566, 372)),
            (new Point(566, 372), new Point(556, 356), new Point(540, 352), new Point(520, 352)),
            (new Point(520, 352), new Point(490, 352), new Point(465, 352), new Point(440, 352)),
        };

        private static Point Mirror(Point p) => new(880 - p.X, p.Y);

        private static readonly StreamGeometry Ds4Body = BuildDs4Body();
        private static readonly StreamGeometry TriangleGlyph = BuildTriangle();

        private static StreamGeometry BuildDs4Body()
        {
            var g = new StreamGeometry();
            using (StreamGeometryContext c = g.Open())
            {
                c.BeginFigure(Ds4Half[0].P0, true);
                foreach (var s in Ds4Half) c.CubicBezierTo(s.C1, s.C2, s.P1);
                for (int i = Ds4Half.Length - 1; i >= 0; i--)
                {
                    var s = Ds4Half[i];
                    c.CubicBezierTo(Mirror(s.C2), Mirror(s.C1), Mirror(s.P0));
                }
                c.EndFigure(true);
            }
            return g;
        }

        private static StreamGeometry BuildTriangle()
        {
            var g = new StreamGeometry();
            using (StreamGeometryContext c = g.Open())
            {
                c.BeginFigure(new Point(0, -6.5), false);
                c.LineTo(new Point(6.2, 4.6));
                c.LineTo(new Point(-6.2, 4.6));
                c.EndFigure(true);
            }
            return g;
        }

        // Face button colours (Xbox: A green, B red, X blue, Y yellow; DualShock 4: cross blue, circle red, square pink, triangle green).
        private static readonly IBrush XboxA = B("#4CD787"), XboxB = B("#FF5C5C"), XboxX = B("#5C8CFF"), XboxY = B("#FFD24C");
        private static readonly IBrush PsCross = B("#6C9CFF"), PsCircle = B("#FF6E6E"), PsSquare = B("#FF8AD4"), PsTriangle = B("#4CD7A8");

        private readonly Dictionary<(string, double, IBrush, bool), FormattedText> textCache = new();
        private readonly Dictionary<PadFamily, List<LayoutSlot>> drawOrder = new();
        private readonly Dictionary<string, DateTime> flashes = new(StringComparer.OrdinalIgnoreCase);
        private readonly DispatcherTimer flashTimer;
        private PadFamily family = PadFamily.DualShock4;
        private string selectedId;

        /// <summary>Text shown on a slot's chip (its current mapping, e.g. "Space" or "Not set").</summary>
        public Func<string, string> MappingText { get; set; } = _ => "";
        /// <summary>What the game does on this button (null = nothing to show).</summary>
        public Func<string, string> NativeText { get; set; } = _ => null;
        /// <summary>False dims a slot (a paddle the connected controller does not have).</summary>
        public Func<string, bool> IsAvailable { get; set; } = _ => true;
        /// <summary>False shows a slot as off (a disabled paddle).</summary>
        public Func<string, bool> IsSlotEnabled { get; set; } = _ => true;
        /// <summary>Pad actions held right now (bit i = BindingActions[i]).</summary>
        public int PressedActionMask { get; set; }
        /// <summary>Paddle slots held right now (bit i = slot i).</summary>
        public int PressedPaddleSlots { get; set; }

        public event Action<string> SlotSelected;

        public ControllerConfigView()
        {
            Focusable = true;
            ClipToBounds = true;
            MinWidth = 420;
            MinHeight = 300;
            flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            flashTimer.Tick += (_, _) =>
            {
                DateTime now = DateTime.UtcNow;
                var done = new List<string>();
                foreach (var pair in flashes) if ((now - pair.Value).TotalMilliseconds > 700) done.Add(pair.Key);
                foreach (string id in done) flashes.Remove(id);
                if (flashes.Count == 0) flashTimer.Stop();
                InvalidateVisual();
            };
        }

        public PadFamily Family
        {
            get => family;
            set { if (family != value) { family = value; InvalidateVisual(); } }
        }

        public string SelectedId
        {
            get => selectedId;
            set { if (!string.Equals(selectedId, value, StringComparison.OrdinalIgnoreCase)) { selectedId = value; InvalidateVisual(); } }
        }

        public void Refresh() => InvalidateVisual();

        /// <summary>Briefly highlights a slot after it was remapped.</summary>
        public void Flash(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            flashes[id] = DateTime.UtcNow;
            if (!flashTimer.IsEnabled) flashTimer.Start();
            InvalidateVisual();
        }

        // ── Interaction ─────────────────────────────────────────────────────────

        private (double Scale, double Ox, double Oy) Fit()
        {
            double s = Math.Min(Bounds.Width / ControllerLayout.Width, Bounds.Height / ControllerLayout.Height);
            if (s <= 0 || double.IsNaN(s)) s = 1;
            return (s, (Bounds.Width - ControllerLayout.Width * s) / 2, (Bounds.Height - ControllerLayout.Height * s) / 2);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            Focus();
            Point p = e.GetPosition(this);
            var (s, ox, oy) = Fit();
            LayoutSlot hit = ControllerLayout.HitTest(family, (p.X - ox) / s, (p.Y - oy) / s);
            if (hit != null) Select(hit.Id);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int step = e.Key is Key.Right or Key.Down ? 1 : e.Key is Key.Left or Key.Up ? -1 : 0;
            if (step == 0) return;
            IReadOnlyList<LayoutSlot> slots = ControllerLayout.For(family);
            int index = -1;
            for (int i = 0; i < slots.Count; i++) if (string.Equals(slots[i].Id, selectedId, StringComparison.OrdinalIgnoreCase)) index = i;
            index = (index + step + slots.Count) % slots.Count;
            Select(slots[index].Id);
            e.Handled = true;
        }

        private void Select(string id)
        {
            SelectedId = id;
            SlotSelected?.Invoke(id);
        }

        // ── Drawing ─────────────────────────────────────────────────────────────

        private static readonly IBrush GlowBrush = A("#4CC9F0", 70), HaloBrush = A("#FFFFFF", 60), RidgeBrush = A("#FFFFFF", 45),
            BodyEdge = A("#8190C4", 200), PartEdge = A("#8C98C8", 170), FaintEdge = A("#8E9AC8", 70), ScrimBrush = A("#0C1022", 150);

        private List<LayoutSlot> DrawOrder()
        {
            if (!drawOrder.TryGetValue(family, out List<LayoutSlot> list))
            {
                list = new List<LayoutSlot>(ControllerLayout.For(family));
                list.Sort((a, b) => (b.MW * b.MH).CompareTo(a.MW * a.MH));   // big shapes first so small ones stay on top of them
                drawOrder[family] = list;
            }
            return list;
        }

        public override void Render(DrawingContext context)
        {
            var (scale, ox, oy) = Fit();
            context.DrawRectangle(BackdropFill, null, new Rect(Bounds.Size));
            using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(ox, oy)))
            {
                DrawHeader(context);
                DrawBodies(context);
                IReadOnlyList<LayoutSlot> slots = ControllerLayout.For(family);
                foreach (LayoutSlot s in slots) DrawConnector(context, s);
                foreach (LayoutSlot s in DrawOrder()) DrawMarker(context, s);
                foreach (LayoutSlot s in slots) DrawChip(context, s);
            }
        }

        private void DrawHeader(DrawingContext c)
        {
            DrawText(c, family == PadFamily.Xbox360 ? "XBOX 360 CONTROLLER" : "DUALSHOCK 4 CONTROLLER", 13, TextBright, new Point(440, 30), true, true);
            DrawText(c, "Select a control to see and change what it does", 10, TextDim, new Point(440, 50), true, false);
        }

        private void DrawBodies(DrawingContext c)
        {
            if (family == PadFamily.DualShock4)
            {
                for (int i = 1; i <= 3; i++)
                    using (c.PushTransform(Matrix.CreateTranslation(0, 2 + i * 3))) c.DrawGeometry(ShadowBrush, null, Ds4Body);
                c.DrawGeometry(BodyFill, PenFor(BodyEdge, 2), Ds4Body);
            }
            else
            {
                for (int i = 1; i <= 3; i++)
                {
                    double dy = 2 + i * 3;
                    c.DrawRectangle(ShadowBrush, null, new Rect(280, 150 + dy, 320, 200), 60, 60);
                    c.DrawEllipse(ShadowBrush, null, new Point(310, 330 + dy), 58, 92);
                    c.DrawEllipse(ShadowBrush, null, new Point(570, 330 + dy), 58, 92);
                }
                c.DrawRectangle(BodyFill, PenFor(BodyEdge, 2), new Rect(280, 150, 320, 200), 60, 60);
                c.DrawEllipse(BodyFill, PenFor(BodyEdge, 2), new Point(310, 330), 58, 92);
                c.DrawEllipse(BodyFill, PenFor(BodyEdge, 2), new Point(570, 330), 58, 92);
                c.DrawRectangle(BodyFill, null, new Rect(284, 154, 312, 190), 56, 56);
            }

            // D-pad cross, centred on its four arm markers
            LayoutSlot up = ControllerLayout.Find(family, ControllerEngine.ActionDPadUp), down = ControllerLayout.Find(family, ControllerEngine.ActionDPadDown);
            LayoutSlot left = ControllerLayout.Find(family, ControllerEngine.ActionDPadLeft), right = ControllerLayout.Find(family, ControllerEngine.ActionDPadRight);
            if (up != null && down != null && left != null && right != null)
            {
                double cx = (left.MX + right.MX) / 2, cy = (up.MY + down.MY) / 2;
                c.DrawRectangle(PadFill, PenFor(PartEdge, 1.2), new Rect(left.MX - 12, cy - 12, right.MX - left.MX + 24, 24), 6, 6);
                c.DrawRectangle(PadFill, PenFor(PartEdge, 1.2), new Rect(cx - 12, up.MY - 12, 24, down.MY - up.MY + 24), 6, 6);
                c.DrawRectangle(PadFill, null, new Rect(cx - 11, cy - 11, 22, 22), 4, 4);   // hides the seam in the middle
            }

            // Rear view (the same silhouette, smaller, with the paddles on its grips)
            DrawText(c, "REAR VIEW", 10, TextDim, new Point(440, 418), true, true);
            using (c.PushTransform(Matrix.CreateScale(0.5, 0.5) * Matrix.CreateTranslation(220, 359)))
            {
                c.DrawGeometry(ShadowBrush, null, Ds4Body);
                c.DrawGeometry(BodyFill, PenFor(BodyEdge, 3), Ds4Body);
            }
        }

        private bool IsBound(string id)
        {
            string t = MappingText(id);
            return !string.IsNullOrEmpty(t) && t != "Not set" && t != "Off" && t != "Mouse look off";
        }

        private bool IsPressed(LayoutSlot s)
        {
            if (PaddleSlots.TryParseId(s.Id, out int slot)) return ((PressedPaddleSlots >> slot) & 1) != 0;
            int i = Array.IndexOf(ControllerEngine.BindingActions, s.Id);
            return i >= 0 && ((PressedActionMask >> i) & 1) != 0;
        }

        private double FlashAmount(string id)
        {
            if (!flashes.TryGetValue(id, out DateTime t)) return 0;
            double age = (DateTime.UtcNow - t).TotalMilliseconds;
            return age >= 700 ? 0 : 1 - age / 700;
        }

        private static bool IsFace(string id) =>
            id == ControllerEngine.ActionACross || id == ControllerEngine.ActionBCircle || id == ControllerEngine.ActionXSquare || id == ControllerEngine.ActionYTriangle;
        private static bool IsDPad(string id) =>
            id == ControllerEngine.ActionDPadUp || id == ControllerEngine.ActionDPadDown || id == ControllerEngine.ActionDPadLeft || id == ControllerEngine.ActionDPadRight;

        private void DrawConnector(DrawingContext c, LayoutSlot s)
        {
            var chip = s.Chip;
            Point from = s.Side == SlotSide.Left ? new Point(chip.X + chip.W, chip.Y + chip.H / 2)
                : s.Side == SlotSide.Right ? new Point(chip.X, chip.Y + chip.H / 2)
                : new Point(chip.X + chip.W / 2, chip.Y);
            bool hot = IsPressed(s) || string.Equals(s.Id, selectedId, StringComparison.OrdinalIgnoreCase);
            c.DrawLine(hot ? PenFor(Accent, 1.8, true) : Dotted(DimBrush, 1.2), from, new Point(s.MX, s.MY));
        }

        // ── Markers ─────────────────────────────────────────────────────────────

        private IBrush EdgeBrush(bool selected, bool pressed, bool bound) =>
            selected ? WhiteBrush : pressed ? Accent : bound ? BoundBrush : DimBrush;

        private void DrawMarker(DrawingContext c, LayoutSlot s)
        {
            bool selected = string.Equals(s.Id, selectedId, StringComparison.OrdinalIgnoreCase);
            bool pressed = IsPressed(s);
            bool bound = IsBound(s.Id);
            bool available = !s.IsPaddle || IsAvailable(s.Id);
            Point centre = new(s.MX, s.MY);

            using (c.PushOpacity(available ? 1.0 : 0.4))
            {
                if (selected) DrawHalo(c, s);

                if (s.Id == ControllerEngine.ActionLsL3 || s.Id == ControllerEngine.ActionRsR3) DrawStick(c, s, selected, pressed, bound);
                else if (s.Id == ControllerLayout.RightStickId) DrawMouseMarker(c, s, selected, bound);
                else if (IsFace(s.Id)) DrawFace(c, s, selected, pressed);
                else if (s.Id == ControllerEngine.ActionTouchpad) DrawTouchpad(c, s, selected, pressed, bound);
                else if (IsDPad(s.Id)) DrawDPadArm(c, s, selected, pressed, bound);
                else if (InputChoices.IsStickAction(s.Id)) DrawDirDot(c, s, selected, pressed, bound);
                else DrawPart(c, s, selected, pressed, bound);
            }

            double flash = FlashAmount(s.Id);
            if (flash > 0)
            {
                IBrush ring = new SolidColorBrush(Color.FromArgb((byte)(255 * flash), 0x4C, 0xD7, 0x87));
                double grow = 6 * (1 - flash) + 4;
                if (s.Shape == SlotShape.Circle) c.DrawEllipse(null, PenFor(ring, 3), centre, s.MW / 2 + grow, s.MH / 2 + grow);
                else c.DrawRectangle(null, PenFor(ring, 3), new Rect(s.MX - s.MW / 2 - grow, s.MY - s.MH / 2 - grow, s.MW + 2 * grow, s.MH + 2 * grow), 9, 9);
            }
        }

        private void DrawHalo(DrawingContext c, LayoutSlot s)
        {
            const double g = 5;
            if (s.Shape == SlotShape.Circle) c.DrawEllipse(null, PenFor(HaloBrush, 5), new Point(s.MX, s.MY), s.MW / 2 + g, s.MH / 2 + g);
            else c.DrawRectangle(null, PenFor(HaloBrush, 5), new Rect(s.MX - s.MW / 2 - g, s.MY - s.MH / 2 - g, s.MW + 2 * g, s.MH + 2 * g), 9, 9);
        }

        private void DrawStick(DrawingContext c, LayoutSlot s, bool selected, bool pressed, bool bound)
        {
            Point p = new(s.MX, s.MY);
            double r = s.MW / 2;
            if (bound || pressed || selected) c.DrawEllipse(null, PenFor(GlowBrush, 7), p, r + 1, r + 1);
            c.DrawEllipse(WellFill, PenFor(EdgeBrush(selected, pressed, bound), selected ? 2.6 : 2), p, r, r);
            c.DrawEllipse(pressed ? Accent : CapFill, PenFor(PartEdge, 1.4), p, r - 9, r - 9);
            c.DrawEllipse(null, PenFor(RidgeBrush, 1), p, r - 16, r - 16);
            DrawText(c, s.Id == ControllerEngine.ActionLsL3 ? "L3" : "R3", 8, pressed ? BackdropTop : TextDim, new Point(s.MX, s.MY + r - 14), true, true);
        }

        private void DrawMouseMarker(DrawingContext c, LayoutSlot s, bool selected, bool bound)
        {
            Point p = new(s.MX, s.MY - 3);
            c.DrawEllipse(ScrimBrush, PenFor(EdgeBrush(selected, false, bound), selected ? 2.4 : 1.4), p, s.MW / 2 - 4, s.MH / 2 - 4);
            IBrush ink = bound ? TextBright : TextDim;
            c.DrawRectangle(null, PenFor(ink, 1.4, true), new Rect(p.X - 4.5, p.Y - 7, 9, 14), 4.5, 4.5);
            c.DrawLine(PenFor(ink, 1.2, true), new Point(p.X, p.Y - 7), new Point(p.X, p.Y - 1.5));
        }

        private void DrawFace(DrawingContext c, LayoutSlot s, bool selected, bool pressed)
        {
            bool xbox = family == PadFamily.Xbox360;
            IBrush colour = s.Id == ControllerEngine.ActionACross ? (xbox ? XboxA : PsCross)
                : s.Id == ControllerEngine.ActionBCircle ? (xbox ? XboxB : PsCircle)
                : s.Id == ControllerEngine.ActionXSquare ? (xbox ? XboxX : PsSquare)
                : (xbox ? XboxY : PsTriangle);
            Point p = new(s.MX, s.MY);
            double r = s.MW / 2;
            c.DrawEllipse(pressed ? colour : PadFill, PenFor(selected ? WhiteBrush : colour, selected ? 2.6 : 1.8), p, r, r);
            IBrush ink = pressed ? BackdropTop : colour;

            if (xbox)
            {
                string letter = s.Id == ControllerEngine.ActionACross ? "A" : s.Id == ControllerEngine.ActionBCircle ? "B" : s.Id == ControllerEngine.ActionXSquare ? "X" : "Y";
                DrawText(c, letter, 13, ink, p, true, true);
                return;
            }
            Pen pen = PenFor(ink, 2.2, true);
            if (s.Id == ControllerEngine.ActionACross)
            {
                c.DrawLine(pen, new Point(p.X - 5, p.Y - 5), new Point(p.X + 5, p.Y + 5));
                c.DrawLine(pen, new Point(p.X + 5, p.Y - 5), new Point(p.X - 5, p.Y + 5));
            }
            else if (s.Id == ControllerEngine.ActionBCircle) c.DrawEllipse(null, pen, p, 5.6, 5.6);
            else if (s.Id == ControllerEngine.ActionXSquare) c.DrawRectangle(null, pen, new Rect(p.X - 5.3, p.Y - 5.3, 10.6, 10.6), 1.5, 1.5);
            else using (c.PushTransform(Matrix.CreateTranslation(p.X, p.Y))) c.DrawGeometry(null, pen, TriangleGlyph);
        }

        private void DrawTouchpad(DrawingContext c, LayoutSlot s, bool selected, bool pressed, bool bound)
        {
            Rect r = new(s.MX - s.MW / 2, s.MY - s.MH / 2, s.MW, s.MH);
            c.DrawRectangle(PadFill, PenFor(EdgeBrush(selected, pressed, bound), selected ? 2.6 : 1.6), r, 12, 12);
            for (double gx = r.X + 10; gx < r.Right - 4; gx += 10)
                for (double gy = r.Y + 9; gy < r.Bottom - 4; gy += 10)
                    c.DrawEllipse(DotBrush, null, new Point(gx, gy), 1, 1);
            if (pressed) c.DrawRectangle(GlowBrush, null, r, 12, 12);
        }

        private void DrawDPadArm(DrawingContext c, LayoutSlot s, bool selected, bool pressed, bool bound)
        {
            Rect r = new(s.MX - s.MW / 2, s.MY - s.MH / 2, s.MW, s.MH);
            c.DrawRectangle(pressed ? Accent : Transparent, PenFor(selected ? WhiteBrush : bound ? BoundBrush : Transparent, selected ? 2.4 : 1.6), r, 5, 5);
            DrawText(c, Glyph(s), 11, pressed ? BackdropTop : TextDim, new Point(s.MX, s.MY), true, false);
        }

        private void DrawDirDot(DrawingContext c, LayoutSlot s, bool selected, bool pressed, bool bound)
        {
            c.DrawEllipse(pressed ? Accent : ScrimBrush, PenFor(EdgeBrush(selected, pressed, bound), selected ? 2.2 : 1.4), new Point(s.MX, s.MY), s.MW / 2, s.MH / 2);
            DrawText(c, Glyph(s), 9, pressed ? BackdropTop : TextBright, new Point(s.MX, s.MY), true, false);
        }

        private void DrawPart(DrawingContext c, LayoutSlot s, bool selected, bool pressed, bool bound)
        {
            IBrush fill = pressed ? Accent : PartFill;
            Pen pen = PenFor(selected ? WhiteBrush : pressed ? Accent : bound ? BoundBrush : PartEdge, selected ? 2.4 : bound ? 1.8 : 1.3);
            if (s.Shape == SlotShape.Circle) c.DrawEllipse(fill, pen, new Point(s.MX, s.MY), s.MW / 2, s.MH / 2);
            else c.DrawRectangle(fill, pen, new Rect(s.MX - s.MW / 2, s.MY - s.MH / 2, s.MW, s.MH), Math.Min(s.MW, s.MH) / 2.2, Math.Min(s.MW, s.MH) / 2.2);

            string glyph = Glyph(s);
            if (!string.IsNullOrEmpty(glyph) && s.MW >= 18 && s.MH >= 12)
                DrawText(c, glyph, 10, pressed ? BackdropTop : TextBright, new Point(s.MX, s.MY), true, true);
        }

        private string Glyph(LayoutSlot s)
        {
            if (s.Id == ControllerEngine.ActionLtL2) return family == PadFamily.Xbox360 ? "LT" : "L2";
            if (s.Id == ControllerEngine.ActionRtR2) return family == PadFamily.Xbox360 ? "RT" : "R2";
            if (s.Id == ControllerEngine.ActionLbL1) return family == PadFamily.Xbox360 ? "LB" : "L1";
            if (s.Id == ControllerEngine.ActionRbR1) return family == PadFamily.Xbox360 ? "RB" : "R1";
            if (s.Id == ControllerEngine.ActionDPadUp || s.Id == ControllerEngine.ActionLeftStickUp) return "↑";
            if (s.Id == ControllerEngine.ActionDPadDown || s.Id == ControllerEngine.ActionLeftStickDown) return "↓";
            if (s.Id == ControllerEngine.ActionDPadLeft || s.Id == ControllerEngine.ActionLeftStickLeft) return "←";
            if (s.Id == ControllerEngine.ActionDPadRight || s.Id == ControllerEngine.ActionLeftStickRight) return "→";
            if (s.Id == ControllerEngine.ActionGuidePs) return family == PadFamily.Xbox360 ? "" : "PS";
            return "";
        }

        // ── Label chips ─────────────────────────────────────────────────────────

        private static IBrush KindBrush(string mapping, bool off)
        {
            if (off || string.IsNullOrEmpty(mapping) || mapping == "Not set" || mapping == "Off" || mapping == "Mouse look off") return DimBrush;
            if (mapping.StartsWith("Pad:", StringComparison.Ordinal)) return PadBrush;
            if (mapping.StartsWith("Mouse", StringComparison.Ordinal) || mapping.StartsWith("Wheel", StringComparison.Ordinal)) return MouseBrush;
            return KeyBrush;
        }

        private void DrawChip(DrawingContext c, LayoutSlot s)
        {
            var chip = s.Chip;
            bool selected = string.Equals(s.Id, selectedId, StringComparison.OrdinalIgnoreCase);
            bool pressed = IsPressed(s);
            bool available = !s.IsPaddle || IsAvailable(s.Id);
            bool enabled = !s.IsPaddle || IsSlotEnabled(s.Id);
            string mapping = MappingText(s.Id);
            string native = NativeText(s.Id);
            string line2 = mapping;
            if (s.IsPaddle && !enabled) line2 = "Off";
            else if (!string.IsNullOrEmpty(native)) line2 = mapping + "  ·  " + native;
            if (s.IsPaddle && !available) line2 += "  (not detected)";
            line2 = Ellipsize(line2, 35);

            double flash = FlashAmount(s.Id);
            Rect r = new(chip.X, chip.Y, chip.W, chip.H);
            IBrush accent = KindBrush(mapping, s.IsPaddle && !enabled);
            using (c.PushOpacity(available ? 1.0 : 0.55))
            {
                c.DrawRectangle(ShadowBrush, null, new Rect(r.X, r.Y + 2, r.Width, r.Height), 8, 8);
                c.DrawRectangle(ChipFill, PenFor(selected ? WhiteBrush : pressed ? Accent : FaintEdge, selected ? 2 : 1.2), r, 8, 8);
                if (flash > 0) c.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(80 * flash), 0x4C, 0xD7, 0x87)), null, r, 8, 8);
                c.DrawRectangle(accent, null, new Rect(r.X + 1, r.Y + 6, 3.5, r.Height - 12), 2, 2);
                DrawText(c, s.Title.ToUpperInvariant(), 9, TextDim, new Point(r.X + 12, r.Y + 4), false, true);
                bool unset = accent == DimBrush;
                DrawText(c, line2, 11, unset ? TextDim : TextBright, new Point(r.X + 12, r.Y + 17), false, false);
            }
        }

        private static string Ellipsize(string text, int max) =>
            string.IsNullOrEmpty(text) || text.Length <= max ? text : text.Substring(0, max - 1) + "…";

        private void DrawText(DrawingContext c, string text, double size, IBrush brush, Point at, bool centered, bool bold)
        {
            if (string.IsNullOrEmpty(text)) return;
            var key = (text, size, brush, bold);
            if (!textCache.TryGetValue(key, out FormattedText ft))
            {
                ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, bold ? Bold : Typeface.Default, size, brush);
                if (textCache.Count > 600) textCache.Clear();
                textCache[key] = ft;
            }
            c.DrawText(ft, centered ? new Point(at.X - ft.Width / 2, at.Y - ft.Height / 2) : at);
        }
    }
}
