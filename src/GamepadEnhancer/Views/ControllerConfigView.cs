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
    /// <summary>What the diagram shows: the full controller (with a small rear view), or only the paddles on a large rear view.</summary>
    internal enum DiagramMode { Controls, Paddles }

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

        private static readonly IBrush BackdropTop = B("#1A1D27"), TextDim = B("#8B8FA3"), TextBright = B("#E4E6EF"), Accent = B("#5EEAD4"), Good = B("#34D399");
        private static readonly IBrush BackdropFill = B("#1A1D27");
        private static readonly IBrush BodyFill = B("#22263A");
        private static readonly IBrush PartFill = B("#2A2E3E");
        private static readonly IBrush WellFill = B("#0F1117");
        private static readonly IBrush CapFill = B("#2F3446");
        private static readonly IBrush PadFill = B("#2A2E3E");
        private static readonly IBrush ChipFill = B("#0F1117");
        private static readonly IBrush Transparent = Brushes.Transparent;
        private static readonly IBrush ShadowBrush = A("#000000", 38);
        private static readonly IBrush DotBrush = A("#8B8FA3", 90);

        private static readonly IBrush DimBrush = B("#4B4E63"), BoundBrush = B("#3FB6A8"), WhiteBrush = B("#FFFFFF");
        private static readonly IBrush GroupFill = A("#FFFFFF", 7), GroupEdge = A("#8B8FA3", 60), BadgeFill = A("#FFFFFF", 16), BadgeEdge = A("#8B8FA3", 50);
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

        // Xbox outline, same construction: right half from the top centre, defined on a 700x400 reference drawing and mapped onto the
        // diagram; the left half is the mirror image.
        private static readonly (double X0, double Y0, double X1, double Y1, double X2, double Y2, double X3, double Y3)[] XboxRef =
        {
            (350, 74, 375, 74, 395, 62, 432, 62),
            (432, 62, 465, 62, 495, 72, 522, 92),
            (522, 92, 560, 120, 588, 200, 600, 290),
            (600, 290, 608, 335, 600, 385, 565, 392),
            (565, 392, 530, 398, 500, 360, 468, 322),
            (468, 322, 445, 298, 400, 292, 350, 292),
        };

        private static Point XboxPt(double rx, double ry) => new(440 + (rx - 350) * 0.86, 105 + (ry - 60) * 0.86);

        private static readonly StreamGeometry XboxBody = BuildXboxBody();

        private static StreamGeometry BuildXboxBody()
        {
            var g = new StreamGeometry();
            using (StreamGeometryContext c = g.Open())
            {
                c.BeginFigure(XboxPt(XboxRef[0].X0, XboxRef[0].Y0), true);
                foreach (var s in XboxRef) c.CubicBezierTo(XboxPt(s.X1, s.Y1), XboxPt(s.X2, s.Y2), XboxPt(s.X3, s.Y3));
                for (int i = XboxRef.Length - 1; i >= 0; i--)
                {
                    var s = XboxRef[i];
                    c.CubicBezierTo(Mirror(XboxPt(s.X2, s.Y2)), Mirror(XboxPt(s.X1, s.Y1)), Mirror(XboxPt(s.X0, s.Y0)));
                }
                c.EndFigure(true);
            }
            return g;
        }

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
        private readonly Dictionary<(PadFamily, DiagramMode), List<LayoutSlot>> drawOrder = new();
        private readonly Dictionary<string, DateTime> flashes = new(StringComparer.OrdinalIgnoreCase);
        private readonly DispatcherTimer flashTimer;
        private PadFamily family = PadFamily.DualShock4;
        private bool overview;
        private DiagramMode mode = DiagramMode.Controls;
        private DiagramPage page = DiagramPage.Buttons;
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

        /// <summary>Read-only picture of the pad (no header, connectors or label chips), cropped to the controller. Used on the dashboard.</summary>
        public bool Overview
        {
            get => overview;
            set { if (overview != value) { overview = value; InvalidateVisual(); } }
        }

        public DiagramMode Mode
        {
            get => mode;
            set { if (mode != value) { mode = value; InvalidateVisual(); } }
        }

        /// <summary>Which page the diagram shows: the buttons, or the stick directions. Only the shown page's chips can be clicked.</summary>
        public DiagramPage Page
        {
            get => page;
            set { if (page != value) { page = value; InvalidateVisual(); } }
        }

        /// <summary>The slots you can click right now (the paddle view's four, or the shown page's).</summary>
        private IReadOnlyList<LayoutSlot> Slots() => mode == DiagramMode.Paddles ? ControllerLayout.ForPaddles(family) : ControllerLayout.For(family, page);

        /// <summary>Every marker that is drawn, including the other page's (dimmed).</summary>
        private IReadOnlyList<LayoutSlot> AllSlots() => mode == DiagramMode.Paddles ? ControllerLayout.ForPaddles(family) : ControllerLayout.For(family);

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

        internal double ScaleForTests => Fit().Scale;

        private (double Scale, double Ox, double Oy) Fit()
        {
            if (overview)
            {
                // the controller body only: x 190..690, y 90..440 of the 880 x 620 drawing
                const double vx = 190, vy = 90, vw = 500, vh = 350;
                double k = Math.Min(Bounds.Width / vw, Bounds.Height / vh);
                if (k <= 0 || double.IsNaN(k)) k = 1;
                return (k, (Bounds.Width - vw * k) / 2 - vx * k, (Bounds.Height - vh * k) / 2 - vy * k);
            }
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
            LayoutSlot hit = ControllerLayout.HitTest(Slots(), (p.X - ox) / s, (p.Y - oy) / s);
            if (hit != null) Select(hit.Id);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int step = e.Key is Key.Right or Key.Down ? 1 : e.Key is Key.Left or Key.Up ? -1 : 0;
            if (step == 0) return;
            IReadOnlyList<LayoutSlot> slots = Slots();
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

        private static readonly IBrush GlowBrush = A("#5EEAD4", 70), HaloBrush = A("#FFFFFF", 60), RidgeBrush = A("#FFFFFF", 45),
            BodyEdge = B("#3B4157"), PartEdge = B("#4B4E63"), FaintEdge = A("#8B8FA3", 70), ScrimBrush = A("#0F1117", 150);

        private List<LayoutSlot> DrawOrder()
        {
            if (!drawOrder.TryGetValue((family, mode), out List<LayoutSlot> list))
            {
                list = new List<LayoutSlot>(AllSlots());
                list.Sort((a, b) => (b.MW * b.MH).CompareTo(a.MW * a.MH));   // big shapes first so small ones stay on top of them
                drawOrder[(family, mode)] = list;
            }
            return list;
        }

        public override void Render(DrawingContext context)
        {
            var (scale, ox, oy) = Fit();
            context.DrawRectangle(BackdropFill, null, new Rect(Bounds.Size));
            using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(ox, oy)))
            {
                if (!overview && mode == DiagramMode.Paddles) DrawHeader(context);
                if (mode == DiagramMode.Paddles) DrawRearBody(context); else DrawBodies(context);
                IReadOnlyList<LayoutSlot> slots = Slots();
                if (!overview && mode == DiagramMode.Controls) DrawGroups(context);
                if (!overview) foreach (LayoutSlot s in slots) DrawConnector(context, s);
                foreach (LayoutSlot s in DrawOrder()) DrawMarker(context, s);
                if (!overview) foreach (LayoutSlot s in slots) DrawChip(context, s);
            }
        }

        /// <summary>The panels that frame the D-pad, the face buttons and a stick, each with its title.</summary>
        private void DrawGroups(DrawingContext c)
        {
            foreach (SlotGroup g in ControllerLayout.Groups(family, page))
            {
                c.DrawRectangle(GroupFill, PenFor(GroupEdge, 1), new Rect(g.X, g.Y, g.W, g.H), 10, 10);
                DrawTextLeftMiddle(c, g.Title, 11, TextDim, g.X + 12, g.Y + ControllerLayout.GroupTitleH / 2 + 1, true);
            }
        }

        private void DrawHeader(DrawingContext c)
        {
            DrawText(c, ControllerLayouts.ForDevice(family).Title.ToUpperInvariant(), 13, TextBright, new Point(440, 30), true, true);
            DrawText(c, mode == DiagramMode.Paddles ? "Rear view: select a paddle to assign it" : "Select a control to see and change what it does", 10, TextDim, new Point(440, 50), true, false);
        }

        /// <summary>The large rear view used by the paddle page: the grips carry the four paddles.</summary>
        private void DrawRearBody(DrawingContext c)
        {
            StreamGeometry body = family == PadFamily.Xbox360 ? XboxBody : Ds4Body;
            double dy = family == PadFamily.Xbox360 ? 20 : 0;
            using (c.PushTransform(Matrix.CreateTranslation(0, dy)))
            {
                for (int i = 1; i <= 3; i++)
                    using (c.PushTransform(Matrix.CreateTranslation(0, 2 + i * 3))) c.DrawGeometry(ShadowBrush, null, body);
                c.DrawGeometry(BodyFill, PenFor(BodyEdge, 2), body);
            }
            DrawText(c, "REAR VIEW", 10, TextDim, new Point(440, 470), true, true);
        }

        private void DrawBodies(DrawingContext c)
        {
            if (family == PadFamily.DualShock4)
            {
                for (int i = 1; i <= 3; i++)
                    using (c.PushTransform(Matrix.CreateTranslation(0, 2 + i * 3))) c.DrawGeometry(ShadowBrush, null, Ds4Body);
                c.DrawGeometry(BodyFill, PenFor(BodyEdge, 2), Ds4Body);
                // the light bar
                c.DrawLine(PenFor(GlowBrush, 7, true), new Point(404, 160), new Point(476, 160));
                c.DrawLine(PenFor(Accent, 2.4, true), new Point(404, 160), new Point(476, 160));
            }
            else
            {
                for (int i = 1; i <= 3; i++)
                    using (c.PushTransform(Matrix.CreateTranslation(0, 2 + i * 3))) c.DrawGeometry(ShadowBrush, null, XboxBody);
                c.DrawGeometry(BodyFill, PenFor(BodyEdge, 2), XboxBody);
                // the centre logo plate behind the Guide marker, and the inner shoulder seams
                c.DrawEllipse(PadFill, PenFor(PartEdge, 1.2), new Point(440, 139), 22, 22);
                c.DrawLine(PenFor(PartEdge, 1.2), new Point(366, 112), new Point(404, 118));
                c.DrawLine(PenFor(PartEdge, 1.2), new Point(514, 112), new Point(476, 118));
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
            if (overview) return;
            if (!ControllerLayouts.ForDevice(family).Supports(Inputs.PaddleLeftUpper)) return;   // no rear paddles on this pad, no rear view
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
                : s.Side == SlotSide.Top ? new Point(chip.X + chip.W / 2, chip.Y + chip.H)
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

            // the other page's controls: the stick directions are left out of the buttons page, everything else is drawn dimmed
            bool onPage = overview || mode == DiagramMode.Paddles || s.Page == page;
            if (!onPage && s.Page == DiagramPage.Sticks) return;
            selected &= onPage; pressed &= onPage;

            using (c.PushOpacity((available ? 1.0 : 0.4) * (onPage ? 1.0 : 0.35)))
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
            DrawText(c, ControllerLayouts.ForDevice(family).Label(s.Id), 8, pressed ? BackdropTop : TextDim, new Point(s.MX, s.MY + r - 14), true, true);
        }

        private void DrawMouseMarker(DrawingContext c, LayoutSlot s, bool selected, bool bound)
        {
            Point p = new(s.MX, s.MY - 3);
            c.DrawEllipse(ScrimBrush, PenFor(EdgeBrush(selected, false, bound), selected ? 2.4 : 1.4), p, s.MW / 2 - 4, s.MH / 2 - 4);
            IBrush ink = bound ? TextBright : TextDim;
            c.DrawRectangle(null, PenFor(ink, 1.4, true), new Rect(p.X - 4.5, p.Y - 7, 9, 14), 4.5, 4.5);
            c.DrawLine(PenFor(ink, 1.2, true), new Point(p.X, p.Y - 7), new Point(p.X, p.Y - 1.5));
        }

        private readonly Dictionary<string, IBrush> colourCache = new(StringComparer.OrdinalIgnoreCase);

        private IBrush LayoutColour(string hex, IBrush fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            if (!colourCache.TryGetValue(hex, out IBrush brush))
            {
                try { brush = B(hex); } catch { brush = fallback; }
                colourCache[hex] = brush;
            }
            return brush;
        }

        /// <summary>The four face buttons. Their colours, and whether they are drawn as symbols or lettered, come from the controller layout.</summary>
        private void DrawFace(DrawingContext c, LayoutSlot s, bool selected, bool pressed)
        {
            ControllerLayoutDef layout = ControllerLayouts.ForDevice(family);
            layout.FaceColors.TryGetValue(s.Id, out string hex);
            IBrush colour = LayoutColour(hex, TextBright);
            Point p = new(s.MX, s.MY);
            double r = s.MW / 2;
            c.DrawEllipse(pressed ? colour : PadFill, PenFor(selected ? WhiteBrush : colour, selected ? 2.6 : 1.8), p, r, r);
            IBrush ink = pressed ? BackdropTop : colour;

            if (!string.Equals(layout.FaceStyle, "symbols", StringComparison.OrdinalIgnoreCase))
            {
                DrawText(c, layout.Label(s.Id), 13, ink, p, true, true);
                return;
            }
            Pen pen = PenFor(ink, 2.2, true);
            if (s.Id == Inputs.FaceSouth)
            {
                c.DrawLine(pen, new Point(p.X - 5, p.Y - 5), new Point(p.X + 5, p.Y + 5));
                c.DrawLine(pen, new Point(p.X + 5, p.Y - 5), new Point(p.X - 5, p.Y + 5));
            }
            else if (s.Id == Inputs.FaceEast) c.DrawEllipse(null, pen, p, 5.6, 5.6);
            else if (s.Id == Inputs.FaceWest) c.DrawRectangle(null, pen, new Rect(p.X - 5.3, p.Y - 5.3, 10.6, 10.6), 1.5, 1.5);
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
                DrawText(c, glyph, s.MW >= 28 ? 15 : 10, pressed ? BackdropTop : TextBright, new Point(s.MX, s.MY), true, true);
        }

        private string Glyph(LayoutSlot s)
        {
            string custom = ControllerLayouts.ForDevice(family).Glyph(s.Id);
            if (!string.IsNullOrEmpty(custom)) return custom;
            if (s.Id == Inputs.DpadUp || s.Id == Inputs.LsUp) return "↑";
            if (s.Id == Inputs.DpadDown || s.Id == Inputs.LsDown) return "↓";
            if (s.Id == Inputs.DpadLeft || s.Id == Inputs.LsLeft) return "←";
            if (s.Id == Inputs.DpadRight || s.Id == Inputs.LsRight) return "→";
            if (s.Id == Inputs.Lb || s.Id == Inputs.Rb || s.Id == Inputs.Lt || s.Id == Inputs.Rt || s.Id == Inputs.Home)
            {
                string label = ControllerLayouts.ForDevice(family).Label(s.Id);
                return label.Length <= 3 ? label : "";   // a long label does not fit inside the marker; the chip carries it
            }
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

        /// <summary>The title as shown in a group panel: the group's own name is dropped ("D-Pad Up" under "D-Pad" reads "Up").</summary>
        private static string ChipTitle(LayoutSlot s)
        {
            if (s.Group != null && s.Title.StartsWith(s.Group + " ", StringComparison.OrdinalIgnoreCase)) return s.Title.Substring(s.Group.Length + 1);
            if (s.Id == ControllerLayout.RightStickId) return "Mouse look";
            return s.Title;
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
            bool big = chip.H > ControllerLayout.ChipH + 8;   // the paddle view's larger chips get three lines

            double flash = FlashAmount(s.Id);
            Rect r = new(chip.X, chip.Y, chip.W, chip.H);
            IBrush accent = KindBrush(mapping, s.IsPaddle && !enabled);
            bool unset = accent == DimBrush;
            using (c.PushOpacity(available ? 1.0 : 0.55))
            {
                c.DrawRectangle(ShadowBrush, null, new Rect(r.X, r.Y + 2, r.Width, r.Height), 8, 8);
                if (selected) c.DrawRectangle(null, PenFor(GlowBrush, 5), r.Inflate(1.5), 9, 9);
                c.DrawRectangle(ChipFill, PenFor(selected ? Accent : pressed ? Accent : FaintEdge, selected ? 1.8 : 1.2), r, 8, 8);
                if (pressed) c.DrawRectangle(GlowBrush, null, r, 8, 8);
                if (flash > 0) c.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(80 * flash), 0x4C, 0xD7, 0x87)), null, r, 8, 8);

                if (big)
                {
                    string line2 = s.IsPaddle && !enabled ? "Off" : mapping;
                    var notes = new List<string>();
                    if (!(s.IsPaddle && !enabled) && !string.IsNullOrEmpty(native)) notes.Add(native);
                    if (s.IsPaddle && !available) notes.Add("not detected");
                    string line3 = notes.Count == 0 ? null : Ellipsize(string.Join("  ·  ", notes), 34);
                    c.DrawRectangle(accent, null, new Rect(r.X + 1, r.Y + 6, 3.5, r.Height - 12), 2, 2);
                    DrawText(c, Ellipsize(s.Title.ToUpperInvariant(), 26), 11, TextDim, new Point(r.X + 14, r.Y + 7), false, true);
                    DrawText(c, Ellipsize(line2, 24), 15, unset ? TextDim : TextBright, new Point(r.X + 14, r.Y + 24), false, false);
                    if (line3 != null) DrawText(c, line3, 10.5, TextDim, new Point(r.X + 14, r.Y + 46), false, false);
                    return;
                }

                // one line: the control's name on the left, what it is mapped to in a small badge on the right
                string badge = s.IsPaddle && !enabled ? "Off" : string.IsNullOrEmpty(mapping) ? "Not set" : mapping;
                if (s.IsPaddle && !available) badge = "Not detected";
                double midY = r.Y + r.Height / 2;
                double badgeMax = r.Width * 0.5;
                string badgeText = FitText(badge, 11, true, badgeMax - 16);
                double badgeW = TextWidth(badgeText, 11, true) + 16;
                var badgeRect = new Rect(r.Right - 8 - badgeW, midY - 10, badgeW, 20);
                c.DrawRectangle(BadgeFill, PenFor(BadgeEdge, 1), badgeRect, 6, 6);
                DrawTextLeftMiddle(c, badgeText, 11, unset ? TextDim : accent, badgeRect.X + 8, midY, true);

                string title = ChipTitle(s);
                double titleMax = badgeRect.X - (r.X + 12) - 6;
                string titleText = FitText(title, 12, true, titleMax);
                DrawTextLeftMiddle(c, titleText, 12, TextBright, r.X + 12, midY, true);

                // what the game does on this button, when there is room left for it
                double used = TextWidth(titleText, 12, true);
                double room = titleMax - used - 8;
                if (!string.IsNullOrEmpty(native) && room >= 28)
                    DrawTextLeftMiddle(c, FitText(native, 10, false, room), 10, TextDim, r.X + 12 + used + 8, midY, false);
            }
        }

        private readonly Dictionary<(string, double, bool, double), string> fitCache = new();

        /// <summary>The text cut with an ellipsis so it is at most <paramref name="maxWidth"/> wide.</summary>
        private string FitText(string text, double size, bool bold, double maxWidth)
        {
            if (string.IsNullOrEmpty(text) || maxWidth <= 0) return "";
            if (TextWidth(text, size, bold) <= maxWidth) return text;
            var key = (text, size, bold, Math.Round(maxWidth));
            if (fitCache.TryGetValue(key, out string hit)) return hit;
            string result = "…";
            for (int n = text.Length - 1; n > 0; n--)
            {
                string t = text.Substring(0, n).TrimEnd() + "…";
                if (TextWidth(t, size, bold) <= maxWidth) { result = t; break; }
            }
            if (fitCache.Count > 400) fitCache.Clear();
            return fitCache[key] = result;
        }

        private readonly Dictionary<(string, double, bool), double> widthCache = new();

        private double TextWidth(string text, double size, bool bold)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var key = (text, size, bold);
            if (!widthCache.TryGetValue(key, out double w))
            {
                w = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, bold ? Bold : Typeface.Default, size, TextBright).Width;
                if (widthCache.Count > 800) widthCache.Clear();
                widthCache[key] = w;
            }
            return w;
        }

        private void DrawTextLeftMiddle(DrawingContext c, string text, double size, IBrush brush, double x, double midY, bool bold)
        {
            if (string.IsNullOrEmpty(text)) return;
            var key = (text, size, brush, bold);
            if (!textCache.TryGetValue(key, out FormattedText ft))
            {
                ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, bold ? Bold : Typeface.Default, size, brush);
                if (textCache.Count > 600) textCache.Clear();
                textCache[key] = ft;
            }
            c.DrawText(ft, new Point(x, midY - ft.Height / 2));
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
