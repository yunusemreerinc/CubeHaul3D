using System;

namespace CubeHaul.Levels.Generation
{
    /// <summary>JavaScript number functions whose C# look-alikes give other results (PORTING_PLAN R-02).</summary>
    public static class JsMath
    {
        /// <summary>
        /// JavaScript <c>Math.round</c>: to the nearest integer, halves toward +∞ (2.5 → 3, −2.5 → −2). C#'s
        /// <c>Math.Round</c> rounds halves to even by default (2.5 → 2), and <c>Math.Floor(x + 0.5)</c> is wrong for
        /// 0.49999999999999994 (x + 0.5 rounds up to 1).
        /// </summary>
        public static double Round(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return x;
            double r = Math.Floor(x);
            return x - r >= 0.5 ? r + 1 : r;
        }

        /// <summary>
        /// JavaScript <c>Math.hypot(x, y)</c> as V8 computes it: both values scaled by the larger one, the squares
        /// summed with Kahan compensation, the root scaled back. <c>Math.Sqrt(x * x + y * y)</c> differs from it in the
        /// last bit for about 40% of inputs, and a truck's position is the sum of thousands of such steps.
        /// </summary>
        public static double Hypot(double x, double y)
        {
            if (double.IsInfinity(x) || double.IsInfinity(y)) return double.PositiveInfinity;
            if (double.IsNaN(x) || double.IsNaN(y)) return double.NaN;
            double a = Math.Abs(x), b = Math.Abs(y);
            double max = Math.Max(a, b);
            if (max == 0) return 0;
            double sum = 0, compensation = 0;
            double n = a / max;
            double summand = n * n - compensation;
            double preliminary = sum + summand;
            compensation = preliminary - sum - summand;
            sum = preliminary;
            n = b / max;
            summand = n * n - compensation;
            preliminary = sum + summand;
            sum = preliminary;
            return Math.Sqrt(sum) * max;
        }

        /// <summary><see cref="Round"/> of a value known to fit an int.</summary>
        public static int RoundToInt(double x) => checked((int)Round(x));
    }
}
