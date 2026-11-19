using System;
using System.Collections.Generic;
using System.Linq;

namespace GamepadEnhancer
{
    internal enum SlotSide { Left, Right, Bottom, Top }
    /// <summary>The configuration diagram has two pages: every button (with the shoulder, D-pad, face and stick-click chips) and the stick directions.</summary>
    internal enum DiagramPage { Buttons, Sticks }
    /// <summary>A titled panel that frames a few related chips (the D-pad, the face buttons, a stick).</summary>
    internal readonly record struct SlotGroup(string Title, double X, double Y, double W, double H);
    internal enum SlotShape { Circle, RoundRect }

    /// <summary>
    /// One selectable control on the configuration diagram: a marker on the controller drawing plus a label chip beside it.
    /// <see cref="Id"/> is the pad action name, <c>RS_MOVE</c> (the right stick, mouse movement) or a paddle id such as <c>PADDLE_LEFT_UPPER</c>.
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
        /// <summary>Explicit chip position (diagram coordinates); null = the default column for <see cref="Side"/> and <see cref="Row"/>.</summary>
        public double? ChipX { get; init; }
        public double? ChipY { get; init; }
        /// <summary>Explicit chip size; null = the standard chip. The paddle view uses bigger chips (only four of them, so there is room).</summary>
        public double? ChipWidth { get; init; }
        public double? ChipHeight { get; init; }
        /// <summary>Which page of the diagram shows this slot's chip.</summary>
        public DiagramPage Page { get; init; }
        /// <summary>Title of the group panel this chip sits in (null = not in a group).</summary>
        public string Group { get; init; }

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
        public const string RightStickId = Inputs.RightStickMove;

        private static readonly Dictionary<PadFamily, IReadOnlyList<LayoutSlot>> Cache = new();

        public static IReadOnlyList<LayoutSlot> For(PadFamily family)
        {
            lock (Cache)
            {
                if (!Cache.TryGetValue(family, out IReadOnlyList<LayoutSlot> list)) Cache[family] = list = Build(family);
                return list;
            }
        }

        private static readonly Dictionary<(PadFamily, DiagramPage), IReadOnlyList<LayoutSlot>> PageCache = new();

        /// <summary>The slots shown on one page of the diagram.</summary>
        public static IReadOnlyList<LayoutSlot> For(PadFamily family, DiagramPage page)
        {
            lock (PageCache)
            {
                if (!PageCache.TryGetValue((family, page), out IReadOnlyList<LayoutSlot> list))
                    PageCache[(family, page)] = list = For(family).Where(s => s.Page == page).ToList();
                return list;
            }
        }

        /// <summary>The group panels of one page: each frames the chips that share a <see cref="LayoutSlot.Group"/>, with room for its title.</summary>
        public static IReadOnlyList<SlotGroup> Groups(PadFamily family, DiagramPage page)
        {
            var result = new List<SlotGroup>();
            foreach (var g in For(family, page).Where(s => s.Group != null).GroupBy(s => s.Group))
            {
                double x0 = g.Min(s => s.Chip.X), y0 = g.Min(s => s.Chip.Y), x1 = g.Max(s => s.Chip.X + s.Chip.W), y1 = g.Max(s => s.Chip.Y + s.Chip.H);
                result.Add(new SlotGroup(g.Key, x0 - GroupPad, y0 - GroupTitleH, x1 - x0 + 2 * GroupPad, y1 - y0 + GroupTitleH + GroupPad));
            }
            return result;
        }

        public const double GroupPad = 8, GroupTitleH = 24;

        public static LayoutSlot Find(PadFamily family, string id) =>
            For(family).FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>Position of a slot's label chip in diagram coordinates.</summary>
        public static (double X, double Y, double W, double H) ChipRect(LayoutSlot s)
        {
            if (s.ChipX.HasValue && s.ChipY.HasValue) return (s.ChipX.Value, s.ChipY.Value, s.ChipWidth ?? ChipW, s.ChipHeight ?? ChipH);
            switch (s.Side)
            {
                case SlotSide.Left: return (12, 16 + s.Row * (ChipH + ChipGap), ChipW, ChipH);
                case SlotSide.Right: return (Width - 12 - ChipW, 16 + s.Row * (ChipH + ChipGap), ChipW, ChipH);
                case SlotSide.Top: return (Width / 2 - 8 - ChipW + s.Row * (ChipW + 16), 16, ChipW, ChipH);
                default: return (40 + s.Row * (ChipW + 18), Height - ChipH - 12, ChipW, ChipH);
            }
        }

        /// <summary>The slot under a point: a label chip first, then the smallest marker containing it (so a stick direction wins over the stick click ring).</summary>
        public static LayoutSlot HitTest(PadFamily family, double x, double y) => HitTest(For(family), x, y);

        public static LayoutSlot HitTest(IReadOnlyList<LayoutSlot> slots, double x, double y)
        {
            foreach (LayoutSlot s in slots) if (s.ChipContains(x, y)) return s;
            return slots.Where(s => s.MarkerContains(x, y)).OrderBy(s => s.MW * s.MH).FirstOrDefault();
        }

        /// <summary>The paddle chips are larger than the standard ones: a title line, a bigger mapping line and a note line.</summary>
        public const double PaddleChipW = 224, PaddleChipH = 64;

        private static readonly Dictionary<PadFamily, IReadOnlyList<LayoutSlot>> PaddleCache = new();

        /// <summary>
        /// The paddle-focused layout: only the four paddles, drawn large on the rear view, with their chips beside the grips
        /// (left pair on the left, right pair on the right). Same slot ids as the paddles in <see cref="For"/>.
        /// </summary>
        public static IReadOnlyList<LayoutSlot> ForPaddles(PadFamily family)
        {
            lock (PaddleCache)
            {
                if (PaddleCache.TryGetValue(family, out IReadOnlyList<LayoutSlot> cached)) return cached;
                var list = new List<LayoutSlot>();
                double[] px = { 262, 262, 618, 618 };
                double[] py = { 300, 372, 300, 372 };
                for (int i = 0; i < PaddleSlots.Count; i++)
                {
                    bool left = i < 2;
                    list.Add(new LayoutSlot
                    {
                        Id = PaddleSlots.Id(i),
                        Title = ControllerLayouts.ForDevice(family).Label(PaddleSlots.Names[i]),
                        Side = left ? SlotSide.Left : SlotSide.Right,
                        Row = i % 2,
                        MX = px[i], MY = py[i], MW = 24, MH = 56, Shape = SlotShape.RoundRect,
                        ChipWidth = PaddleChipW, ChipHeight = PaddleChipH,
                        ChipX = left ? 12 : Width - 12 - PaddleChipW,
                        ChipY = 280 + (i % 2) * (PaddleChipH + 12)
                    });
                }
                return PaddleCache[family] = list;
            }
        }

        private static IReadOnlyList<LayoutSlot> Build(PadFamily family)
        {
            var list = new List<LayoutSlot>();
            const double lx = 12, rx = Width - 12 - ChipW, gw = ChipW - 2 * GroupPad, glx = lx + GroupPad, grx = rx + GroupPad;
            const double topLeft = Width / 2 - 8 - ChipW, topRight = Width / 2 + 8;
            const SlotShape C = SlotShape.Circle, R = SlotShape.RoundRect;
            const SlotSide L = SlotSide.Left, Rt = SlotSide.Right, T = SlotSide.Top;
            const DiagramPage B = DiagramPage.Buttons, S = DiagramPage.Sticks;

            // Marker (on the drawing) and chip (beside it) of one control. A chip in a group is narrower and its title drops the group's name.
            void Slot(string action, DiagramPage page, string group, SlotSide side, double chipX, double chipY, double mx, double my, double mw, double mh, SlotShape shape) =>
                list.Add(new LayoutSlot
                {
                    Id = action,
                    Title = PadLabels.For(family, Array.IndexOf(ControllerEngine.BindingActions, action)),
                    Side = side, Page = page, Group = group,
                    ChipX = chipX, ChipY = chipY, ChipWidth = group != null ? gw : ChipW,
                    MX = mx, MY = my, MW = mw, MH = mh, Shape = shape
                });

            // chip rows inside the two column groups: the D-pad / face buttons on the buttons page, a stick on the sticks page
            double[] row = { 120, 158, 196, 234 };
            double[] stickRow = { 40, 78, 116, 154 };

            if (family == PadFamily.DualShock4)
            {
                Slot(ControllerEngine.ActionLtL2, B, null, L, lx, 16, 340, 118, 80, 26, R);
                Slot(ControllerEngine.ActionLbL1, B, null, L, lx, 54, 338, 146, 96, 16, R);
                Slot(ControllerEngine.ActionBackShare, B, null, T, topLeft, 16, 372, 184, 12, 24, R);
                Slot(ControllerEngine.ActionDPadUp, B, "D-Pad", L, glx, row[0], 350, 218, 22, 22, R);
                Slot(ControllerEngine.ActionDPadDown, B, "D-Pad", L, glx, row[1], 350, 262, 22, 22, R);
                Slot(ControllerEngine.ActionDPadLeft, B, "D-Pad", L, glx, row[2], 328, 240, 22, 22, R);
                Slot(ControllerEngine.ActionDPadRight, B, "D-Pad", L, glx, row[3], 372, 240, 22, 22, R);
                Slot(ControllerEngine.ActionLsL3, B, null, L, lx, 286, 400, 300, 60, 60, C);
                Slot(ControllerEngine.ActionGuidePs, B, null, L, lx, 324, 440, 300, 16, 16, C);
                Slot(ControllerEngine.ActionLeftStickUp, S, "Left Stick", L, glx, stickRow[0], 400, 268, 16, 16, C);
                Slot(ControllerEngine.ActionLeftStickDown, S, "Left Stick", L, glx, stickRow[1], 400, 332, 16, 16, C);
                Slot(ControllerEngine.ActionLeftStickLeft, S, "Left Stick", L, glx, stickRow[2], 366, 300, 16, 16, C);
                Slot(ControllerEngine.ActionLeftStickRight, S, "Left Stick", L, glx, stickRow[3], 434, 300, 16, 16, C);

                Slot(ControllerEngine.ActionRtR2, B, null, Rt, rx, 16, 540, 118, 80, 26, R);
                Slot(ControllerEngine.ActionRbR1, B, null, Rt, rx, 54, 542, 146, 96, 16, R);
                Slot(ControllerEngine.ActionStartOptions, B, null, T, topRight, 16, 508, 184, 12, 24, R);
                Slot(ControllerEngine.ActionACross, B, "Face Buttons", Rt, grx, row[0], 530, 264, 24, 24, C);
                Slot(ControllerEngine.ActionBCircle, B, "Face Buttons", Rt, grx, row[1], 554, 240, 24, 24, C);
                Slot(ControllerEngine.ActionXSquare, B, "Face Buttons", Rt, grx, row[2], 506, 240, 24, 24, C);
                Slot(ControllerEngine.ActionYTriangle, B, "Face Buttons", Rt, grx, row[3], 530, 216, 24, 24, C);
                Slot(ControllerEngine.ActionRsR3, B, null, Rt, rx, 286, 480, 300, 60, 60, C);
                Slot(ControllerEngine.ActionTouchpad, B, null, Rt, rx, 324, 440, 205, 120, 56, R);
                list.Add(new LayoutSlot { Id = RightStickId, Title = Inputs.NeutralLabel(Inputs.RightStickMove), Side = Rt, Page = S, Group = "Right Stick",
                    ChipX = grx, ChipY = stickRow[0], ChipWidth = gw, MX = 480, MY = 300, MW = 36, MH = 36, Shape = C });
            }
            else
            {
                // Xbox layout: left stick high on the left, D-pad below it, face buttons high on the right, right stick below them.
                Slot(ControllerEngine.ActionLtL2, B, null, L, lx, 16, 330, 98, 70, 22, R);
                Slot(ControllerEngine.ActionLbL1, B, null, L, lx, 54, 330, 124, 90, 14, R);
                Slot(ControllerEngine.ActionBackShare, B, null, T, topLeft, 16, 410, 184, 14, 14, C);
                Slot(ControllerEngine.ActionDPadUp, B, "D-Pad", L, glx, row[0], 374, 229, 22, 22, R);
                Slot(ControllerEngine.ActionDPadDown, B, "D-Pad", L, glx, row[1], 374, 273, 22, 22, R);
                Slot(ControllerEngine.ActionDPadLeft, B, "D-Pad", L, glx, row[2], 352, 251, 22, 22, R);
                Slot(ControllerEngine.ActionDPadRight, B, "D-Pad", L, glx, row[3], 396, 251, 22, 22, R);
                Slot(ControllerEngine.ActionLsL3, B, null, L, lx, 286, 325, 182, 60, 60, C);
                Slot(ControllerEngine.ActionGuidePs, B, null, L, lx, 324, 440, 139, 30, 30, C);
                Slot(ControllerEngine.ActionLeftStickUp, S, "Left Stick", L, glx, stickRow[0], 325, 150, 16, 16, C);
                Slot(ControllerEngine.ActionLeftStickDown, S, "Left Stick", L, glx, stickRow[1], 325, 214, 16, 16, C);
                Slot(ControllerEngine.ActionLeftStickLeft, S, "Left Stick", L, glx, stickRow[2], 291, 182, 16, 16, C);
                Slot(ControllerEngine.ActionLeftStickRight, S, "Left Stick", L, glx, stickRow[3], 359, 182, 16, 16, C);

                Slot(ControllerEngine.ActionRtR2, B, null, Rt, rx, 16, 550, 98, 70, 22, R);
                Slot(ControllerEngine.ActionRbR1, B, null, Rt, rx, 54, 550, 124, 90, 14, R);
                Slot(ControllerEngine.ActionStartOptions, B, null, T, topRight, 16, 470, 184, 14, 14, C);
                Slot(ControllerEngine.ActionACross, B, "Face Buttons", Rt, grx, row[0], 550, 211, 26, 26, C);
                Slot(ControllerEngine.ActionBCircle, B, "Face Buttons", Rt, grx, row[1], 578, 184, 26, 26, C);
                Slot(ControllerEngine.ActionXSquare, B, "Face Buttons", Rt, grx, row[2], 522, 184, 26, 26, C);
                Slot(ControllerEngine.ActionYTriangle, B, "Face Buttons", Rt, grx, row[3], 550, 157, 26, 26, C);
                Slot(ControllerEngine.ActionRsR3, B, null, Rt, rx, 286, 486, 251, 60, 60, C);
                list.Add(new LayoutSlot { Id = RightStickId, Title = Inputs.NeutralLabel(Inputs.RightStickMove), Side = Rt, Page = S, Group = "Right Stick",
                    ChipX = grx, ChipY = stickRow[0], ChipWidth = gw, MX = 486, MY = 251, MW = 36, MH = 36, Shape = C });
            }

            // Rear view: four paddles, left pair and right pair (chips along the bottom of the buttons page).
            double[] px = { 352, 352, 528, 528 };
            double[] py = { 462, 502, 462, 502 };
            for (int i = 0; i < PaddleSlots.Count; i++)
                list.Add(new LayoutSlot
                {
                    Id = PaddleSlots.Id(i), Title = ControllerLayouts.ForDevice(family).Label(PaddleSlots.Names[i]), Side = SlotSide.Bottom, Row = i, Page = B,
                    MX = px[i], MY = py[i], MW = 16, MH = 30, Shape = R
                });
            // controls the controller does not have are not drawn (a DualShock 4 has no rear paddles)
            ControllerLayoutDef layout = ControllerLayouts.ForDevice(family);
            list.RemoveAll(s => !layout.Supports(s.Id));
            return list;
        }
    }
}
