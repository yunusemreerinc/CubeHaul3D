using System;
using System.Collections.Generic;
using System.Linq;
using CubeHaul.Domain;
using CubeHaul.Gameplay;
using CubeHaul.Gameplay.Session;
using CubeHaul.Gameplay.Trucks;
using CubeHaul.Levels;
using NUnit.Framework;

namespace CubeHaul.Tests.EditMode
{
    /// <summary>
    /// Task 15 (docs/SESSION.md): the rules joined to main.ts's clock. The first level's truck loop runs from the first
    /// tap to the last truck home at 1x, 2x and 3x; one truck per cube; a cube leaves the board only when its truck
    /// reaches it, and the cubes behind it stay closed until then; slot cooldowns, ramp lanes and the crate flight keep
    /// main.ts's timing; the same game at any frame rate.
    /// </summary>
    public class SessionRuleTests
    {
        const double Dt = 1.0 / 60;

        static LevelDefinition Level(string id) => LevelRepository.LoadDirectory(ProjectPaths.Of("Assets/_CubeHaul/Levels/Content")).ThrowIfErrors().Get(id);

        /// <summary>Everything that happened in a game, with real and simulation times.</summary>
        sealed class Trace
        {
            public readonly List<(double real, double sim, int col, int slot)> Taps = new List<(double, double, int, int)>();
            public readonly List<(double real, double sim, TruckMotion truck)> Sent = new List<(double, double, TruckMotion)>();
            public readonly List<(double sim, TruckMotion truck)> Launched = new List<(double, TruckMotion)>();
            public readonly List<(double sim, TruckMotion truck)> Collected = new List<(double, TruckMotion)>();
            public readonly List<TruckMotion> Returned = new List<TruckMotion>();
            public readonly List<string> Problems = new List<string>();
        }

        static Trace Watch(GameSession s)
        {
            var tr = new Trace();
            Board board = s.Game.Board;
            s.CrateTapped += (col, slot) => tr.Taps.Add((s.RealTime, s.SimTime, col, slot));
            s.TruckSent += t =>
            {
                tr.Sent.Add((s.RealTime, s.SimTime, t));
                if (!board.IsClaimed(t.Cell)) tr.Problems.Add($"cell {t.Cell}: a truck was sent for a cube that is not claimed");
                if (board.Cell(t.Cell) == Cells.Empty) tr.Problems.Add($"cell {t.Cell}: a truck was sent for an empty cell");
                // Independent check: on a board built from scratch with every cube still standing (claimed ones too),
                // the cube must be reachable. A cube behind one whose truck is still on the way never is.
                var cells = new int[board.CellCount];
                board.CopyCells(cells);
                var fresh = new Board(board.Width, board.Height, cells, board.ColorCount, board.Edges);
                if (!fresh.IsReachable(t.Cell)) tr.Problems.Add($"cell {t.Cell}: claimed before the way to it was open");
            };
            s.TruckLaunched += t => tr.Launched.Add((s.SimTime, t));
            s.CubeCollected += t =>
            {
                tr.Collected.Add((s.SimTime, t));
                if (board.Cell(t.Cell) != Cells.Empty) tr.Problems.Add($"cell {t.Cell}: still on the board after its collect");
                if (t.State != TruckState.Load) tr.Problems.Add($"cell {t.Cell}: collected by a truck that is {t.State}");
                if (t.X != t.Route.Parking.X || t.Y != t.Route.Parking.Y) tr.Problems.Add($"cell {t.Cell}: collected away from its parking spot");
            };
            s.TruckReturned += t => tr.Returned.Add(t);
            return tr;
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void TheFirstLevelsTruckLoopRunsFromStartToEnd(int speed)
        {
            LevelDefinition level = Level("strawberry");
            var s = GameSession.For(level);
            s.SetSpeed(speed);
            Trace tr = Watch(s);
            AutoPlayOutcome outcome = AutoPlayer.Play(s, Dt);

            Assert.That(tr.Problems, Is.Empty);
            Assert.That(outcome, Is.EqualTo(AutoPlayOutcome.Won));
            Assert.That(s.Game.Status, Is.EqualTo(GameStatus.Won));
            Assert.That(s.Game.Board.Remaining, Is.Zero, "every cube collected");
            Assert.That(s.Game.Board.Pending, Is.Zero);
            int cubes = level.Picture.CubeCount;
            Assert.That(tr.Sent.Count, Is.EqualTo(cubes), "one truck per cube");
            Assert.That(tr.Sent.Select(x => x.truck.Cell).Distinct().Count(), Is.EqualTo(cubes), "no cube given to two trucks");
            Assert.That(tr.Collected.Count, Is.EqualTo(cubes));
            CollectionAssert.AreEquivalent(tr.Sent.Select(x => x.truck), tr.Collected.Select(x => x.truck), "each truck collected its own cube");
            CollectionAssert.AreEquivalent(tr.Sent.Select(x => x.truck), tr.Returned, "and came home");
            Assert.That(s.TrucksOut, Is.Zero);
            Assert.That(s.IsOver, Is.True);
            int taps = level.Tray.Columns.Sum(c => c.Count());
            Assert.That(tr.Taps.Count, Is.EqualTo(taps), "every crate was played");
            Assert.That(s.SlotBoostUsed, Is.False, "the first level needs no +1 Slot");
            // Every truck drove its whole route both ways: it left the ramp, reached its cube and came back.
            foreach ((double sim, TruckMotion t) in tr.Collected)
            {
                double launched = tr.Launched.Single(l => l.truck == t).sim;
                double length = RouteLength(t.Route.Start, t.Route.Out);
                // It already drives in the frame it leaves the ramp (whose time is counted from the frame's start).
                Assert.That(sim - launched, Is.GreaterThanOrEqualTo(length / SimConstants.CarSpeed - Dt * speed - 1e-9), $"cell {t.Cell}: no shortcut");
                Assert.That(sim - launched, Is.LessThan(length / SimConstants.CarSpeed + Dt * speed), $"cell {t.Cell}: and no waiting on the road");
            }

            // The end panel waits 0.4 s after the last truck is home.
            for (int k = 0; k < 30; k++) s.Advance(Dt);
            Assert.That(s.ShowsEndPanel, Is.True);
        }

        static double RouteLength(Gameplay.Roads.PointD from, IReadOnlyList<Gameplay.Roads.PointD> path)
        {
            double n = from.DistanceTo(path[0]);
            for (int k = 1; k < path.Count; k++) n += path[k - 1].DistanceTo(path[k]);
            return n;
        }

        [Test]
        public void TheSpeedScalesTheTrucksButNotTheCrateFlight()
        {
            LevelDefinition level = Level("strawberry");
            var real = new Dictionary<int, double>();
            var firstTruck = new Dictionary<int, double>();
            foreach (int speed in new[] { 1, 2, 3 })
            {
                var s = GameSession.For(level);
                s.SetSpeed(speed);
                Trace tr = Watch(s);
                Assert.That(AutoPlayer.Play(s, Dt), Is.EqualTo(AutoPlayOutcome.Won));
                real[speed] = s.RealTime;
                // The first crate flies 1/3.8 s of real time before it sends anything, at any speed.
                firstTruck[speed] = tr.Sent[0].real - tr.Taps[0].real;
                Assert.That(firstTruck[speed], Is.GreaterThanOrEqualTo(SimConstants.CrateFlightTime - 1e-9), $"{speed}x");
                Assert.That(firstTruck[speed], Is.LessThan(SimConstants.CrateFlightTime + 2 * Dt), $"{speed}x");
                Assert.That(s.SimTime, Is.EqualTo(s.RealTime * speed).Within(1e-6), $"{speed}x: simulation time = real time x speed");
            }
            Assert.That(real[2], Is.LessThan(real[1] * 0.6), "2x takes about half the time");
            Assert.That(real[3], Is.LessThan(real[1] * 0.45), "3x about a third");
        }

        [Test]
        public void ASlotWaitsPopIntervalBetweenItsTrucks()
        {
            var s = GameSession.For(Level("mushroom"));
            s.SetSpeed(2);
            Trace tr = Watch(s);
            AutoPlayer.Play(s, Dt);
            Assert.That(tr.Problems, Is.Empty);
            int checkedPairs = 0;
            foreach (var bySlot in tr.Sent.GroupBy(x => x.truck.Slot))
            {
                double[] times = bySlot.Select(x => x.sim).ToArray();
                for (int k = 1; k < times.Length; k++, checkedPairs++)
                    Assert.That(times[k] - times[k - 1], Is.GreaterThanOrEqualTo(SimConstants.PopInterval - 1e-9), $"slot {bySlot.Key}, truck {k}");
                // A crate with cubes in reach sends one truck per POP_INTERVAL, rounded up to whole frames: 0.15 s at 2x is 4.5 frames.
                Assert.That(times.Zip(times.Skip(1), (a, b) => b - a).Min(), Is.LessThan(SimConstants.PopInterval + 2 * Dt * 2 + 1e-9));
            }
            Assert.That(checkedPairs, Is.GreaterThan(1000));
        }

        [Test]
        public void ARampLaneLetsItsTrucksOutOneAtATimeInTheOrderTheyWereSent()
        {
            var s = GameSession.For(Level("strawberry"));
            Trace tr = Watch(s);
            AutoPlayer.Play(s, Dt);
            double spacing = s.Fleet.LaneSpacing;
            Assert.That(spacing, Is.EqualTo(SimConstants.ConvoyGap * TruckMotion.ScaleFor(s.Roads.CellSize) / SimConstants.CarSpeed));
            foreach (var lane in tr.Launched.GroupBy(x => x.truck.Route.Lane))
            {
                var launched = lane.ToList();
                for (int k = 1; k < launched.Count; k++)
                    Assert.That(launched[k].sim - launched[k - 1].sim, Is.GreaterThanOrEqualTo(spacing - 1e-9), $"lane {lane.Key}, truck {k}");
                var sentOrder = tr.Sent.Where(x => x.truck.Route.Lane == lane.Key).Select(x => x.truck).ToList();
                CollectionAssert.AreEqual(sentOrder, launched.Select(x => x.truck), $"lane {lane.Key}: first sent, first out");
            }
            Assert.That(tr.Launched.Select(x => x.truck.Route.Lane).Distinct().Count(), Is.GreaterThan(1), "more than one lane is used");
        }

        [Test]
        public void ATruckThatIsStillOnItsWayKeepsTheCubesBehindItsCubeClosed()
        {
            // A 1x3 column, bottom edge only: the cube behind the bottom one opens only when the bottom one is collected.
            var board = new Board(1, 3, new[] { 0, 0, 0 }, 1, OpenEdges.Bottom);
            var game = new GameEngine(board, new[] { new[] { new CrateSpec(0, 0, 3) } }, 1);
            var s = new GameSession(game);
            var sentAt = new List<(int cell, long frame)>();
            var collectedAt = new List<(int cell, long frame)>();
            s.TruckSent += t => sentAt.Add((t.Cell, s.Frames));
            s.CubeCollected += t => collectedAt.Add((t.Cell, s.Frames));
            s.Tap(0);
            for (int f = 0; f < 3000 && !s.IsOver; f++)
            {
                s.Advance(Dt);
                // Never more than one truck out: the next cube is not reachable before the one in front is collected.
                Assert.That(s.Game.Board.Pending, Is.LessThanOrEqualTo(1), $"frame {s.Frames}");
            }
            Assert.That(s.Game.Status, Is.EqualTo(GameStatus.Won));
            CollectionAssert.AreEqual(new[] { 2, 1, 0 }, sentAt.Select(x => x.cell), "bottom first");
            for (int k = 1; k < 3; k++)
                Assert.That(sentAt[k].frame, Is.GreaterThan(collectedAt[k - 1].frame), $"cube {sentAt[k].cell} waited for cube {collectedAt[k - 1].cell} to be collected");
        }

        [Test]
        public void TheSameGameAtAnyFrameRate()
        {
            JsonOf("s-01-strawberry-1x-60fps", out LevelDefinition level, out var expected);
            var rng = new Random(7);
            foreach (string name in new[] { "30 Hz", "60 Hz", "90 Hz", "120 Hz", "144 Hz", "uneven" })
            {
                var loop = new SessionLoop(GameSession.For(level)) { AutoPlay = true };
                var log = new SessionGoldenTests.Log();
                log.Attach(loop.Session);
                int frames = 0;
                while (loop.Outcome == AutoPlayOutcome.Playing && frames++ < 100_000)
                {
                    double dt = name switch
                    {
                        "30 Hz" => 1.0 / 30,
                        "60 Hz" => 1.0 / 60,
                        "90 Hz" => 1.0 / 90,
                        "120 Hz" => 1.0 / 120,
                        "144 Hz" => 1.0 / 144,
                        _ => rng.NextDouble() * 0.04 + 0.001,
                    };
                    // Each step is a 1/60 s frame of cube-hunt; Frame numbers the steps like the golden run does.
                    int before = (int)loop.Session.Frames;
                    log.Frame = before + 1;
                    int steps = loop.Clock.Advance(dt);
                    for (int k = 0; k < steps && loop.Outcome == AutoPlayOutcome.Playing; k++)
                    {
                        log.Frame = (int)loop.Session.Frames + 1;
                        loop.Step();
                    }
                }
                Assert.That(loop.Outcome, Is.EqualTo(AutoPlayOutcome.Won), name);
                CollectionAssert.AreEqual(expected, log.Rows, $"{name}: the same taps, trucks and pickups in the same steps as cube-hunt at 60 Hz");
            }
        }

        static void JsonOf(string run, out LevelDefinition level, out List<(int frame, int kind, int a, int b)> rows)
        {
            var doc = GoldenScenarios.Json(GoldenScenarios.GoldenDir + "/sessions/" + run + ".json");
            level = SessionGoldenTests.LevelOf(doc);
            rows = SessionGoldenTests.Expected(doc);
        }

        [Test]
        public void TapsAndTheBoosterFollowMainTs()
        {
            var s = GameSession.For(Level("strawberry"));
            int refused = 0;
            s.TapRefused += _ => refused++;
            Assert.That(s.Tap(-1), Is.EqualTo(-1), "no such column");
            int slots = s.Game.State.SlotCount;
            int Column() => Enumerable.Range(0, s.Game.State.ColumnCount).First(c => s.Game.State.ColumnLength(c) > 0);
            for (int k = 0; k < slots; k++) Assert.That(s.Tap(Column()), Is.EqualTo(k));
            Assert.That(s.Tap(Column()), Is.EqualTo(-1), "every slot is full");
            Assert.That(refused, Is.EqualTo(1), "the tray shakes");
            for (int k = 0; k < slots; k++) Assert.That(s.Scheduler.IsFlying(k), Is.True);
            Assert.That(s.Scheduler.IsReady(0), Is.False, "a flying crate sends nothing");

            Assert.That(s.CanAddSlot, Is.True);
            Assert.That(s.AddSlot(), Is.True);
            Assert.That(s.Game.State.SlotCount, Is.EqualTo(slots + 1));
            Assert.That(s.AddSlot(), Is.False, "once per level");
            Assert.That(s.Scheduler.IsReady(SimConstants.MaxSlots), Is.False, "main.ts has timers for six slots only");

            Assert.That(s.Speed, Is.EqualTo(1));
            var seen = new List<int>();
            s.SpeedChanged += v => seen.Add(v);
            Assert.That(new[] { s.CycleSpeed(), s.CycleSpeed(), s.CycleSpeed() }, Is.EqualTo(new[] { 2, 3, 1 }));
            CollectionAssert.AreEqual(new[] { 2, 3, 1 }, seen);
            Assert.Throws<ArgumentOutOfRangeException>(() => s.SetSpeed(4));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Advance(-1));
        }

        [Test]
        public void TheLoopStepsWholeSixtiethsWhateverTheFrameRate()
        {
            foreach (double fps in new[] { 24.0, 30, 60, 90, 120, 144, 240 })
            {
                var clock = new FixedStepClock();
                int steps = 0;
                for (int f = 0; f < fps * 10; f++)
                {
                    steps += clock.Advance(1 / fps);
                    Assert.That(clock.Alpha, Is.InRange(0.0, 1.0));
                }
                Assert.That(steps, Is.InRange(599, 600), $"{fps} fps: ten seconds are 600 steps");
            }
            var c = new FixedStepClock();
            Assert.That(c.Advance(1), Is.EqualTo(3), "a long frame counts as 0.05 s, like main.ts");
            Assert.That(c.Advance(double.NaN), Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => new FixedStepClock(0));
        }
    }
}
