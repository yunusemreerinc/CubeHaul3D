using System;

namespace CubeHaul.Domain
{
    /// <summary>A side of the board, as seen from a cell on it (cube-hunt core/board.ts Side).</summary>
    public enum Side
    {
        Top,
        Right,
        Bottom,
        Left,
    }

    /// <summary>The open board edges a cell lies on. Listed in cube-hunt's order: bottom, left, right, top.</summary>
    [Flags]
    public enum SideMask
    {
        None = 0,
        Bottom = 1,
        Left = 2,
        Right = 4,
        Top = 8,
    }
}
