namespace GamepadEnhancer
{
    /// <summary>
    /// Exponential low-pass filter for the right stick (X and Y, in percent). It is seeded with the first target
    /// after a reset so it never starts from stale values.
    /// </summary>
    public sealed class LowPassFilter
    {
        /// <summary>Snap to the target once this close (percent), so the output settles exactly even at 16-bit resolution.</summary>
        private const double SnapEpsilonPercent = 0.001;

        private double x, y;
        private bool seeded;

        /// <summary>True once the first target after a reset has been taken.</summary>
        public bool Seeded => seeded;

        public void Reset() => seeded = false;

        private static double Approach(double current, double target, double alpha)
        {
            double next = current + alpha * (target - current);
            return System.Math.Abs(target - next) < SnapEpsilonPercent ? target : next;
        }

        /// <summary>out += alpha * (target - out) per axis; the first call after Reset returns the target unchanged.</summary>
        public (double X, double Y) Step(double targetX, double targetY, double alpha)
        {
            if (!seeded)
            {
                x = targetX;
                y = targetY;
                seeded = true;
                return (x, y);
            }
            x = Approach(x, targetX, alpha);
            y = Approach(y, targetY, alpha);
            return (x, y);
        }
    }
}
