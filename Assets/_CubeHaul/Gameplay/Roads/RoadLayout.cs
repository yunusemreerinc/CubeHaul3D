using System;
using System.Collections.Generic;
using CubeHaul.Gameplay.Layout;

namespace CubeHaul.Gameplay.Roads
{
    /// <summary>The road's center line: a rectangle around the field (cube-hunt Ring), kept as its four edges.</summary>
    public readonly struct RoadRing
    {
        public double X0 { get; }
        public double Y0 { get; }
        public double X1 { get; }
        public double Y1 { get; }

        public RoadRing(double x0, double y0, double x1, double y1)
        {
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
        }

        public double Width => X1 - X0;
        public double Height => Y1 - Y0;
        public double Perimeter => 2 * (Width + Height);

        /// <summary>True if the point is on the ring's center line (within <paramref name="tolerance"/>).</summary>
        public bool IsOn(PointD p, double tolerance = 1e-6) =>
            (Math.Abs(p.Y - Y0) <= tolerance || Math.Abs(p.Y - Y1) <= tolerance) && p.X >= X0 - tolerance && p.X <= X1 + tolerance
            || (Math.Abs(p.X - X0) <= tolerance || Math.Abs(p.X - X1) <= tolerance) && p.Y >= Y0 - tolerance && p.Y <= Y1 + tolerance;

        public override string ToString() => FormattableString.Invariant($"ring [{X0:0.###}, {Y0:0.###} .. {X1:0.###}, {Y1:0.###}]");
    }

    /// <summary>main.ts's road and ramp geometry, design units (docs/ROADS.md).</summary>
    public static class RoadLayout
    {
        /// <summary>The road's center line runs this far outside the cube field on every side (ROAD_GAP).</summary>
        public const double RoadGap = 22;
        /// <summary>Road width (ROAD_W): two trucks side by side.</summary>
        public const double RoadWidth = 23;
        /// <summary>Where a truck starts and ends in the tunnel: 20% down its mouth (sendTruck's sy).</summary>
        public static readonly double TunnelStartY = SimConstants.TunnelY + SimConstants.TunnelHeight * 0.2;

        public static IReadOnlyList<double> LaneOffsets => SimConstants.RampLanes;
        public static int LaneCount => SimConstants.RampLanes.Count;

        /// <summary>
        /// main.ts startLevel's geometry for a picture: cell size, field origin and the ring, computed with the same
        /// operations in the same order as TypeScript, so every coordinate is the same double.
        /// </summary>
        public static (double cellSize, double fieldX, double fieldY, RoadRing ring) Geometry(int width, int height)
        {
            double cs = DesignLayout.CellSize(width, height);
            double fx = DesignLayout.Board.X + (DesignLayout.Board.Width - cs * width) / 2;
            double fy = DesignLayout.Board.Y + (DesignLayout.Board.Height - cs * height) / 2;
            return (cs, fx, fy, new RoadRing(fx - RoadGap, fy - RoadGap, fx + cs * width + RoadGap, fy + cs * height + RoadGap));
        }

        /// <summary>Where ramp lane k's driveway meets the road: on the ring's bottom side.</summary>
        public static PointD LaneEntry(RoadRing ring, int lane) => new PointD(SimConstants.TunnelX + SimConstants.RampLanes[lane], ring.Y1);

        /// <summary>Where a truck in ramp lane k comes up out of the tunnel.</summary>
        public static PointD TunnelStart(int lane) => new PointD(SimConstants.TunnelX + SimConstants.RampLanes[lane], TunnelStartY);
    }
}
