using System;
using CubeHaul.Levels.Generation;

namespace CubeHaul.Gameplay.Trucks
{
    /// <summary>Where a cube being loaded is, at one moment of the load (see <see cref="CubeLoad.At"/>).</summary>
    public readonly struct CubeLoadFrame
    {
        public CubeLoadFrame(double along, double height, double rock, bool inFlight)
        {
            Along = along;
            Height = height;
            Rock = rock;
            InFlight = inFlight;
        }

        /// <summary>
        /// How far it has moved from its cell toward the bed (0..1, eased): the same share blends its size to the bed
        /// cube's and turns it, the short way round, to sit square in the bed.
        /// </summary>
        public double Along { get; }
        /// <summary>Height above the straight line from its spot to the bed, design units.</summary>
        public double Height { get; }
        /// <summary>Rocking turn while it works loose, radians (only while lifting).</summary>
        public double Rock { get; }
        /// <summary>False once it is in the bed (from LAND_AT on): the truck's own cargo cube shows instead.</summary>
        public bool InFlight { get; }
    }

    /// <summary>
    /// The loading animation of main.ts drawLoading, engine-free (Task 15, docs/SESSION.md): through the first
    /// LIFT_END of LOAD_TIME the cube rocks loose and rises out of its spot; then, until LAND_AT, it arcs up and over
    /// into the truck's bed, easing in and out, shrinking to the bed cube's size and turning to line up with the truck.
    /// After LAND_AT it is in the bed. The view maps the frame onto its cell, the bed and the board's height.
    /// </summary>
    public static class CubeLoad
    {
        /// <summary>Share of LOAD_TIME spent working loose and rising (main.ts LIFT_END).</summary>
        public const double LiftEnd = 0.22;
        public const double LandAt = TruckMotion.LandAt;
        /// <summary>How high it rises while working loose, and the arc's extra height, in cells (main.ts: cs·0.3, cs·1.1).</summary>
        public const double Rise = 0.3, Peak = 1.1;

        /// <param name="t">How far through the load (TruckMotion.LoadProgress, 0..1).</param>
        /// <param name="cellSize">The level's cell size, design units.</param>
        public static CubeLoadFrame At(double t, double cellSize)
        {
            if (double.IsNaN(t)) throw new ArgumentOutOfRangeException(nameof(t));
            t = Math.Max(0, t);
            double rise = cellSize * Rise;
            double peak = cellSize * Peak;
            if (t >= LandAt) return new CubeLoadFrame(1, 0, 0, false);
            if (t < LiftEnd)
            {
                double k = t / LiftEnd;
                return new CubeLoadFrame(0, rise * k * k, Math.Sin(k * Math.PI * 3) * 0.12 * (1 - k), true);
            }
            double a = (t - LiftEnd) / (LandAt - LiftEnd);
            double e = a * a * (3 - 2 * a);
            return new CubeLoadFrame(e, rise * (1 - a) + 4 * peak * a * (1 - a), 0, true);
        }

        /// <summary>
        /// The turn that lines a cube up with a truck heading <paramref name="angle"/> (radians), the short way round:
        /// a cube looks the same every quarter turn (main.ts: turn − round(turn / (π/2))·π/2).
        /// </summary>
        public static double AlignTurn(double angle)
        {
            double turn = Math.Atan2(Math.Sin(angle), Math.Cos(angle));
            double quarter = JsMath.Round(turn / (Math.PI / 2)) * (Math.PI / 2);
            return turn - quarter;
        }
    }
}
