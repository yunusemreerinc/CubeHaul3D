using System;
using CubeHaul.Gameplay.Layout;

namespace CubeHaul.Gameplay.Roads
{
    /// <summary>
    /// Cells, design points and the 3D world, in one place (docs/ROADS.md): the board lies on the world's XZ plane,
    /// x = designX · u, z = −designY · u, height y = h · u (u = world units per design unit; WorldMapper's
    /// convention: the ramp side, large design y, is nearest the camera, at negative z). Engine-free; Presentation turns
    /// the triples into Vector3.
    /// </summary>
    public sealed class BoardCoordinateMapper
    {
        public BoardCoordinateMapper(int width, int height, double unitsPerDesign = 0.025)
        {
            if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width));
            if (!(unitsPerDesign > 0)) throw new ArgumentOutOfRangeException(nameof(unitsPerDesign));
            Width = width;
            Height = height;
            UnitsPerDesign = unitsPerDesign;
            var (cs, fx, fy, ring) = RoadLayout.Geometry(width, height);
            CellSize = cs;
            FieldX = fx;
            FieldY = fy;
            Ring = ring;
            Field = new RectD(fx, fy, cs * width, cs * height);
        }

        public int Width { get; }
        public int Height { get; }
        public double UnitsPerDesign { get; }
        public RectD Field { get; }
        public double FieldX { get; }
        public double FieldY { get; }
        public double CellSize { get; }
        public RoadRing Ring { get; }

        public PointD CellCenter(int cell)
        {
            if (cell < 0 || cell >= Width * Height) throw new ArgumentOutOfRangeException(nameof(cell));
            return new PointD(FieldX + (cell % Width + 0.5) * CellSize, FieldY + (cell / Width + 0.5) * CellSize);
        }

        /// <summary>The cell under a design point, or -1 off the field (on the road, the ramp, …).</summary>
        public int CellAt(PointD p)
        {
            double fx = (p.X - FieldX) / CellSize, fy = (p.Y - FieldY) / CellSize;
            if (fx < 0 || fy < 0) return -1;
            int x = (int)Math.Floor(fx), y = (int)Math.Floor(fy);
            return x < Width && y < Height ? y * Width + x : -1;
        }

        public (double x, double y, double z) ToWorld(PointD p, double height = 0) => (p.X * UnitsPerDesign, height * UnitsPerDesign, -p.Y * UnitsPerDesign);

        public PointD FromWorld(double x, double z) => new PointD(x / UnitsPerDesign, -z / UnitsPerDesign);

        public (double x, double y, double z) CellToWorld(int cell, double height = 0) => ToWorld(CellCenter(cell), height);

        /// <summary>The cell under a world point (its height ignored), or -1.</summary>
        public int WorldToCell(double x, double z) => CellAt(FromWorld(x, z));
    }
}
