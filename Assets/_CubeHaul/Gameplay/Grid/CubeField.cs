using System;
using System.Collections.Generic;
using CubeHaul.Domain;
using CubeHaul.Gameplay.Layout;
using CubeHaul.Levels;

namespace CubeHaul.Gameplay.Grid
{
    /// <summary>What the view shows for a cell.</summary>
    public enum CubeState : byte
    {
        /// <summary>No cube: an empty cell of the picture, or a cube already collected.</summary>
        Empty,
        /// <summary>A cube on the board.</summary>
        Present,
        /// <summary>A cube a truck is on its way for: still on the board and still shown (the timing contract).</summary>
        Claimed,
    }

    /// <summary>
    /// The cube field the 3D view draws, engine-free (docs/GRID_VIEW.md): one cube per non-empty cell of
    /// <c>LevelDefinition.picture.cells</c>, addressed by the same row-major cell index as <see cref="Board"/>, its color
    /// the palette entry and its place cube-hunt's cell center (<see cref="DesignLayout.CellCenter"/>). It follows the game
    /// only through <see cref="Sync"/>: a cell the board has emptied loses its cube, a claimed one keeps it. Nothing here
    /// (and nothing in the view) uses physics to decide anything.
    /// </summary>
    public sealed class CubeField
    {
        readonly int[] colors;
        readonly CubeState[] states;
        readonly RectD field;

        public CubeField(PictureDefinition picture)
        {
            if (picture == null) throw new ArgumentNullException(nameof(picture));
            if (picture.Width < 1 || picture.Height < 1 || picture.Cells == null || picture.Cells.Length != picture.Width * picture.Height)
                throw new ArgumentException("picture cells must be width x height", nameof(picture));
            Width = picture.Width;
            Height = picture.Height;
            colors = (int[])picture.Cells.Clone();
            states = new CubeState[colors.Length];
            Palette = new PaletteColor[picture.Palette.Length];
            for (int c = 0; c < Palette.Length; c++) Palette[c] = PaletteColor.Parse(picture.Palette[c]);
            for (int i = 0; i < colors.Length; i++)
            {
                if (colors[i] == Cells.Empty) continue;
                if (colors[i] < 0 || colors[i] >= Palette.Length) throw new ArgumentException($"cell {i} has color {colors[i]} outside the palette", nameof(picture));
                states[i] = CubeState.Present;
                PresentCount++;
            }
            field = DesignLayout.Field(Width, Height);
            CellSize = field.Width / Width;
        }

        public int Width { get; }
        public int Height { get; }
        public int CellCount => colors.Length;
        public PaletteColor[] Palette { get; }
        /// <summary>Side of a cell, design units.</summary>
        public double CellSize { get; }
        /// <summary>The cube field on the board, design units.</summary>
        public RectD Field => field;

        public int PresentCount { get; private set; }
        public int ClaimedCount { get; private set; }
        /// <summary>Cubes shown: present and claimed.</summary>
        public int VisibleCount => PresentCount + ClaimedCount;

        /// <summary>Palette index of the cell's cube, or -1 for a cell the picture leaves empty.</summary>
        public int ColorOf(int cell) => colors[cell];

        public CubeState StateOf(int cell) => states[cell];

        public bool IsVisible(int cell) => states[cell] != CubeState.Empty;

        public int Column(int cell) => cell % Width;
        public int Row(int cell) => cell / Width;

        /// <summary>The cell's center, design units (x right, y down; row 0 is the top, farthest from the ramp).</summary>
        public (double x, double y) DesignCenter(int cell) =>
            (field.X + (cell % Width + 0.5) * CellSize, field.Y + (cell / Width + 0.5) * CellSize);

        /// <summary>The cell under a design point, or -1 outside the field. The inverse of <see cref="DesignCenter"/>.</summary>
        public int CellAt(double designX, double designY)
        {
            double fx = (designX - field.X) / CellSize, fy = (designY - field.Y) / CellSize;
            if (fx < 0 || fy < 0) return -1;
            int x = (int)fx, y = (int)fy;
            return x < Width && y < Height ? y * Width + x : -1;
        }

        /// <summary>
        /// Brings the field in line with the board: cubes of emptied cells go, cubes of claimed cells stay (marked
        /// claimed). Appends every cell whose state changed to <paramref name="changed"/> (if given). Allocation-free.
        /// </summary>
        public int Sync(Board board, List<int> changed = null)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (board.CellCount != colors.Length) throw new ArgumentException("board and field have different sizes", nameof(board));
            int n = 0;
            for (int i = 0; i < states.Length; i++)
            {
                CubeState s = states[i];
                if (s == CubeState.Empty) continue; // a collected cube never comes back (I-05)
                CubeState now = board.Cell(i) == Cells.Empty ? CubeState.Empty : board.IsClaimed(i) ? CubeState.Claimed : CubeState.Present;
                if (now == s) continue;
                Set(i, now);
                changed?.Add(i);
                n++;
            }
            return n;
        }

        /// <summary>A truck claimed this cell's cube: it stays shown.</summary>
        public bool MarkClaimed(int cell)
        {
            if (states[cell] != CubeState.Present) return false;
            Set(cell, CubeState.Claimed);
            return true;
        }

        /// <summary>This cell's cube was collected: it is no longer shown.</summary>
        public bool MarkCollected(int cell)
        {
            if (states[cell] == CubeState.Empty) return false;
            Set(cell, CubeState.Empty);
            return true;
        }

        void Set(int cell, CubeState now)
        {
            CubeState was = states[cell];
            if (was == CubeState.Present) PresentCount--;
            else if (was == CubeState.Claimed) ClaimedCount--;
            if (now == CubeState.Present) PresentCount++;
            else if (now == CubeState.Claimed) ClaimedCount++;
            states[cell] = now;
        }
    }
}
