using System;

namespace CubeHaul.Gameplay.Layout
{
    /// <summary>
    /// An axis-aligned rectangle in doubles, engine-free. Screen rectangles use Unity's convention (origin bottom-left,
    /// y up, pixels); design rectangles use cube-hunt's (origin top-left, y down, design units).
    /// </summary>
    public readonly struct RectD : IEquatable<RectD>
    {
        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }

        public RectD(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public static RectD FromMinMax(double xMin, double yMin, double xMax, double yMax) => new RectD(xMin, yMin, xMax - xMin, yMax - yMin);

        public double XMin => X;
        public double YMin => Y;
        public double XMax => X + Width;
        public double YMax => Y + Height;
        public double CenterX => X + Width / 2;
        public double CenterY => Y + Height / 2;

        /// <summary>True if <paramref name="other"/> lies inside this one, allowing <paramref name="tolerance"/> on every side.</summary>
        public bool Contains(RectD other, double tolerance = 1e-6) =>
            other.XMin >= XMin - tolerance && other.YMin >= YMin - tolerance && other.XMax <= XMax + tolerance && other.YMax <= YMax + tolerance;

        public bool Overlaps(RectD other, double tolerance = 1e-6) =>
            other.XMin < XMax - tolerance && other.XMax > XMin + tolerance && other.YMin < YMax - tolerance && other.YMax > YMin + tolerance;

        public RectD Inset(double left, double bottom, double right, double top) => FromMinMax(XMin + left, YMin + bottom, XMax - right, YMax - top);

        public bool Equals(RectD o) => X == o.X && Y == o.Y && Width == o.Width && Height == o.Height;
        public override bool Equals(object obj) => obj is RectD o && Equals(o);
        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() * 397) ^ (Width.GetHashCode() * 7919) ^ (Height.GetHashCode() * 31);
        public override string ToString() => FormattableString.Invariant($"[{XMin:0.##}, {YMin:0.##} .. {XMax:0.##}, {YMax:0.##}]");
    }
}
