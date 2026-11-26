using System;
using System.Collections.Generic;
using System.Linq;
using CubeHaul.Domain;
using CubeHaul.Gameplay.Roads;
using CubeHaul.Gameplay.Trucks;
using CubeHaul.Levels;
using NUnit.Framework;

namespace CubeHaul.Tests.EditMode
{
    /// <summary>
    /// Truck movement (docs/TRUCKS.md): a truck drives its planned route to its cube, loads it, turns and drives back
    /// down its ramp lane, as main.ts does: at CAR_SPEED, waypoint to waypoint, steering smoothly, keeping right on the
    /// road and fading in and out at the tunnel.
    /// </summary>
    public class TruckMotionTests
    {
        const double Dt = 1 / 60.0;

        static LevelRepository repo;
        static LevelRepository Repo => repo ?? (repo = LevelRepository.LoadDirectory(ProjectPaths.Of("Assets/_CubeHaul/Levels/Content")).ThrowIfErrors());

        static (RoadPathPlanner roads, Board board) Strawberry(int peel = 0)
        {
            LevelDefinition level = Repo.Get("strawberry");
            Board board = level.CreateBoard();
            for (int r = 0; r < peel; r++)
                for (int c = 0; c < board.ColorCount; c++)
                {
                    int t = board.PickTarget(c);
                    if (t >= 0) board.Eat(t);
                }
            return (RoadPathPlanner.ForBoard(board), board);
        }

        static TruckMotion Truck(RoadPathPlanner roads, int cell) =>
            new TruckMotion(roads.PlanTruck(cell), roads.Center(cell), roads.CellSize, roads.Ring);

        static double Length(IReadOnlyList<PointD> path, PointD from)
        {
            double n = from.DistanceTo(path[0]);
            for (int k = 1; k < path.Count; k++) n += path[k - 1].DistanceTo(path[k]);
            return n;
        }

        [Test]
        public void ATruckDrivesToItsCubeLoadsItAndDrivesBackDownItsLane()
        {
            var (roads, board) = Strawberry(peel: 8);
            int cell = board.PickTarget(0);
            TruckMotion t = Truck(roads, cell);
            TruckRoute r = t.Route;
            double outLength = Length(r.Out, r.Start), homeLength = Length(r.Home, r.Parking);
            Assert.That((t.State, t.X, t.Y, t.HasCargo), Is.EqualTo((TruckState.Out, r.Start.X, r.Start.Y, false)));

            double time = 0, arrivedAt = -1, loadedAt = -1, goneAt = -1;
            for (int frame = 0; frame < 100_000 && t.State != TruckState.Gone; frame++)
            {
                TruckEvents e = t.Advance(Dt);
                time += Dt;
                if ((e & TruckEvents.Arrived) != 0)
                {
                    Assert.That(arrivedAt, Is.EqualTo(-1), "arrives once");
                    arrivedAt = time;
                    Assert.That((t.X, t.Y), Is.EqualTo((r.Parking.X, r.Parking.Y)), "parked where the route ends");
                    Assert.That(t.HasCargo, Is.True, "the cube is collected on arrival");
                }
                if ((e & TruckEvents.Loaded) != 0) loadedAt = time;
                if ((e & TruckEvents.Gone) != 0) goneAt = time;
            }
            Assert.That(arrivedAt, Is.EqualTo(outLength / TruckMotion.Speed).Within(Dt), "arrives after its route's length at CAR_SPEED");
            Assert.That(loadedAt - arrivedAt, Is.EqualTo(TruckMotion.LoadTime).Within(Dt), "loads for LOAD_TIME");
            Assert.That(goneAt - loadedAt, Is.EqualTo(homeLength / TruckMotion.Speed).Within(Dt), "drives home the same way");
            Assert.That((t.State, t.X, t.Y), Is.EqualTo((TruckState.Gone, r.Start.X, r.Start.Y)), "back where it came up out of the tunnel");
            Assert.That(t.Odometer, Is.EqualTo(outLength + homeLength).Within(1e-6));
            Assert.That(t.Advance(Dt), Is.EqualTo(TruckEvents.None), "a truck that is gone stays gone");
        }

        [Test]
        public void ItStaysOnItsRouteAndTurnsSmoothly()
        {
            var (roads, board) = Strawberry(peel: 12);
            foreach (int cell in Enumerable.Range(0, board.ColorCount).SelectMany(board.ReachableCells).Take(40))
            {
                TruckMotion t = Truck(roads, cell);
                double angle = t.Angle;
                for (int frame = 0; frame < 100_000 && t.State != TruckState.Gone; frame++)
                {
                    TruckState before = t.State;
                    t.Advance(Dt);
                    double turned = Math.Abs(Wrap(t.Angle - angle));
                    double limit = (before == TruckState.Load ? TruckMotion.LoadTurnRate : TruckMotion.DriveTurnRate) * Dt;
                    Assert.That(turned, Is.LessThanOrEqualTo(limit + 1e-12), $"cell {cell} frame {frame}: no snap turns");
                    angle = t.Angle;
                    Assert.That(DistanceToPath(t), Is.LessThan(1e-6), $"cell {cell} frame {frame}: on its route");
                }
                Assert.That(t.State, Is.EqualTo(TruckState.Gone), "cell " + cell);
            }
        }

        [Test]
        public void WhileLoadingItTurnsToFaceItsCube()
        {
            var (roads, board) = Strawberry(peel: 4);
            TruckMotion t = Truck(roads, board.PickTarget(1));
            while (t.State != TruckState.Load) t.Advance(Dt);
            double toCube = Math.Atan2(t.Cube.Y - t.Y, t.Cube.X - t.X);
            while (t.State == TruckState.Load) t.Advance(Dt);
            Assert.That(Math.Abs(Wrap(t.Angle - toCube)), Is.LessThan(0.05), "faces the cube by the time it is loaded");
        }

        [Test]
        public void ItKeepsRightOnTheRoadButDrivesCenteredInTheField()
        {
            var (roads, board) = Strawberry(peel: 10);
            TruckMotion t = Truck(roads, board.PickTarget(0));
            RoadRing ring = roads.Ring;
            Assert.That(t.LaneShare(ring.X0 + 40, ring.Y1), Is.EqualTo(1), "on the road");
            Assert.That(t.LaneShare(Center(ring).X, Center(ring).Y), Is.EqualTo(0), "in the middle of the field");
            Assert.That(t.LaneShare(RoadLayout.TunnelStart(0).X, RoadLayout.TunnelStart(0).Y), Is.EqualTo(1), "on the driveway");
            // Heading right (+x) on the bottom road, the lane is below the center line (y down: towards the camera).
            for (int frame = 0; frame < 100_000 && t.State != TruckState.Gone; frame++)
            {
                t.Advance(Dt);
                PointD pose = t.Pose();
                double offset = pose.DistanceTo(new PointD(t.X, t.Y));
                Assert.That(offset, Is.EqualTo(TruckMotion.Lane * t.CarScale * t.LaneShare(t.X, t.Y)).Within(1e-9));
                if (offset > 0)
                {
                    double side = Math.Cos(t.Angle) * (pose.Y - t.Y) - Math.Sin(t.Angle) * (pose.X - t.X);
                    Assert.That(side, Is.GreaterThan(0), "the lane is to the right of the direction of travel");
                }
            }
        }

        [Test]
        public void ItRisesOutOfTheTunnelAndSinksBackIntoIt()
        {
            var (roads, board) = Strawberry(peel: 2);
            TruckMotion t = Truck(roads, board.PickTarget(0));
            Assert.That((t.Fade, t.Visible), Is.EqualTo((0.0, false)), "starts in the tunnel");
            t.Advance(5 / TruckMotion.Speed);
            Assert.That(t.Fade, Is.EqualTo(5 / TruckMotion.RampFade).Within(1e-9));
            Assert.That(t.DrawScale, Is.EqualTo(t.CarScale * (0.55 + 0.45 * t.Fade)).Within(1e-12));
            t.Advance(1);
            Assert.That(t.Fade, Is.EqualTo(1), "out on the road");
            while (t.State != TruckState.Gone) t.Advance(Dt);
            Assert.That((t.Fade, t.Visible), Is.EqualTo((0.0, false)), "back in the tunnel");
        }

        [Test]
        public void TheCubeLandsInTheBedPartWayThroughTheLoad()
        {
            var (roads, board) = Strawberry();
            TruckMotion t = Truck(roads, board.PickTarget(0));
            while (t.State != TruckState.Load) t.Advance(Dt);
            Assert.That(t.CargoVisible, Is.False, "the cube is still on its way up");
            while (t.State == TruckState.Load && t.LoadProgress < TruckMotion.LandAt) t.Advance(Dt);
            while (t.State == TruckState.Load) t.Advance(Dt);
            Assert.That(t.CargoVisible, Is.True, "in the bed for the drive home");
        }

        [Test]
        public void TrucksShrinkWithTheCubes()
        {
            Assert.That(TruckMotion.ScaleFor(312.0 / 29), Is.EqualTo(312.0 / 29 / 13).Within(1e-12));
            Assert.That(TruckMotion.ScaleFor(312.0 / 46), Is.EqualTo(0.52).Within(0.01));
            Assert.That(TruckMotion.ScaleFor(2), Is.EqualTo(0.5));
            Assert.That(TruckMotion.ScaleFor(60), Is.EqualTo(1));
        }

        [TestCase(0, Math.PI / 2, 0.1, 0.1)]
        [TestCase(3.0, -3.0, 1.0, 3.0 + (2 * Math.PI - 6.0))]
        [TestCase(-3.0, 3.0, 0.1, -3.1)]
        [TestCase(1.0, 1.05, 0.5, 1.05)]
        public void TurnTowardTakesTheShortWayRound(double from, double to, double step, double expected)
        {
            Assert.That(TruckMotion.TurnToward(from, to, step), Is.EqualTo(expected).Within(1e-12));
        }

        [Test]
        public void EveryTruckOfAWholeGameComesHome()
        {
            LevelDefinition level = Repo.Get("rocket");
            GameEngine g = level.CreateGame();
            RoadPathPlanner roads = RoadPathPlanner.ForBoard(g.Board);
            var trucks = new List<TruckMotion>();
            var sent = new List<Dispatch>();
            int home = 0, arrived = 0;
            for (int frame = 0; frame < 1_000_000 && (g.Status != GameStatus.Won || trucks.Count > 0); frame++)
            {
                if (frame % 9 == 0 && g.Status == GameStatus.Playing)
                {
                    for (int c = 0; c < g.State.ColumnCount && g.State.HasFreeSlot(); c++)
                        if (g.State.ColumnLength(c) > 0 && g.Board.ReachableCount(g.State.Front(c).Color) > 0) g.Tap(c);
                    g.Tick(null, sent);
                    foreach (Dispatch d in sent) trucks.Add(Truck(roads, d.Cell));
                    // Nothing can eat and no truck is out: tap a blocked crate, as a player would.
                    if (sent.Count == 0 && trucks.Count == 0 && g.State.HasFreeSlot())
                        for (int c = 0; c < g.State.ColumnCount; c++)
                            if (g.Tap(c) >= 0) break;
                }
                if (g.Status == GameStatus.Stuck) g.AddSlot();
                for (int k = trucks.Count - 1; k >= 0; k--)
                {
                    TruckEvents e = trucks[k].Advance(Dt);
                    if ((e & TruckEvents.Arrived) != 0)
                    {
                        g.Collect(trucks[k].Cell); // main.ts pickUp: the cube leaves the board when its truck arrives
                        arrived++;
                    }
                    if ((e & TruckEvents.Gone) != 0)
                    {
                        trucks.RemoveAt(k);
                        home++;
                    }
                }
            }
            Assert.That(g.Status, Is.EqualTo(GameStatus.Won));
            Assert.That((arrived, home), Is.EqualTo((level.Picture.CubeCount, level.Picture.CubeCount)), "one truck per cube, and every one came home");
        }

        static PointD Center(RoadRing r) => new PointD((r.X0 + r.X1) / 2, (r.Y0 + r.Y1) / 2);

        static double Wrap(double a)
        {
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a < -Math.PI) a += 2 * Math.PI;
            return a;
        }

        /// <summary>Distance from the truck to the nearest segment of the route it is on (Start → Out…, or Parking → Home…).</summary>
        static double DistanceToPath(TruckMotion t)
        {
            var p = new PointD(t.X, t.Y);
            var points = new List<PointD>();
            if (t.State == TruckState.Out || t.State == TruckState.Load) points.Add(t.Route.Start);
            else points.Add(t.Route.Parking);
            points.AddRange(t.State == TruckState.Home || t.State == TruckState.Gone ? t.Route.Home : t.Route.Out);
            double best = double.PositiveInfinity;
            for (int k = 1; k < points.Count; k++) best = Math.Min(best, Segment(p, points[k - 1], points[k]));
            return best;
        }

        static double Segment(PointD p, PointD a, PointD b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, len = dx * dx + dy * dy;
            double t = len == 0 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len));
            return p.DistanceTo(new PointD(a.X + t * dx, a.Y + t * dy));
        }
    }
}
