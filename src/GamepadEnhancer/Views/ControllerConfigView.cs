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

        private static readonly IBrush Backdrop = B("#14161C"), BodyFill = B("#22252E"), Idle = B("#343844"), ChipFill = B("#1C1F27");
        private static readonly IBrush TextDim = B("#9AA1B0"), TextBright = B("#F2F4F8"), Accent = B("#6C8CFF"), Good = B("#4CD787"), Warn = B("#FFB84C");
        private static readonly Pen BodyPen = new(B("#3A3E4A"), 2), IdlePen = new(B("#4A4F5E"), 1.5), LinePen = new(B("#2E323D"), 1);
        private static readonly Pen LineHotPen = new(B("#6C8CFF"), 1.5), SelectedPen = new(B("#FFFFFF"), 2.5);
        private static readonly Typeface Bold = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

        private readonly Dictionary<(string, double, IBrush, bool), FormattedText> textCache = new();
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
        public Func<string, bool> IsEnabled { get; set; } = _ => true;
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

        public override void Render(DrawingContext context)
        {
            var (scale, ox, oy) = Fit();
            context.DrawRectangle(Backdrop, null, new Rect(Bounds.Size));
            using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(ox, oy)))
            {
                DrawBodies(context);
                IReadOnlyList<LayoutSlot> slots = ControllerLayout.For(family);
                foreach (LayoutSlot s in slots) DrawConnector(context, s);
                foreach (LayoutSlot s in slots) DrawMarker(context, s);
                foreach (LayoutSlot s in slots) DrawChip(context, s);
            }
        }

        private void DrawBodies(DrawingContext c)
        {
            c.DrawRectangle(BodyFill, BodyPen, new Rect(280, 150, 320, 200), 60, 60);
            c.DrawEllipse(BodyFill, BodyPen, new Point(310, 330), 58, 92);
            c.DrawEllipse(BodyFill, BodyPen, new Point(570, 330), 58, 92);
            c.DrawRectangle(BodyFill, null, new Rect(284, 154, 312, 190), 56, 56);   // hides the seams between body and grips
            DrawText(c, family == PadFamily.Xbox360 ? "XBOX 360" : "DUALSHOCK 4", 11, TextDim, new Point(440, 392), true, true);

            DrawText(c, "REAR VIEW", 10, TextDim, new Point(440, 425), true, true);
            c.DrawRectangle(BodyFill, BodyPen, new Rect(330, 440, 220, 110), 34, 34);
        }

        private bool IsBound(string id)
        {
            string t = MappingText(id);
            return !string.IsNullOrEmpty(t) && t != "Not set" && t != "Off";
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

        private void DrawConnector(DrawingContext c, LayoutSlot s)
        {
            var chip = s.Chip;
            Point from = s.Side == SlotSide.Left ? new Point(chip.X + chip.W, chip.Y + chip.H / 2)
                : s.Side == SlotSide.Right ? new Point(chip.X, chip.Y + chip.H / 2)
                : new Point(chip.X + chip.W / 2, chip.Y);
            bool hot = IsPressed(s) || string.Equals(s.Id, selectedId, StringComparison.OrdinalIgnoreCase);
            c.DrawLine(hot ? LineHotPen : LinePen, from, new Point(s.MX, s.MY));
        }

        private void DrawMarker(DrawingContext c, LayoutSlot s)
        {
            bool selected = string.Equals(s.Id, selectedId, StringComparison.OrdinalIgnoreCase);
            bool available = !s.IsPaddle || IsAvailable(s.Id);
            IBrush fill = IsPressed(s) ? Accent : Idle;
            Pen pen = selected ? SelectedPen : IsBound(s.Id) ? new Pen(Accent, 1.5) : IdlePen;
            using (c.PushOpacity(available ? 1.0 : 0.4))
            {
                if (s.Shape == SlotShape.Circle) c.DrawEllipse(fill, pen, new Point(s.MX, s.MY), s.MW / 2, s.MH / 2);
                else c.DrawRectangle(fill, pen, new Rect(s.MX - s.MW / 2, s.MY - s.MH / 2, s.MW, s.MH), 5, 5);

                string glyph = Glyph(s);
                if (!string.IsNullOrEmpty(glyph) && s.MW >= 18 && s.MH >= 14)
                    DrawText(c, glyph, 10, IsPressed(s) ? TextBright : TextDim, new Point(s.MX, s.MY), true, false);
            }

            double flash = FlashAmount(s.Id);
            if (flash > 0)
            {
                IBrush ring = new SolidColorBrush(Color.FromArgb((byte)(255 * flash), 0x4C, 0xD7, 0x87));
                double grow = 6 * (1 - flash) + 3;
                if (s.Shape == SlotShape.Circle) c.DrawEllipse(null, new Pen(ring, 3), new Point(s.MX, s.MY), s.MW / 2 + grow, s.MH / 2 + grow);
                else c.DrawRectangle(null, new Pen(ring, 3), new Rect(s.MX - s.MW / 2 - grow, s.MY - s.MH / 2 - grow, s.MW + 2 * grow, s.MH + 2 * grow), 7, 7);
            }
        }

        private string Glyph(LayoutSlot s)
        {
            if (s.Id == ControllerEngine.ActionLtL2) return family == PadFamily.Xbox360 ? "LT" : "L2";
            if (s.Id == ControllerEngine.ActionRtR2) return family == PadFamily.Xbox360 ? "RT" : "R2";
            if (s.Id == ControllerEngine.ActionLbL1) return family == PadFamily.Xbox360 ? "LB" : "L1";
            if (s.Id == ControllerEngine.ActionRbR1) return family == PadFamily.Xbox360 ? "RB" : "R1";
            if (s.Id == ControllerEngine.ActionLsL3) return "L3";
            if (s.Id == ControllerEngine.ActionRsR3) return "R3";
            if (s.Id == ControllerEngine.ActionDPadUp || s.Id == ControllerEngine.ActionLeftStickUp) return "↑";
            if (s.Id == ControllerEngine.ActionDPadDown || s.Id == ControllerEngine.ActionLeftStickDown) return "↓";
            if (s.Id == ControllerEngine.ActionDPadLeft || s.Id == ControllerEngine.ActionLeftStickLeft) return "←";
            if (s.Id == ControllerEngine.ActionDPadRight || s.Id == ControllerEngine.ActionLeftStickRight) return "→";
            if (s.Id == ControllerEngine.ActionACross) return family == PadFamily.Xbox360 ? "A" : "✕";
            if (s.Id == ControllerEngine.ActionBCircle) return family == PadFamily.Xbox360 ? "B" : "○";
            if (s.Id == ControllerEngine.ActionXSquare) return family == PadFamily.Xbox360 ? "X" : "□";
            if (s.Id == ControllerEngine.ActionYTriangle) return family == PadFamily.Xbox360 ? "Y" : "△";
            if (s.Id == ControllerEngine.ActionTouchpad) return "TOUCHPAD";
            if (s.Id == ControllerEngine.ActionGuidePs) return family == PadFamily.Xbox360 ? "" : "PS";
            if (s.Id == ControllerLayout.RightStickId) return "🖱";
            return "";
        }

        private void DrawChip(DrawingContext c, LayoutSlot s)
        {
            var chip = s.Chip;
            bool selected = string.Equals(s.Id, selectedId, StringComparison.OrdinalIgnoreCase);
            bool available = !s.IsPaddle || IsAvailable(s.Id);
            bool enabled = !s.IsPaddle || IsEnabled(s.Id);
            string mapping = MappingText(s.Id);
            string native = NativeText(s.Id);
            string line2 = mapping;
            if (s.IsPaddle && !enabled) line2 = "Off";
            else if (!string.IsNullOrEmpty(native)) line2 = mapping + "  ·  " + native;
            if (s.IsPaddle && !available) line2 += "  (not detected)";
            line2 = Ellipsize(line2, 36);

            double flash = FlashAmount(s.Id);
            IBrush fill = flash > 0 ? new SolidColorBrush(Color.FromArgb((byte)(70 * flash), 0x4C, 0xD7, 0x87)) : ChipFill;
            Rect r = new Rect(chip.X, chip.Y, chip.W, chip.H);
            using (c.PushOpacity(available ? 1.0 : 0.55))
            {
                c.DrawRectangle(ChipFill, null, r, 6, 6);
                c.DrawRectangle(fill, selected ? SelectedPen : IsPressed(s) ? new Pen(Accent, 2) : IdlePen, r, 6, 6);
                DrawText(c, s.Title, 10, TextDim, new Point(r.X + 8, r.Y + 3), false, true);
                bool unset = string.IsNullOrEmpty(mapping) || mapping == "Not set" || (s.IsPaddle && !enabled);
                DrawText(c, line2, 11, unset ? TextDim : TextBright, new Point(r.X + 8, r.Y + 16), false, false);
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
