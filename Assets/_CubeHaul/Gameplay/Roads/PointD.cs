using System;

namespace CubeHaul.Gameplay.Roads
{
    /// <summary>A point in design units (cube-hunt's 420 x 900 screen: x right, y down). cube-hunt Pt.</summary>
    public readonly struct PointD : IEquatable<PointD>
    {
        public double X { get; }
        public double Y { get; }

        public PointD(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double DistanceTo(PointD o)
        {
            double dx = o.X - X, dy = o.Y - Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public bool Equals(PointD o) => X == o.X && Y == o.Y;
        public override bool Equals(object obj) => obj is PointD o && Equals(o);
        public override int GetHashCode() => X.GetHashCode() * 397 ^ Y.GetHashCode();
        public override string ToString() => FormattableString.Invariant($"({X:0.###}, {Y:0.###})");
    }
}
