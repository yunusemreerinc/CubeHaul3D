using System;

namespace CubeHaul.Gameplay.Layout
{
    /// <summary>
    /// The screen composition of cube-hunt (src/main.ts @ 559dfd9) in its design units: a 420 x 900 portrait screen,
    /// origin top-left, y down. Top bar, board, ramp, slots, tray, bottom bar, top to bottom. The 3D world lays the middle
    /// part (board to tray) on the ground through <c>WorldMapper</c>; the bars become UI (docs/SCENE_LAYOUT.md).
    /// </summary>
    public static class DesignLayout
    {
        public const double Width = SimConstants.ScreenWidth;   // 420
        public const double Height = SimConstants.ScreenHeight; // 900

        /// <summary>Top bar: restart, previous, title and difficulty, next, speed. y 0..64.</summary>
        public const double HeaderHeight = 64;
        /// <summary>Bottom bar: the band main.ts paints from BAR_Y - 10 = 826 to the bottom. y 826..900.</summary>
        public const double FooterHeight = Height - (BarY - 10);
        public const double BarY = 836;

        public static readonly RectD Board = new RectD(SimConstants.BoardX, SimConstants.BoardY, SimConstants.BoardWidth, SimConstants.BoardHeight);
        public static readonly RectD Tunnel = new RectD(SimConstants.TunnelX - SimConstants.TunnelWidth / 2, SimConstants.TunnelY - SimConstants.TunnelHeight / 2,
            SimConstants.TunnelWidth, SimConstants.TunnelHeight);

        public const double SlotY = 540;
        public const double SlotHeight = 66;
        public const double SlotGap = 10;
        public const double SlotMaxWidth = 68;

        public const double TrayY = 636;
        /// <summary>Tray rows drawn (the fourth fades out).</summary>
        public const int TrayRows = 3;
        public const double BlockWidth = 68, BlockHeight = 52, BlockGapX = 10, BlockGapY = 8;

        /// <summary>The bottom of the tray's third row: 636 + 3 * 52 + 2 * 8 = 808.</summary>
        public const double TrayBottom = TrayY + TrayRows * BlockHeight + (TrayRows - 1) * BlockGapY;

        /// <summary>
        /// What the 3D world must show: the full design width (it has its own 16-unit side margins), from the top of
        /// the board to the bottom of the tray, plus a little air above and below.
        /// </summary>
        public const double WorldPadding = 6;
        public static readonly RectD WorldContent = RectD.FromMinMax(0, SimConstants.BoardY - WorldPadding, Width, TrayBottom + WorldPadding);

        /// <summary>main.ts slotRect: n slots centered, 10 apart, at most 68 wide.</summary>
        public static RectD SlotRect(int i, int n)
        {
            if (n < 1) throw new ArgumentOutOfRangeException(nameof(n));
            double w = Math.Min(SlotMaxWidth, (Width - 32 - SlotGap * (n - 1)) / n);
            double x0 = (Width - (n * w + (n - 1) * SlotGap)) / 2;
            return new RectD(x0 + i * (w + SlotGap), SlotY, w, SlotHeight);
        }

        /// <summary>main.ts trayLeft: left edge of a tray of n columns, centered.</summary>
        public static double TrayLeft(int n) => (Width - (n * BlockWidth + (n - 1) * BlockGapX)) / 2;

        /// <summary>A crate's box at this place in a tray of <paramref name="columns"/> columns (main.ts blockRect, without the glide).</summary>
        public static RectD BlockRect(int place, int row, int columns) =>
            new RectD(TrayLeft(columns) + place * (BlockWidth + BlockGapX), TrayY + row * (BlockHeight + BlockGapY), BlockWidth, BlockHeight);

        /// <summary>main.ts cellSize: the cube field fills the board minus FieldInset on every side, cells square.</summary>
        public static double CellSize(int pictureWidth, int pictureHeight) =>
            Math.Min((Board.Width - 2 * SimConstants.FieldInset) / pictureWidth, (Board.Height - 2 * SimConstants.FieldInset) / pictureHeight);

        /// <summary>The cube field, centered on the board.</summary>
        public static RectD Field(int pictureWidth, int pictureHeight)
        {
            double cs = CellSize(pictureWidth, pictureHeight);
            return new RectD(Board.X + (Board.Width - cs * pictureWidth) / 2, Board.Y + (Board.Height - cs * pictureHeight) / 2, cs * pictureWidth, cs * pictureHeight);
        }

        /// <summary>main.ts cellCenter.</summary>
        public static (double x, double y) CellCenter(int cell, int pictureWidth, int pictureHeight)
        {
            RectD f = Field(pictureWidth, pictureHeight);
            double cs = f.Width / pictureWidth;
            return (f.X + (cell % pictureWidth + 0.5) * cs, f.Y + (cell / pictureWidth + 0.5) * cs);
        }
    }
}
