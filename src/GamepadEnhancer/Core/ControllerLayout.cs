using System;
using System.Collections.Generic;
using System.Linq;

namespace GamepadEnhancer
{
    internal enum SlotSide { Left, Right, Bottom }
    internal enum SlotShape { Circle, RoundRect }

    /// <summary>
    /// One selectable control on the configuration diagram: a marker on the controller drawing plus a label chip beside it.
    /// <see cref="Id"/> is the pad action name, <c>"RightStick"</c> (mouse movement) or <c>"Paddle:&lt;Slot&gt;"</c>.
    /// </summary>
    internal sealed class LayoutSlot
    {
        public string Id { get; init; }
        public string Title { get; init; }
        public SlotSide Side { get; init; }
        public int Row { get; init; }
        /// <summary>Marker centre and size in diagram coordinates.</summary>
        public double MX { get; init; }
        public double MY { get; init; }
        public double MW { get; init; }
        public double MH { get; init; }
        public SlotShape Shape { get; init; }

        public bool IsPaddle => PaddleSlots.TryParseId(Id, out _);

        public (double X, double Y, double W, double H) Chip => ControllerLayout.ChipRect(this);

        public bool MarkerContains(double x, double y)
        {
            double dx = x - MX, dy = y - MY;
            const double slack = 3;
            if (Shape == SlotShape.Circle) { double r = Math.Max(MW, MH) / 2 + slack; return dx * dx + dy * dy <= r * r; }
            return Math.Abs(dx) <= MW / 2 + slack && Math.Abs(dy) <= MH / 2 + slack;
        }

        public bool ChipContains(double x, double y)
        {
            var c = Chip;
            return x >= c.X && x <= c.X + c.W && y >= c.Y && y <= c.Y + c.H;
        }
    }

    /// <summary>
    /// Geometry of the interactive controller diagram for each supported controller layout (DualShock 4 and Xbox 360), including the
    /// rear view with the four paddles. Pure data and hit testing, so the drawing control stays a thin renderer and this is unit
    /// tested. The diagram has a fixed logical size; the control scales it to the window.
    /// </summary>
    internal static class ControllerLayout
    {
        public const double Width = 880, Height = 620, ChipW = 196, ChipH = 32, ChipGap = 6;
        public const string RightStickId = "RightStick";

        private static readonly Dictionary<PadFamily, IReadOnlyList<LayoutSlot>> Cache = new();

        public static IReadOnlyList<LayoutSlot> For(PadFamily family)
        {
            lock (Cache)
            {
                if (!Cache.TryGetValue(family, out IReadOnlyList<LayoutSlot> list)) Cache[family] = list = Build(family);
                return list;
            }
        }

        public static LayoutSlot Find(PadFamily family, string id) =>
            For(family).FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>Position of a slot's label chip in diagram coordinates.</summary>
        public static (double X, double Y, double W, double H) ChipRect(LayoutSlot s)
        {
            switch (s.Side)
            {
                case SlotSide.Left: return (12, 16 + s.Row * (ChipH + ChipGap), ChipW, ChipH);
                case SlotSide.Right: return (Width - 12 - ChipW, 16 + s.Row * (ChipH + ChipGap), ChipW, ChipH);
                default: return (40 + s.Row * (ChipW + 18), Height - ChipH - 12, ChipW, ChipH);
            }
        }

        /// <summary>The slot under a point: a label chip first, then the smallest marker containing it (so a stick direction wins over the stick click ring).</summary>
        public static LayoutSlot HitTest(PadFamily family, double x, double y)
        {
            IReadOnlyList<LayoutSlot> slots = For(family);
            foreach (LayoutSlot s in slots) if (s.ChipContains(x, y)) return s;
            return slots.Where(s => s.MarkerContains(x, y)).OrderBy(s => s.MW * s.MH).FirstOrDefault();
        }

        private static IReadOnlyList<LayoutSlot> Build(PadFamily family)
        {
            var list = new List<LayoutSlot>();
            void Slot(string action, SlotSide side, int row, double mx, double my, double mw, double mh, SlotShape shape) =>
                list.Add(new LayoutSlot
                {
                    Id = action,
                    Title = PadLabels.For(family, Array.IndexOf(ControllerEngine.BindingActions, action)),
                    Side = side, Row = row, MX = mx, MY = my, MW = mw, MH = mh, Shape = shape
                });

            const SlotShape C = SlotShape.Circle, R = SlotShape.RoundRect;
            const SlotSide L = SlotSide.Left, Rt = SlotSide.Right;

            if (family == PadFamily.DualShock4)
            {
                Slot(ControllerEngine.ActionLtL2, L, 0, 340, 118, 80, 26, R);
                Slot(ControllerEngine.ActionLbL1, L, 1, 338, 146, 96, 16, R);
                Slot(ControllerEngine.ActionBackShare, L, 2, 372, 184, 12, 24, R);
                Slot(ControllerEngine.ActionDPadUp, L, 3, 350, 218, 22, 22, R);
                Slot(ControllerEngine.ActionDPadLeft, L, 4, 328, 240, 22, 22, R);
                Slot(ControllerEngine.ActionDPadRight, L, 5, 372, 240, 22, 22, R);
                Slot(ControllerEngine.ActionDPadDown, L, 6, 350, 262, 22, 22, R);
                Slot(ControllerEngine.ActionLsL3, L, 7, 400, 300, 60, 60, C);
                Slot(ControllerEngine.ActionLeftStickUp, L, 8, 400, 268, 16, 16, C);
                Slot(ControllerEngine.ActionLeftStickLeft, L, 9, 366, 300, 16, 16, C);
                Slot(ControllerEngine.ActionLeftStickRight, L, 10, 434, 300, 16, 16, C);
                Slot(ControllerEngine.ActionLeftStickDown, L, 11, 400, 332, 16, 16, C);

                Slot(ControllerEngine.ActionRtR2, Rt, 0, 540, 118, 80, 26, R);
                Slot(ControllerEngine.ActionRbR1, Rt, 1, 542, 146, 96, 16, R);
                Slot(ControllerEngine.ActionStartOptions, Rt, 2, 508, 184, 12, 24, R);
                Slot(ControllerEngine.ActionTouchpad, Rt, 3, 440, 205, 120, 56, R);
                Slot(ControllerEngine.ActionYTriangle, Rt, 4, 530, 216, 24, 24, C);
                Slot(ControllerEngine.ActionBCircle, Rt, 5, 554, 240, 24, 24, C);
                Slot(ControllerEngine.ActionXSquare, Rt, 6, 506, 240, 24, 24, C);
                Slot(ControllerEngine.ActionACross, Rt, 7, 530, 264, 24, 24, C);
                Slot(ControllerEngine.ActionGuidePs, Rt, 8, 440, 300, 16, 16, C);
                list.Add(new LayoutSlot { Id = RightStickId, Title = "Right Stick", Side = Rt, Row = 9, MX = 480, MY = 300, MW = 36, MH = 36, Shape = C });
                Slot(ControllerEngine.ActionRsR3, Rt, 10, 480, 300, 60, 60, C);
            }
            else
            {
                Slot(ControllerEngine.ActionLtL2, L, 0, 340, 118, 80, 26, R);
                Slot(ControllerEngine.ActionLbL1, L, 1, 338, 146, 96, 16, R);
                Slot(ControllerEngine.ActionBackShare, L, 2, 408, 222, 14, 14, C);
                Slot(ControllerEngine.ActionDPadUp, L, 3, 400, 278, 22, 22, R);
                Slot(ControllerEngine.ActionDPadLeft, L, 4, 378, 300, 22, 22, R);
                Slot(ControllerEngine.ActionDPadRight, L, 5, 422, 300, 22, 22, R);
                Slot(ControllerEngine.ActionDPadDown, L, 6, 400, 322, 22, 22, R);
                Slot(ControllerEngine.ActionLsL3, L, 7, 356, 232, 60, 60, C);
                Slot(ControllerEngine.ActionLeftStickUp, L, 8, 356, 200, 16, 16, C);
                Slot(ControllerEngine.ActionLeftStickLeft, L, 9, 322, 232, 16, 16, C);
                Slot(ControllerEngine.ActionLeftStickRight, L, 10, 390, 232, 16, 16, C);
                Slot(ControllerEngine.ActionLeftStickDown, L, 11, 356, 264, 16, 16, C);

                Slot(ControllerEngine.ActionRtR2, Rt, 0, 540, 118, 80, 26, R);
                Slot(ControllerEngine.ActionRbR1, Rt, 1, 542, 146, 96, 16, R);
                Slot(ControllerEngine.ActionStartOptions, Rt, 2, 472, 222, 14, 14, C);
                Slot(ControllerEngine.ActionGuidePs, Rt, 3, 440, 196, 20, 20, C);
                Slot(ControllerEngine.ActionYTriangle, Rt, 4, 532, 206, 24, 24, C);
                Slot(ControllerEngine.ActionBCircle, Rt, 5, 556, 230, 24, 24, C);
                Slot(ControllerEngine.ActionXSquare, Rt, 6, 508, 230, 24, 24, C);
                Slot(ControllerEngine.ActionACross, Rt, 7, 532, 254, 24, 24, C);
                list.Add(new LayoutSlot { Id = RightStickId, Title = "Right Stick", Side = Rt, Row = 8, MX = 484, MY = 300, MW = 36, MH = 36, Shape = C });
                Slot(ControllerEngine.ActionRsR3, Rt, 9, 484, 300, 60, 60, C);
            }

            // Rear view: four paddles, left pair and right pair.
            double[] px = { 352, 352, 528, 528 };
            double[] py = { 462, 502, 462, 502 };
            for (int i = 0; i < PaddleSlots.Count; i++)
                list.Add(new LayoutSlot
                {
                    Id = PaddleSlots.Id(i), Title = PaddleSlots.Titles[i], Side = SlotSide.Bottom, Row = i,
                    MX = px[i], MY = py[i], MW = 16, MH = 30, Shape = R
                });
            return list;
        }
    }
}
