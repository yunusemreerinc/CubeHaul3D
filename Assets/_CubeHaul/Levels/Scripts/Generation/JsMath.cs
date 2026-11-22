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

        /// <summary><see cref="Round"/> of a value known to fit an int.</summary>
        public static int RoundToInt(double x) => checked((int)Round(x));
    }
}
