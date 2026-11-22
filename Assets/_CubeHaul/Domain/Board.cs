using System;
using System.Collections.Generic;

namespace CubeHaul.Domain
{
    /// <summary>
    /// The picture being cleared: a port of cube-hunt src/core/board.ts @ 559dfd9, rule for rule.
    ///
    /// Trucks drive in from the open board edges and only through cleared space, so a cell is reachable when it
    /// touches an open edge or an open empty cell: one connected to an open edge through other empty cells. Sealed
    /// transparent cells do not count until a truck can get to them. Cubes are taken row by row from the ramp side,
    /// each row from the middle out: the reachable cube in the lowest row, then the shortest drive from the ramp, then
    /// the lowest index (<see cref="PickTarget"/>).
    ///
    /// Differences from the TypeScript, none of which changes a result (docs/PORTING_PLAN.md §3.2, I-06, I-07):
    /// - The per-color reachable sets are arrays with a position index (add, remove and lookup in constant time,
    ///   iteration without allocating) instead of JavaScript Sets. Nothing depends on their order: PickTarget takes the
    ///   smallest (row, drive, index), and <see cref="ReachableCells"/> returns the cells in ascending order.
    /// - Flood fill, the distance update and <see cref="OpensUp"/> reuse buffers allocated once, and OpensUp starts from
    ///   a list of the claimed cells instead of scanning the whole board. After construction the hot paths
    ///   (PickTarget, Claim, Eat, OpensUp, ReachableCount) allocate nothing.
    /// Road distances are stored as float, like the TypeScript Float32Array, and compared as double.
    ///
    /// Not thread-safe (shared scratch buffers); use one Board per thread.
    /// </summary>
    public sealed class Board
    {
        const int Unreached = 1 << 30;

        readonly int width;
        readonly int height;
        readonly int colorCount;
        readonly OpenEdges edges;
        readonly int[] cells;
        /// <summary>1 for empty cells connected to an open edge (only ever goes 0 -> 1).</summary>
        readonly byte[] open;
        /// <summary>1 for cubes a truck has been sent for but has not picked up yet.</summary>
        readonly byte[] claimed;
        /// <summary>For open cells, along their best way in from the ramp: cells driven inside the field (0 = on the edge)…</summary>
        readonly int[] depth;
        /// <summary>…and the road distance (in cells) before that.</summary>
        readonly float[] road;

        // Reachable cubes per color: reachItems[c][0..reachCount[c]); reachPos[cell] = its index in its color's list, or -1.
        readonly int[][] reachItems;
        readonly int[] reachCount;
        readonly int[] reachPos;
        readonly int[] remainingByColor;

        // Claimed cells, for OpensUp: claimedItems[0..pending); claimedPos[cell] = index or -1.
        readonly int[] claimedItems;
        readonly int[] claimedPos;

        int remaining;
        int pending;

        // Scratch, reused by Flood / UpdateDepths / OpensUp.
        readonly int[] stack;
        readonly int[] opened;
        readonly List<int> queue;
        readonly int[] seen;
        int seenStamp;
        int stackSize;
        int openedSize;

        /// <param name="width">Board width in cells.</param>
        /// <param name="height">Board height in cells.</param>
        /// <param name="cells">Row-major, width * height values: <see cref="Cells.Empty"/> or a color 0..colorCount-1. Copied.</param>
        /// <param name="colorCount">Palette size.</param>
        /// <param name="edges">Which edges trucks drive in from.</param>
        public Board(int width, int height, int[] cells, int colorCount, OpenEdges edges = OpenEdges.All)
        {
            if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width), "the board needs at least one cell");
            if (cells == null) throw new ArgumentNullException(nameof(cells));
            if (cells.Length != width * height) throw new ArgumentException($"{cells.Length} cells for a {width}x{height} board", nameof(cells));
            if (colorCount < 0) throw new ArgumentOutOfRangeException(nameof(colorCount));
            if (!Enum.IsDefined(typeof(OpenEdges), edges)) throw new ArgumentOutOfRangeException(nameof(edges));

            int n = cells.Length;
            this.width = width;
            this.height = height;
            this.colorCount = colorCount;
            this.edges = edges;
            this.cells = (int[])cells.Clone();
            open = new byte[n];
            claimed = new byte[n];
            depth = new int[n];
            road = new float[n];
            for (int i = 0; i < n; i++)
            {
                depth[i] = Unreached;
                road[i] = float.PositiveInfinity;
            }

            remainingByColor = new int[colorCount];
            for (int i = 0; i < n; i++)
            {
                int c = this.cells[i];
                if (c == Cells.Empty) continue;
                if (c < 0 || c >= colorCount) throw new ArgumentException($"cell {i} has color {c}, outside 0..{colorCount - 1}", nameof(cells));
                remainingByColor[c]++;
            }
            reachItems = new int[colorCount][];
            for (int c = 0; c < colorCount; c++) reachItems[c] = new int[remainingByColor[c]];
            reachCount = new int[colorCount];
            reachPos = new int[n];
            claimedItems = new int[n];
            claimedPos = new int[n];
            for (int i = 0; i < n; i++)
            {
                reachPos[i] = -1;
                claimedPos[i] = -1;
            }

            stack = new int[n];
            opened = new int[n];
            queue = new List<int>(4 * n);
            seen = new int[n];

            for (int i = 0; i < n; i++)
            {
                if (this.cells[i] == Cells.Empty && open[i] == 0 && OpenSides(i) != SideMask.None) Flood(i);
            }
            for (int i = 0; i < n; i++)
            {
                int c = this.cells[i];
                if (c == Cells.Empty) continue;
                remaining++;
                if (IsExposed(i)) AddReachable(c, i);
            }
        }

        public int Width => width;
        public int Height => height;
        public int CellCount => cells.Length;
        public int ColorCount => colorCount;
        public OpenEdges Edges => edges;
        /// <summary>Cubes still on the board, including claimed ones not yet picked up.</summary>
        public int Remaining => remaining;
        /// <summary>How many cubes are claimed but not yet picked up (trucks on their way).</summary>
        public int Pending => pending;

        /// <summary>The cell's color, or <see cref="Cells.Empty"/>.</summary>
        public int Cell(int i) => cells[i];

        /// <summary>Copies the cells into <paramref name="destination"/> (length >= CellCount).</summary>
        public void CopyCells(int[] destination) => Array.Copy(cells, destination, cells.Length);

        /// <summary>The open board edges this cell lies on (None for interior cells).</summary>
        public SideMask OpenSides(int i)
        {
            int x = i % width;
            int y = i / width;
            SideMask sides = SideMask.None;
            if (y == height - 1) sides |= SideMask.Bottom;
            if (edges != OpenEdges.Bottom)
            {
                if (x == 0) sides |= SideMask.Left;
                if (x == width - 1) sides |= SideMask.Right;
            }
            if (edges == OpenEdges.All && y == 0) sides |= SideMask.Top;
            return sides;
        }

        /// <summary>
        /// Writes the orthogonal neighbors inside the board into <paramref name="output"/> (length >= 4) in cube-hunt's
        /// order: left, right, up, down. Returns how many.
        /// </summary>
        public int Neighbors(int i, int[] output)
        {
            int x = i % width;
            int y = i / width;
            int k = 0;
            if (x > 0) output[k++] = i - 1;
            if (x < width - 1) output[k++] = i + 1;
            if (y > 0) output[k++] = i - width;
            if (y < height - 1) output[k++] = i + width;
            return k;
        }

        /// <summary>Is this an empty cell that trucks can drive through?</summary>
        public bool IsOpen(int i) => open[i] == 1;

        public bool IsClaimed(int i) => claimed[i] == 1;

        /// <summary>Is this cube reachable now (and not claimed)?</summary>
        public bool IsReachable(int i) => reachPos[i] >= 0;

        public int ReachableCount(int color) => color >= 0 && color < colorCount ? reachCount[color] : 0;

        public int RemainingOf(int color) => color >= 0 && color < colorCount ? remainingByColor[color] : 0;

        /// <summary>The reachable cells of one color, in ascending order (a new array; not for hot paths).</summary>
        public int[] ReachableCells(int color)
        {
            if (color < 0 || color >= colorCount) return new int[0];
            var copy = new int[reachCount[color]];
            Array.Copy(reachItems[color], copy, copy.Length);
            Array.Sort(copy);
            return copy;
        }

        /// <summary>
        /// Will a cube of <paramref name="color"/> become reachable once the trucks already sent pick up their cubes?
        /// Each claimed cube, once collected, opens its own cell and any sealed empty space behind it, exposing the
        /// cubes around them.
        /// </summary>
        public bool OpensUp(int color)
        {
            if (pending == 0) return false;
            NextStamp();
            stackSize = 0;
            for (int k = 0; k < pending; k++)
            {
                int c = claimedItems[k];
                seen[c] = seenStamp;
                stack[stackSize++] = c;
            }
            while (stackSize > 0)
            {
                int j = stack[--stackSize];
                int x = j % width;
                int y = j / width;
                if ((x > 0 && OpensUpVisit(j - 1, color)) || (x < width - 1 && OpensUpVisit(j + 1, color)) ||
                    (y > 0 && OpensUpVisit(j - width, color)) || (y < height - 1 && OpensUpVisit(j + width, color)))
                    return true;
            }
            return false;
        }

        bool OpensUpVisit(int n, int color)
        {
            if (seen[n] == seenStamp) return false;
            int c = cells[n];
            if (c == Cells.Empty)
            {
                // Already-open space exposes nothing new; a sealed gap opens up too.
                if (open[n] == 0)
                {
                    seen[n] = seenStamp;
                    stack[stackSize++] = n;
                }
                return false;
            }
            return c == color && claimed[n] == 0;
        }

        /// <summary>
        /// The next cube of <paramref name="color"/> for a truck, or -1 if none can go now: the one in the lowest row
        /// (nearest the ramp side), then the shortest drive from the ramp (so a row goes from the middle out to the
        /// sides), then the lowest index.
        /// </summary>
        public int PickTarget(int color)
        {
            if (color < 0 || color >= colorCount) throw new ArgumentOutOfRangeException(nameof(color));
            int best = -1;
            double bestRow = double.PositiveInfinity;
            double bestDrive = double.PositiveInfinity;
            int[] items = reachItems[color];
            int count = reachCount[color];
            for (int k = 0; k < count; k++)
            {
                int i = items[k];
                int row = height - 1 - i / width; // 0 = bottom row
                if (row > bestRow) continue;
                WayIn(i, out double d, out double r);
                double drive = d + r;
                if (row < bestRow || drive < bestDrive || (drive == bestDrive && i < best))
                {
                    bestRow = row;
                    bestDrive = drive;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>
        /// A truck has been sent for this cube: no other truck may take it, but it stays on the board, still blocking
        /// the cubes behind it, until <see cref="Eat"/> collects it.
        /// </summary>
        public void Claim(int i)
        {
            int c = cells[i];
            if (c == Cells.Empty || reachPos[i] < 0) throw new InvalidOperationException($"cell {i} is not reachable");
            RemoveReachable(c, i);
            claimed[i] = 1;
            claimedPos[i] = pending;
            claimedItems[pending] = i;
            pending++;
        }

        /// <summary>Removes a cube (claimed, or reachable and taken directly), opening the space behind it.</summary>
        public void Eat(int i)
        {
            int c = cells[i];
            if (c == Cells.Empty) throw new InvalidOperationException($"cell {i} is already empty");
            if (claimed[i] == 1)
            {
                claimed[i] = 0;
                int p = claimedPos[i];
                int last = claimedItems[--pending];
                claimedItems[p] = last;
                claimedPos[last] = p;
                claimedPos[i] = -1;
            }
            else if (reachPos[i] < 0)
            {
                throw new InvalidOperationException($"cell {i} is not reachable");
            }
            cells[i] = Cells.Empty;
            RemoveReachable(c, i);
            remaining--;
            remainingByColor[c]--;
            // It was reachable, so it connects to an open edge; so does any sealed gap it touches.
            Flood(i);
        }

        // ---- internals, for the tests (I-09) ----

        internal int DepthAt(int i) => depth[i];
        internal float RoadAt(int i) => road[i];

        // ---- reachability bookkeeping ----

        bool IsExposed(int i)
        {
            if (OpenSides(i) != SideMask.None) return true;
            int x = i % width;
            int y = i / width;
            return (x > 0 && open[i - 1] == 1) || (x < width - 1 && open[i + 1] == 1) ||
                   (y > 0 && open[i - width] == 1) || (y < height - 1 && open[i + width] == 1);
        }

        void AddReachable(int color, int i)
        {
            if (reachPos[i] >= 0) return;
            reachPos[i] = reachCount[color];
            reachItems[color][reachCount[color]++] = i;
        }

        void RemoveReachable(int color, int i)
        {
            int p = reachPos[i];
            if (p < 0) return;
            int last = reachItems[color][--reachCount[color]];
            reachItems[color][p] = last;
            reachPos[last] = p;
            reachPos[i] = -1;
        }

        /// <summary>Marks empty cell <paramref name="start"/> and every empty cell connected to it as open, exposing their neighbors.</summary>
        void Flood(int start)
        {
            stackSize = 0;
            openedSize = 0;
            stack[stackSize++] = start;
            opened[openedSize++] = start;
            open[start] = 1;
            while (stackSize > 0)
            {
                int j = stack[--stackSize];
                int x = j % width;
                int y = j / width;
                if (x > 0) FloodVisit(j - 1);
                if (x < width - 1) FloodVisit(j + 1);
                if (y > 0) FloodVisit(j - width);
                if (y < height - 1) FloodVisit(j + width);
            }
            UpdateDepths();
        }

        void FloodVisit(int n)
        {
            int c = cells[n];
            if (c != Cells.Empty)
            {
                // A claimed cube already has a truck coming for it; do not offer it again.
                if (claimed[n] == 0) AddReachable(c, n);
            }
            else if (open[n] == 0)
            {
                open[n] = 1;
                stack[stackSize++] = n;
                opened[openedSize++] = n;
            }
        }

        /// <summary>Road distance (in cells) from the ramp, below the middle of the bottom edge, to a cell's side.</summary>
        double RoadTo(int i, Side side)
        {
            double x = i % width + 0.5;
            double y = i / width + 0.5;
            double mid = width / 2.0;
            switch (side)
            {
                case Side.Bottom: return Math.Abs(x - mid);
                case Side.Left:
                case Side.Right: return mid + (height - y);
                default: return mid + height + Math.Min(x, width - x);
            }
        }

        /// <summary>Is a way in of d1 cells inside after r1 of road better than (d2, r2)? Fewer cells in wins; on a tie, the shorter road.</summary>
        static bool Better(double d1, double r1, double d2, double r2) => d1 < d2 || (d1 == d2 && r1 < r2);

        /// <summary>
        /// Updates the shortest drive to the cells just opened, and to any open cells that now have a shorter one through
        /// them. Opening cells only ever shortens drives, so this only touches the area around the change.
        /// </summary>
        void UpdateDepths()
        {
            queue.Clear();
            for (int k = 0; k < openedSize; k++)
            {
                int i = opened[k];
                SideMask sides = OpenSides(i);
                if ((sides & SideMask.Bottom) != 0) TrySide(i, Side.Bottom);
                if ((sides & SideMask.Left) != 0) TrySide(i, Side.Left);
                if ((sides & SideMask.Right) != 0) TrySide(i, Side.Right);
                if ((sides & SideMask.Top) != 0) TrySide(i, Side.Top);
                int x = i % width;
                int y = i / width;
                if (x > 0) TryNeighbor(i, i - 1);
                if (x < width - 1) TryNeighbor(i, i + 1);
                if (y > 0) TryNeighbor(i, i - width);
                if (y < height - 1) TryNeighbor(i, i + width);
                queue.Add(i);
            }
            for (int q = 0; q < queue.Count; q++)
            {
                int u = queue[q];
                int x = u % width;
                int y = u / width;
                if (x > 0) Relax(u, u - 1);
                if (x < width - 1) Relax(u, u + 1);
                if (y > 0) Relax(u, u - width);
                if (y < height - 1) Relax(u, u + width);
            }
        }

        void TrySide(int i, Side side)
        {
            double r = RoadTo(i, side);
            if (Better(0, r, depth[i], road[i]))
            {
                depth[i] = 0;
                road[i] = (float)r;
            }
        }

        void TryNeighbor(int i, int n)
        {
            if (open[n] == 1 && Better(depth[n] + 1, road[n], depth[i], road[i]))
            {
                depth[i] = depth[n] + 1;
                road[i] = road[n];
            }
        }

        void Relax(int u, int n)
        {
            if (open[n] != 1) return;
            if (Better(depth[u] + 1, road[u], depth[n], road[n]))
            {
                depth[n] = depth[u] + 1;
                road[n] = road[u];
                queue.Add(n);
            }
        }

        /// <summary>
        /// How a truck gets to cube i: cells in from the road (d) and road distance from the ramp (r), either straight in
        /// from an open edge or through its best cleared neighbor; chosen by fewest cells in, then the shorter road (I-08).
        /// </summary>
        void WayIn(int i, out double d, out double r)
        {
            int w = width;
            int h = height;
            double mid = w / 2.0;
            int x = i % w;
            int y = i / w;
            d = double.PositiveInfinity;
            r = double.PositiveInfinity;
            if (y == h - 1)
            {
                d = 0;
                r = Math.Abs(x + 0.5 - mid);
            }
            if (edges != OpenEdges.Bottom && (x == 0 || x == w - 1))
            {
                double rs = mid + (h - y - 0.5);
                if (Better(0, rs, d, r))
                {
                    d = 0;
                    r = rs;
                }
            }
            if (edges == OpenEdges.All && y == 0)
            {
                double rs = mid + h + Math.Min(x + 0.5, w - x - 0.5);
                if (Better(0, rs, d, r))
                {
                    d = 0;
                    r = rs;
                }
            }
            if (x > 0 && open[i - 1] == 1 && Better(depth[i - 1] + 1, road[i - 1], d, r))
            {
                d = depth[i - 1] + 1;
                r = road[i - 1];
            }
            if (x < w - 1 && open[i + 1] == 1 && Better(depth[i + 1] + 1, road[i + 1], d, r))
            {
                d = depth[i + 1] + 1;
                r = road[i + 1];
            }
            if (y > 0 && open[i - w] == 1 && Better(depth[i - w] + 1, road[i - w], d, r))
            {
                d = depth[i - w] + 1;
                r = road[i - w];
            }
            if (y < h - 1 && open[i + w] == 1 && Better(depth[i + w] + 1, road[i + w], d, r))
            {
                d = depth[i + w] + 1;
                r = road[i + w];
            }
        }

        void NextStamp()
        {
            if (seenStamp == int.MaxValue)
            {
                Array.Clear(seen, 0, seen.Length);
                seenStamp = 0;
            }
            seenStamp++;
        }
    }
}
