using System.Linq;
using Xunit;

namespace GamepadEnhancer.Tests
{
    public class PaddleLayoutTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PaddleChipsFitTheCanvasAndNeverCoverEachOtherOrAMarker(bool xbox)
        {
            PadFamily family = xbox ? PadFamily.Xbox360 : PadFamily.DualShock4;
            var slots = ControllerLayout.ForPaddles(family);
            Assert.Equal(4, slots.Count);
            foreach (LayoutSlot s in slots)
            {
                var c = s.Chip;
                Assert.True(c.X >= 0 && c.Y >= 0 && c.X + c.W <= ControllerLayout.Width && c.Y + c.H <= ControllerLayout.Height, s.Id + " chip is outside the canvas");
                Assert.True(c.W >= ControllerLayout.PaddleChipW && c.H >= ControllerLayout.PaddleChipH);
            }
            foreach (LayoutSlot a in slots)
                foreach (LayoutSlot b in slots.Where(x => x != a))
                {
                    var p = a.Chip; var q = b.Chip;
                    bool overlap = p.X < q.X + q.W && q.X < p.X + p.W && p.Y < q.Y + q.H && q.Y < p.Y + p.H;
                    Assert.False(overlap, a.Id + " chip overlaps " + b.Id);
                    Assert.False(a.Chip.X < b.MX + b.MW / 2 && b.MX - b.MW / 2 < a.Chip.X + a.Chip.W
                                 && a.Chip.Y < b.MY + b.MH / 2 && b.MY - b.MH / 2 < a.Chip.Y + a.Chip.H, a.Id + " chip covers the " + b.Id + " marker");
                }
        }
    }
}
