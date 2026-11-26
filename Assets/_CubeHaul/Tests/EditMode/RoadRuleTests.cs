using System;
using System.Collections.Generic;
using System.Linq;
using CubeHaul.Domain;
using CubeHaul.Gameplay.Grid;
using CubeHaul.Gameplay.Roads;
using CubeHaul.Levels;
using NUnit.Framework;

namespace CubeHaul.Tests.EditMode
{
    /// <summary>
    /// Road rules (docs/ROADS.md): cube-hunt's roads.test.ts on the C# planner, and every truck route on every reachable
    /// cell of the ten levels in all three edge modes, fresh, peeled and with claimed cubes: in only through an open edge,
    /// along the road, through open cells only, parked next to the cube, and home the same way.
    /// </summary>
    public class RoadRuleTests
    {
        static LevelRepository repo;
        static LevelRepository Repo => repo ?? (repo = LevelRepository.LoadDirectory(ProjectPaths.Of("Assets/_CubeHaul/Levels/Content")).ThrowIfErrors());

        // ---- roads.test.ts: 10-unit cells from (100, 100), the ring 20 outside them ----

        const double CS = 10, O = 100;

        static RoadPathPlanner RoadsFor(Board b)
        {
            var ring = new RoadRing(O - 20, O - 20, O + b.Width * CS + 20, O + b.Height * CS + 20);
            return new RoadPathPlanner(b, O, O, CS, ring, new PointD(O + b.Width * CS / 2, ring.Y1));
        }

        static int CellAt(Board b, PointD p)
        {
            int x = (int)Math.Floor((p.X - O) / CS), y = (int)Math.Floor((p.Y - O) / CS);
            return x < 0 || y < 0 || x >= b.Width || y >= b.Height ? -1 : y * b.Width + x;
        }

        static Board Solid(int w, int h, OpenEdges edges) => new Board(w, h, new int[w * h], 1, edges);

        [Test]
        public void LeavesTheRoadAndParksRightBelowACubeOnTheOpenBottomEdge()
        {
            Board b = Solid(5, 5, OpenEdges.Bottom);
            RoadPathPlanner roads = RoadsFor(b);
            List<PointD> route = roads.Route(21);
            Assert.That(route[0], Is.EqualTo(roads.Entry));
            Assert.That(route[route.Count - 2], Is.EqualTo(new PointD(O + 1.5 * CS, roads.Ring.Y1)));
            Assert.That(route[route.Count - 1], Is.EqualTo(new PointD(O + 1.5 * CS, O + 5.5 * CS)));
        }

        [Test]
        public void DrivesInThroughClearedCellsOnlyAndParksNextToTheCube()
        {
            Board b = Solid(5, 5, OpenEdges.Bottom);
            foreach (int y in new[] { 4, 3, 2 }) b.Eat(y * 5 + 2);
            b.Eat(11);
            int[] cells = RoadsFor(b).Route(11).Select(p => CellAt(b, p)).Where(i => i >= 0).ToArray();
            Assert.That(cells, Is.EqualTo(new[] { 22, 17, 12 }));
            Assert.That(cells.All(b.IsOpen), Is.True);
        }

        [Test]
        public void GoesRoundTheRoadToReachAnOpenSideWhenThatIsShorter()
        {
            Board b = Solid(6, 6, OpenEdges.All);
            RoadPathPlanner roads = RoadsFor(b);
            List<PointD> route = roads.Route(0);
            PointD road = route[route.Count - 2];
            Assert.That(road.X == roads.Ring.X0 || road.Y == roads.Ring.Y0, Is.True);
            Assert.That(route[route.Count - 1].DistanceTo(new PointD(O + 0.5 * CS, O + 0.5 * CS)), Is.EqualTo(CS));
            CollectionAssert.Contains(route, new PointD(roads.Ring.X0, roads.Ring.Y1), "passes the bottom-left corner");
        }

        static IEnumerable<string> LevelIds() => LevelRepository.LoadDirectory(ProjectPaths.Of("Assets/_CubeHaul/Levels/Content")).ThrowIfErrors().LevelIds;

        /// <summary>roads.test.ts per level: a whole game, every truck's route avoids cubes and ends beside its cube.</summary>
        [TestCaseSource(nameof(LevelIds))]
        public void EveryRouteOfAWholeGameAvoidsCubesAndEndsBesideItsCube(string levelId)
        {
            LevelDefinition level = Repo.Get(levelId);
            GameEngine g = level.CreateGame();
            RoadPathPlanner roads = RoadPathPlanner.ForBoard(g.Board);
            var sent = new List<Dispatch>();
            int checkedRoutes = 0;
            for (int guard = 0; g.Status != GameStatus.Won && guard < 100_000; guard++)
            {
                if (g.Status == GameStatus.Stuck) g.AddSlot();
                while (g.State.HasFreeSlot())
                {
                    int col = Enumerable.Range(0, g.State.ColumnCount).Where(c => g.State.ColumnLength(c) > 0 && g.Board.ReachableCount(g.State.Front(c).Color) > 0).DefaultIfEmpty(-1).First();
                    if (col < 0) break;
                    g.Tap(col);
                }
                g.Tick(null, sent);
                if (sent.Count == 0 && g.Status == GameStatus.Playing)
                {
                    int col = Enumerable.Range(0, g.State.ColumnCount).Where(c => g.State.ColumnLength(c) > 0).DefaultIfEmpty(-1).First();
                    if (col >= 0 && g.State.HasFreeSlot()) g.Tap(col);
                }
                foreach (Dispatch d in sent)
                {
                    Check(roads, roads.PlanTruck(d.Cell), levelId);
                    checkedRoutes++;
                }
                foreach (Dispatch d in sent) g.Collect(d.Cell);
            }
            Assert.That(g.Status, Is.EqualTo(GameStatus.Won));
            Assert.That(checkedRoutes, Is.EqualTo(level.Picture.CubeCount), "one route per cube");
        }

        static IEnumerable<TestCaseData> States()
        {
            foreach (string id in LevelIds())
                foreach (OpenEdges edges in new[] { OpenEdges.Bottom, OpenEdges.Sides, OpenEdges.All })
                    yield return new TestCaseData(id, edges).SetName($"EveryReachableCellIsRoutedByTheRules({id}, {edges})");
        }

        /// <summary>
        /// Task 13 acceptance: for every reachable cell (every cube a truck can be sent for) in four states of every
        /// level in every edge mode, the route enters through an open edge only and drives through open space only.
        /// </summary>
        [TestCaseSource(nameof(States))]
        public void EveryReachableCellIsRoutedByTheRules(string levelId, OpenEdges edges)
        {
            PictureDefinition p = Repo.Get(levelId).Picture;
            var board = new Board(p.Width, p.Height, p.Cells, p.Palette.Length, edges);
            RoadPathPlanner roads = RoadPathPlanner.ForBoard(board);
            int routes = 0;
            foreach (int rounds in new[] { 0, 5, 15, 40 })
            {
                Peel(board, rounds == 0 ? 0 : rounds - Peeled);
                routes += CheckAllReachable(board, roads, $"{levelId} {edges} after {rounds} rounds");
            }
            // And with every color's next cube claimed: a claimed cube is still a wall.
            for (int c = 0; c < board.ColorCount; c++)
            {
                int t = board.PickTarget(c);
                if (t >= 0) board.Claim(t);
            }
            routes += CheckAllReachable(board, roads, $"{levelId} {edges} with claimed cubes");
            TestContext.WriteLine($"{levelId} {edges}: {routes} routes checked");
            Assert.That(routes, Is.GreaterThan(50));
        }

        int peeled;
        int Peeled => peeled;

        void Peel(Board board, int rounds)
        {
            for (int r = 0; r < rounds; r++, peeled++)
                for (int c = 0; c < board.ColorCount; c++)
                {
                    int t = board.PickTarget(c);
                    if (t >= 0) board.Eat(t);
                }
        }

        [SetUp]
        public void ResetPeel() => peeled = 0;

        static IEnumerable<string> FixtureCases() =>
            GoldenScenarios.Json(GoldenScenarios.GoldenDir + "/roads.json").Get("cases").Items.Select(c => c.Get("name").String);

        /// <summary>
        /// The same rules on every board state of roads.json, every reachable cell (not only the sampled targets),
        /// including the corridors where a closed edge would be the short way in.
        /// </summary>
        [TestCaseSource(nameof(FixtureCases))]
        public void EveryReachableCellOfTheFixtureStatesIsRoutedByTheRules(string name)
        {
            var c = GoldenScenarios.Json(GoldenScenarios.GoldenDir + "/roads.json").Get("cases").Items.Single(x => x.Get("name").String == name);
            Board board = RoadGoldenTests.BoardOf(c);
            Assert.That(CheckAllReachable(board, RoadPathPlanner.ForBoard(board), name), Is.GreaterThan(0));
        }

        static int CheckAllReachable(Board board, RoadPathPlanner roads, string where)
        {
            int n = 0;
            for (int c = 0; c < board.ColorCount; c++)
                foreach (int cell in board.ReachableCells(c))
                {
                    Check(roads, roads.PlanTruck(cell), where);
                    n++;
                }
            return n;
        }

        /// <summary>Every rule a route has to keep.</summary>
        internal static void Check(RoadPathPlanner roads, TruckRoute t, string where)
        {
            Board b = roads.Board;
            RoadRing ring = roads.Ring;
            double cs = roads.CellSize;
            string at = $"{where}, cell {t.Target}";
            IReadOnlyList<PointD> path = t.Out;

            // Out of the tunnel up its lane to the road.
            Assert.That(t.Start, Is.EqualTo(RoadLayout.TunnelStart(t.Lane)), at + ": starts in its tunnel lane");
            Assert.That(path[0], Is.EqualTo(RoadLayout.LaneEntry(ring, t.Lane)), at + ": joins the road where its lane meets it");

            // In only through an open edge of this board.
            int x = t.WayInCell % b.Width, y = t.WayInCell / b.Width;
            Assert.That(t.WayInCell, Is.GreaterThanOrEqualTo(0), at + ": has a way in");
            bool allowed = t.WayInSide == Side.Bottom
                || (t.WayInSide == Side.Left || t.WayInSide == Side.Right) && b.Edges != OpenEdges.Bottom
                || t.WayInSide == Side.Top && b.Edges == OpenEdges.All;
            Assert.That(allowed, Is.True, $"{at}: entered through the {t.WayInSide} edge, closed with {b.Edges} edges");
            bool onThatEdge = t.WayInSide == Side.Bottom ? y == b.Height - 1 : t.WayInSide == Side.Top ? y == 0 : t.WayInSide == Side.Left ? x == 0 : x == b.Width - 1;
            Assert.That(onThatEdge, Is.True, at + ": the way-in cell is on that edge");
            Assert.That(t.WayInCell == t.Target || b.IsOpen(t.WayInCell), Is.True, at + ": the way-in cell is cleared, or the cube itself");

            // Along the road: every point up to the road point is on the ring, corner to corner. (When the lane meets
            // the road exactly at the way in, cube-hunt's route lists that point twice: the entry, then the road point.)
            PointD roadPoint = roads.RoadPoint(t.WayInCell, t.WayInSide);
            int k = -1;
            for (int j = 0; j < path.Count; j++)
                if (path[j].Equals(roadPoint)) k = j;
            Assert.That(k, Is.GreaterThanOrEqualTo(0), at + ": reaches the road point of its way in");
            for (int j = 0; j <= k; j++) Assert.That(ring.IsOn(path[j]), Is.True, $"{at}: point {j} {path[j]} is on the road");
            for (int j = 1; j <= k; j++)
                Assert.That(path[j].X == path[j - 1].X || path[j].Y == path[j - 1].Y, Is.True, $"{at}: road segment {j} follows the road");

            // Then only open cells, one step at a time, ending next to the cube.
            PointD cube = roads.Center(t.Target);
            int previous = -1;
            for (int j = k + 1; j < path.Count; j++)
            {
                bool last = j == path.Count - 1;
                int cell = Cell(roads, path[j]);
                if (last && t.WayInCell == t.Target)
                {
                    Assert.That(cell, Is.EqualTo(-1), at + ": parks just outside the field");
                    Assert.That(path[j], Is.EqualTo(roads.Beside(t.Target, t.WayInSide)), at + ": facing the cube");
                    break;
                }
                Assert.That(cell, Is.GreaterThanOrEqualTo(0), $"{at}: point {j} is in the field");
                Assert.That(path[j], Is.EqualTo(roads.Center(cell)), $"{at}: point {j} is a cell center");
                Assert.That(b.IsOpen(cell), Is.True, $"{at}: point {j} (cell {cell}) is open, cleared space");
                if (previous < 0) Assert.That(cell, Is.EqualTo(t.WayInCell), at + ": the first cell is the way in");
                else Assert.That(Math.Abs(cell - previous) == 1 && cell / b.Width == previous / b.Width || Math.Abs(cell - previous) == b.Width, Is.True, $"{at}: cells {previous} -> {cell} are neighbours");
                previous = cell;
            }
            Assert.That(t.Parking.DistanceTo(cube), Is.EqualTo(cs).Within(1e-9), at + ": parked one cell from the cube");

            // Its cost (what picks the lane and the way in) is the drive along the road, in to the first cell, and one
            // cell per step to the cube. (Not simply the route's length: with cells over twice the road gap, which only
            // tiny boards have, the parking point beside an edge cube lies beyond the road.)
            double road = 0;
            for (int j = 1; j <= k; j++) road += path[j - 1].DistanceTo(path[j]);
            int cellsDriven = path.Count - 1 - k - (t.WayInCell == t.Target ? 1 : 0);
            double expected = road + roadPoint.DistanceTo(roads.Center(t.WayInCell)) + (t.WayInCell == t.Target ? 0 : cellsDriven) * cs;
            Assert.That(t.Cost, Is.EqualTo(expected).Within(1e-6), at + ": cost = road + way in + one cell per step");
            // Home is the same way back, then down the lane.
            Assert.That(t.Home.Count, Is.EqualTo(path.Count));
            for (int j = 0; j < path.Count - 1; j++) Assert.That(t.Home[j], Is.EqualTo(path[path.Count - 2 - j]), at + ": home retraces the route");
            Assert.That(t.Home[t.Home.Count - 1], Is.EqualTo(t.Start), at + ": and goes back down its lane");
        }

        static int Cell(RoadPathPlanner roads, PointD p)
        {
            double fx = (p.X - roads.FieldX) / roads.CellSize, fy = (p.Y - roads.FieldY) / roads.CellSize;
            if (fx < 0 || fy < 0) return -1;
            int x = (int)Math.Floor(fx), y = (int)Math.Floor(fy);
            return x < roads.Board.Width && y < roads.Board.Height ? y * roads.Board.Width + x : -1;
        }

        // ---- BoardCoordinateMapper ----

        [TestCaseSource(nameof(LevelIds))]
        public void CellsDesignPointsAndTheWorldMapBothWays(string levelId)
        {
            PictureDefinition p = Repo.Get(levelId).Picture;
            var map = new BoardCoordinateMapper(p.Width, p.Height);
            var field = new CubeField(p);
            var roads = RoadPathPlanner.ForBoard(new Board(p.Width, p.Height, p.Cells, p.Palette.Length));
            for (int i = 0; i < p.Cells.Length; i++)
            {
                PointD c = map.CellCenter(i);
                Assert.That(c, Is.EqualTo(roads.Center(i)), "the planner and the mapper agree");
                (double fx, double fy) = field.DesignCenter(i);
                Assert.That(c.X, Is.EqualTo(fx).Within(1e-9));
                Assert.That(c.Y, Is.EqualTo(fy).Within(1e-9));
                Assert.That(map.CellAt(c), Is.EqualTo(i));
                (double wx, double wy, double wz) = map.CellToWorld(i, 5);
                Assert.That((wx, wy, wz), Is.EqualTo((c.X * 0.025, 5 * 0.025, -c.Y * 0.025)));
                Assert.That(map.WorldToCell(wx, wz), Is.EqualTo(i), "world -> cell");
                PointD back = map.FromWorld(wx, wz);
                Assert.That(back.X, Is.EqualTo(c.X).Within(1e-9));
                Assert.That(back.Y, Is.EqualTo(c.Y).Within(1e-9));
            }
            // The ramp side (larger design y) is nearer the camera: smaller world z. Column 0 is at the smallest x.
            Assert.That(map.CellToWorld(p.Width).z, Is.LessThan(map.CellToWorld(0).z));
            Assert.That(map.CellToWorld(1).x, Is.GreaterThan(map.CellToWorld(0).x));
            Assert.That(map.CellAt(new PointD(map.Ring.X0, map.Ring.Y1)), Is.EqualTo(-1), "the road is off the field");
            Assert.That(map.CellAt(RoadLayout.TunnelStart(1)), Is.EqualTo(-1), "the tunnel is off the field");
        }

        [Test]
        public void TheRoadRingAndRampSitBetweenTheBoardAndTheSlots()
        {
            foreach (string id in LevelIds())
            {
                PictureDefinition p = Repo.Get(id).Picture;
                RoadRing ring = new BoardCoordinateMapper(p.Width, p.Height).Ring;
                Assert.That(ring.X0 >= CubeHaul.Gameplay.Layout.DesignLayout.Board.XMin && ring.X1 <= CubeHaul.Gameplay.Layout.DesignLayout.Board.XMax, Is.True, id + ": the road is on the board");
                Assert.That(ring.Y0 >= CubeHaul.Gameplay.Layout.DesignLayout.Board.YMin && ring.Y1 <= CubeHaul.Gameplay.Layout.DesignLayout.Board.YMax, Is.True, id);
                for (int k = 0; k < RoadLayout.LaneCount; k++)
                {
                    PointD entry = RoadLayout.LaneEntry(ring, k), start = RoadLayout.TunnelStart(k);
                    Assert.That(entry.X, Is.EqualTo(start.X), id + ": a lane runs straight down to the tunnel");
                    Assert.That(start.Y, Is.GreaterThan(entry.Y));
                    Assert.That(entry.X > ring.X0 && entry.X < ring.X1, Is.True, id + ": every lane meets the bottom road");
                }
            }
        }

        [Test]
        public void RoutingAllocatesNothingBeyondTheOutputList()
        {
            PictureDefinition p = Repo.Get("cat").Picture;
            var board = new Board(p.Width, p.Height, p.Cells, p.Palette.Length, OpenEdges.Bottom);
            RoadPathPlanner roads = RoadPathPlanner.ForBoard(board);
            var path = new List<PointD>(256);
            int target = board.PickTarget(0);
            roads.RouteFrom(target, roads.Entry, path); // warm up
            int before = path.Capacity;
            for (int k = 0; k < 100; k++) roads.RouteFrom(target, roads.Entry, path);
            Assert.That(path.Capacity, Is.EqualTo(before));
            Assert.Throws<ArgumentOutOfRangeException>(() => roads.RouteFrom(-1, roads.Entry, path));
            Assert.Throws<ArgumentNullException>(() => roads.RouteFrom(target, roads.Entry, null));
        }
    }
}
