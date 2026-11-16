using System;

namespace GamepadEnhancer
{
    /// <summary>
    /// Turns raw mouse deltas into a right-stick vector (percent, -100..100). Pure and Win32-free.
    /// Velocity based: counts per millisecond since the last step go through a response curve, then are lifted past the
    /// game's own inner dead zone. With no movement the last output is held for <c>ReleaseMs</c> (smooths low mouse
    /// polling rates) and then drops to 0, so the stick always recenters.
    /// Y is positive when the mouse moves DOWN (the PS4 convention); the engine flips it for Xbox.
    /// </summary>
    internal sealed class MouseLook
    {
        private readonly object sync = new();
        private long pendingX, pendingY;
        private bool hasLast;
        private double lastStepMs;
        private double lastMoveMs;
        private double outX, outY;

        /// <summary>Called from the input thread.</summary>
        public void Accumulate(int dx, int dy)
        {
            if (dx == 0 && dy == 0) return;
            lock (sync) { pendingX += dx; pendingY += dy; }
        }

        public void Reset()
        {
            lock (sync) { pendingX = pendingY = 0; hasLast = false; outX = outY = 0; }
        }

        /// <param name="nowMs">Monotonic time in milliseconds.</param>
        /// <param name="sensX">Percent of stick per (count/ms) horizontally; 10 means 10 counts/ms is full tilt.</param>
        /// <param name="curve">Response exponent; 1 = linear, above 1 = finer control at low speed.</param>
        /// <param name="gameDeadZone">Fraction (0..0.5) the game ignores around the center; non-zero output starts just past it.</param>
        public (double X, double Y) Step(double nowMs, double sensX, double sensY, double curve, double gameDeadZone, int releaseMs, bool invertY)
        {
            long ax, ay;
            lock (sync) { ax = pendingX; ay = pendingY; pendingX = pendingY = 0; }

            double dt = hasLast ? Math.Clamp(nowMs - lastStepMs, 1.0, 50.0) : 10.0;
            hasLast = true;
            lastStepMs = nowMs;

            if (ax != 0 || ay != 0)
            {
                lastMoveMs = nowMs;
                outX = Shape(ax / dt * sensX, curve, gameDeadZone);
                outY = Shape(ay / dt * sensY, curve, gameDeadZone);
                if (invertY) outY = -outY;
            }
            else if (nowMs - lastMoveMs > Math.Max(0, releaseMs))
            {
                outX = outY = 0;
            }
            return (outX, outY);
        }

        /// <summary>Response curve + game dead-zone lift for one axis; input and output in percent.</summary>
        internal static double Shape(double percent, double curve, double gameDeadZone)
        {
            double mag = Math.Min(Math.Abs(percent), 100.0) / 100.0;
            if (mag <= 0) return 0;
            double c = curve <= 0 ? 1.0 : curve;
            mag = Math.Pow(mag, c);
            double dz = Math.Clamp(gameDeadZone, 0.0, 0.5);
            mag = dz + mag * (1.0 - dz);
            double result = Math.Min(mag, 1.0) * 100.0;
            return percent < 0 ? -result : result;
        }
    }
}
