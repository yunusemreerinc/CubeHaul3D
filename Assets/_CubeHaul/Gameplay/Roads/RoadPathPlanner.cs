using System;
using System.Collections.Generic;
using CubeHaul.Domain;

namespace CubeHaul.Gameplay.Roads
{
    /// <summary>
    /// Truck routes (Task 13, docs/ROADS.md): a port of cube-hunt's RoadMap (src/view/roads.ts @ 559dfd9). From where a
    /// ramp lane meets the road, along the road that circles the field (the short way round), in through an open edge of
    /// the board, then through cleared, open cells only, to park next to the target cube: on the cell beside it, or for a
    /// cube on an open edge, just outside the field facing it. Never through a closed edge, never across a cube still on
    /// the board (a claimed one included). Works in design units; <see cref="BoardCoordinateMapper"/> takes points to
    /// the 3D world.
    ///
    /// Same numbers as TypeScript: the breadth-first search visits neighbours and open sides in RoadMap's order and keeps
    /// the first of equal costs, so ties resolve identically. Buffers are reused: routing allocates nothing but the
    /// caller's output list growing.
    /// </summary>
    public sealed class RoadPathPlanner
    {
        static readonly Side[] SideOrder = { Side.Bottom, Side.Left, Side.Right, Side.Top }; // Board.openSides order

        readonly Board board;
        readonly int[] dist, toward, queue;
        readonly int[] neighbors = new int[4];
        readonly (PointD p, double t)[] corners = new (PointD, double)[4];
        readonly List<(PointD p, double d, int k)> passed = new List<(PointD, double, int)>(4);

        /// <param name="fieldX">Left of the cell grid, design units.</param>
        /// <param name="fieldY">Top of the cell grid, design units.</param>
        /// <param name="cellSize">Side of a cell, design units.</param>
        /// <param name="ring">The road's center line: a rectangle around the field.</param>
        /// <param name="entry">Where the driveway from the ramp meets the road (default for <see cref="Route"/>).</param>
        public RoadPathPlanner(Board board, double fieldX, double fieldY, double cellSize, RoadRing ring, PointD entry)
        {
            this.board = board ?? throw new ArgumentNullException(nameof(board));
            FieldX = fieldX;
            FieldY = fieldY;
            CellSize = cellSize;
            Ring = ring;
            Entry = entry;
            dist = new int[board.CellCount];
            toward = new int[board.CellCount];
            queue = new int[board.CellCount];
            corners[0] = (new PointD(ring.X0, ring.Y0), 0);
            corners[1] = (new PointD(ring.X1, ring.Y0), ring.Width);
            corners[2] = (new PointD(ring.X1, ring.Y1), ring.Width + ring.Height);
            corners[3] = (new PointD(ring.X0, ring.Y1), 2 * ring.Width + ring.Height);
        }

        /// <summary>The planner with main.ts's geometry for this board (RoadLayout).</summary>
        public static RoadPathPlanner ForBoard(Board board)
        {
            var (cs, fx, fy, ring) = RoadLayout.Geometry(board.Width, board.Height);
            return new RoadPathPlanner(board, fx, fy, cs, ring, RoadLayout.LaneEntry(ring, 1));
        }

        public Board Board => board;
        public double FieldX { get; }
        public double FieldY { get; }
        public double CellSize { get; }
        public RoadRing Ring { get; }
        public PointD Entry { get; }

        /// <summary>After a route: the cell where the truck left the road, and through which open side.</summary>
        public int LastWayInCell { get; private set; } = -1;
        public Side LastWayInSide { get; private set; }

        public PointD Center(int i) =>
            new PointD(FieldX + (i % board.Width + 0.5) * CellSize, FieldY + (i / board.Width + 0.5) * CellSize);

        /// <summary>Where a truck parks to load a cube on an open edge: one cell outside it, touching it.</summary>
        public PointD Beside(int i, Side side)
        {
            PointD c = Center(i);
            switch (side)
            {
                case Side.Top: return new PointD(c.X, c.Y - CellSize);
                case Side.Bottom: return new PointD(c.X, c.Y + CellSize);
                case Side.Left: return new PointD(c.X - CellSize, c.Y);
                default: return new PointD(c.X + CellSize, c.Y);
            }
        }

        /// <summary>The point on the road straight out from a cell on the given board side.</summary>
        public PointD RoadPoint(int i, Side side)
        {
            PointD c = Center(i);
            switch (side)
            {
                case Side.Top: return new PointD(c.X, Ring.Y0);
                case Side.Bottom: return new PointD(c.X, Ring.Y1);
                case Side.Left: return new PointD(Ring.X0, c.Y);
                default: return new PointD(Ring.X1, c.Y);
            }
        }

        double Perimeter => Ring.Perimeter;

        /// <summary>Distance along the road, clockwise from the top-left corner, of a point on it (RoadMap.along).</summary>
        double Along(PointD p)
        {
            const double eps = 0.5;
            RoadRing r = Ring;
            if (Math.Abs(p.Y - r.Y0) < eps) return p.X - r.X0;
            if (Math.Abs(p.X - r.X1) < eps) return r.Width + (p.Y - r.Y0);
            if (Math.Abs(p.Y - r.Y1) < eps) return r.Width + r.Height + (r.X1 - p.X);
            return 2 * r.Width + r.Height + (r.Y1 - p.Y);
        }

        /// <summary>Shortest distance along the road between two points on it.</summary>
        public double RoadDistance(PointD a, PointD b)
        {
            double d = Math.Abs(Along(a) - Along(b));
            return Math.Min(d, Perimeter - d);
        }

        /// <summary>Waypoints driving the short way round from a to b: the corners passed, then b (a excluded).</summary>
        public void RoadPath(PointD a, PointD b, List<PointD> output)
        {
            double P = Perimeter;
            double ta = Along(a);
            double forward = (Along(b) - ta + P) % P;
            bool clockwise = forward <= P - forward;
            double limit = clockwise ? forward : P - forward;
            passed.Clear();
            for (int k = 0; k < 4; k++)
            {
                double d = clockwise ? (corners[k].t - ta + P) % P : (ta - corners[k].t + P) % P;
                if (d > 0.5 && d < limit - 0.5) passed.Add((corners[k].p, d, k));
            }
            // Stable sort by distance (JavaScript's sort is stable): ties keep corner order.
            for (int i = 1; i < passed.Count; i++)
            {
                var x = passed[i];
                int j = i - 1;
                while (j >= 0 && passed[j].d > x.d)
                {
                    passed[j + 1] = passed[j];
                    j--;
                }
                passed[j + 1] = x;
            }
            foreach (var c in passed) output.Add(c.p);
            output.Add(b);
        }

        /// <summary>Route from the default entry to where a truck parks to load cell t (RoadMap.route).</summary>
        public List<PointD> Route(int target)
        {
            var path = new List<PointD>();
            RouteFrom(target, Entry, path);
            return path;
        }

        /// <summary>
        /// RoadMap.routeFrom: the route from <paramref name="entry"/> (a point on the road) to where a truck parks to load
        /// <paramref name="target"/>, written to <paramref name="path"/> (cleared first; the entry is its first point,
        /// the parking point its last). Returns the drive's cost: its length plus one cell (the last step to the cube).
        /// </summary>
        public double RouteFrom(int target, PointD entry, List<PointD> path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (target < 0 || target >= board.CellCount) throw new ArgumentOutOfRangeException(nameof(target));
            path.Clear();
            for (int i = 0; i < dist.Length; i++)
            {
                dist[i] = -1;
                toward[i] = -1;
            }
            dist[target] = 0;
            int head = 0, tail = 0;
            queue[tail++] = target;
            int bestCell = -1;
            Side bestSide = Side.Bottom;
            PointD bestRoad = default;
            double bestCost = 0;
            while (head < tail)
            {
                int u = queue[head++];
                PointD c = Center(u);
                SideMask open = board.OpenSides(u);
                foreach (Side side in SideOrder)
                {
                    if ((open & Mask(side)) == 0) continue;
                    PointD road = RoadPoint(u, side);
                    double cost = dist[u] * CellSize + Hypot(road.X - c.X, road.Y - c.Y) + RoadDistance(entry, road);
                    if (bestCell < 0 || cost < bestCost)
                    {
                        bestCell = u;
                        bestSide = side;
                        bestRoad = road;
                        bestCost = cost;
                    }
                }
                int n = board.Neighbors(u, neighbors);
                for (int k = 0; k < n; k++)
                {
                    int m = neighbors[k];
                    if (dist[m] >= 0 || !board.IsOpen(m)) continue;
                    dist[m] = dist[u] + 1;
                    toward[m] = u;
                    queue[tail++] = m;
                }
            }
            if (bestCell < 0)
            {
                // Unreachable by road; cannot happen for a reachable cube (TypeScript drives straight there).
                LastWayInCell = -1;
                PointD c = Center(target);
                path.Add(entry);
                path.Add(c);
                return Hypot(c.X - entry.X, c.Y - entry.Y);
            }
            LastWayInCell = bestCell;
            LastWayInSide = bestSide;
            path.Add(entry);
            RoadPath(entry, bestRoad, path);
            for (int u = bestCell; u != target; u = toward[u]) path.Add(Center(u));
            // A cube on the edge itself: leave the road and pull up right next to it.
            if (bestCell == target) path.Add(Beside(target, bestSide));
            return bestCost;
        }

        /// <summary>
        /// main.ts sendTruck: the ramp lane whose route is strictly cheapest (the first on ties), its route out and the
        /// way home (the route back without its parking point, then down the lane into the tunnel).
        /// </summary>
        public TruckRoute PlanTruck(int target)
        {
            var best = new List<PointD>();
            var tryPath = new List<PointD>();
            double bestCost = double.PositiveInfinity;
            int lane = 0;
            for (int k = 0; k < RoadLayout.LaneCount; k++)
            {
                double cost = RouteFrom(target, RoadLayout.LaneEntry(Ring, k), tryPath);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    lane = k;
                    best.Clear();
                    best.AddRange(tryPath);
                }
            }
            RouteFrom(target, RoadLayout.LaneEntry(Ring, lane), tryPath); // leaves LastWayIn* describing the chosen route
            PointD start = RoadLayout.TunnelStart(lane);
            var home = new List<PointD>(best.Count);
            for (int i = best.Count - 2; i >= 0; i--) home.Add(best[i]);
            home.Add(start);
            return new TruckRoute(target, lane, start, best, home, bestCost, LastWayInCell, LastWayInSide);
        }

        static SideMask Mask(Side s)
        {
            switch (s)
            {
                case Side.Bottom: return SideMask.Bottom;
                case Side.Left: return SideMask.Left;
                case Side.Right: return SideMask.Right;
                default: return SideMask.Top;
            }
        }

        /// <summary>JavaScript Math.hypot for the cases routing uses: one of the two is 0 except in the fallback.</summary>
        static double Hypot(double x, double y)
        {
            if (x == 0) return Math.Abs(y);
            if (y == 0) return Math.Abs(x);
            return Math.Sqrt(x * x + y * y);
        }
    }

    /// <summary>A truck's trip (main.ts sendTruck and the "home" leg).</summary>
    public sealed class TruckRoute
    {
        public TruckRoute(int target, int lane, PointD start, IReadOnlyList<PointD> outward, IReadOnlyList<PointD> home, double cost, int wayInCell, Side wayInSide)
        {
            Target = target;
            Lane = lane;
            Start = start;
            Out = outward;
            Home = home;
            Cost = cost;
            WayInCell = wayInCell;
            WayInSide = wayInSide;
        }

        public int Target { get; }
        /// <summary>Ramp lane, 0..2 (RoadLayout.LaneOffsets).</summary>
        public int Lane { get; }
        /// <summary>Where the truck comes up out of the tunnel and goes back down.</summary>
        public PointD Start { get; }
        /// <summary>From the lane's point on the road to the parking point (the truck drives Start → Out[0] → …).</summary>
        public IReadOnlyList<PointD> Out { get; }
        /// <summary>Back the way it came, then down to <see cref="Start"/>.</summary>
        public IReadOnlyList<PointD> Home { get; }
        public double Cost { get; }
        /// <summary>The cell where it leaves the road (on the field's boundary) and the open side it crosses.</summary>
        public int WayInCell { get; }
        public Side WayInSide { get; }
        public PointD Parking => Out[Out.Count - 1];
    }
}
