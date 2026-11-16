using GamepadEnhancer;
using Xunit;

namespace GamepadEnhancer.Tests
{
    public class MouseLookTests
    {
        [Fact]
        public void NoMovementGivesZero()
        {
            var m = new MouseLook();
            var (x, y) = m.Step(0, 8, 8, 1, 0, 30, false);
            Assert.Equal(0, x); Assert.Equal(0, y);
        }

        [Fact]
        public void MovementMapsToStickAndDownIsPositiveY()
        {
            var m = new MouseLook();
            m.Step(0, 8, 8, 1, 0, 30, false);
            m.Accumulate(10, 5);   // 10 counts in 10 ms = 1 count/ms -> 8 %
            var (x, y) = m.Step(10, 8, 8, 1, 0, 30, false);
            Assert.Equal(8.0, x, 6);
            Assert.Equal(4.0, y, 6);
        }

        [Fact]
        public void NegativeMovementGivesNegativeStick()
        {
            var m = new MouseLook();
            m.Step(0, 8, 8, 1, 0, 30, false);
            m.Accumulate(-10, -10);
            var (x, y) = m.Step(10, 8, 8, 1, 0, 30, false);
            Assert.True(x < 0 && y < 0);
        }

        [Fact]
        public void InvertYFlipsOnlyY()
        {
            var m = new MouseLook();
            m.Step(0, 8, 8, 1, 0, 30, true);
            m.Accumulate(10, 10);
            var (x, y) = m.Step(10, 8, 8, 1, 0, 30, true);
            Assert.True(x > 0 && y < 0);
        }

        [Fact]
        public void OutputClampsAtFullDeflection()
        {
            var m = new MouseLook();
            m.Step(0, 50, 50, 1, 0, 30, false);
            m.Accumulate(5000, -5000);
            var (x, y) = m.Step(10, 50, 50, 1, 0, 30, false);
            Assert.Equal(100.0, x, 6);
            Assert.Equal(-100.0, y, 6);
        }

        [Fact]
        public void CurveMakesSmallMovesFiner()
        {
            Assert.True(MouseLook.Shape(20, 2.0, 0) < MouseLook.Shape(20, 1.0, 0));
            Assert.Equal(100.0, MouseLook.Shape(100, 2.0, 0), 6);
        }

        [Fact]
        public void GameDeadZoneLiftsSmallOutputPastIt()
        {
            double lifted = MouseLook.Shape(1, 1.0, 0.2);
            Assert.True(lifted >= 20.0);                      // starts just past the 20 % dead zone
            Assert.Equal(0.0, MouseLook.Shape(0, 1.0, 0.2));  // zero stays zero
            Assert.Equal(100.0, MouseLook.Shape(100, 1.0, 0.2), 6);
            Assert.True(MouseLook.Shape(-1, 1.0, 0.2) <= -20.0);
        }

        [Fact]
        public void HoldsThenRecentersAfterReleaseTime()
        {
            var m = new MouseLook();
            m.Step(0, 8, 8, 1, 0, 30, false);
            m.Accumulate(10, 0);
            var (x0, _) = m.Step(10, 8, 8, 1, 0, 30, false);
            Assert.True(x0 > 0);
            var (xHold, _) = m.Step(20, 8, 8, 1, 0, 30, false);   // 10 ms after the last move: still held
            Assert.Equal(x0, xHold);
            var (xGone, yGone) = m.Step(60, 8, 8, 1, 0, 30, false); // 50 ms after: recentered
            Assert.Equal(0.0, xGone); Assert.Equal(0.0, yGone);
        }

        [Fact]
        public void ResetClearsEverything()
        {
            var m = new MouseLook();
            m.Step(0, 8, 8, 1, 0, 30, false);
            m.Accumulate(100, 100);
            m.Reset();
            var (x, y) = m.Step(10, 8, 8, 1, 0, 30, false);
            Assert.Equal(0.0, x); Assert.Equal(0.0, y);
        }
    }
}
