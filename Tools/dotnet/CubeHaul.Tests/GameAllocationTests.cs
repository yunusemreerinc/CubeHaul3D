using System;
using System.Collections.Generic;
using System.IO;
using CubeHaul.Domain;
using CubeHaul.Gameplay.Grid;
using CubeHaul.Gameplay.Roads;
using CubeHaul.Gameplay.Session;
using CubeHaul.Gameplay.Trucks;
using CubeHaul.Levels;
using CubeHaul.Tests.LevelFormat;
using NUnit.Framework;

namespace CubeHaul.Tests.Domain
{
    /// <summary>
    /// The calls a view makes every frame (Tick with a cached ready delegate and a reused list, Collect, Tap, the slot
    /// queries) allocate nothing once the game is built (PORTING_PLAN R-11).
    /// </summary>
    public class GameAllocationTests
    {
        static LevelRepository repo;

        [OneTimeSetUp]
        public void Load()
        {
            string dir = Path.Combine(Path.GetDirectoryName(Fixtures.Root), "Assets", "_CubeHaul", "Levels", "Content");
            repo = LevelRepository.LoadDirectory(dir).ThrowIfErrors();
        }

        sealed class Player
        {
            readonly GameEngine game;
            readonly List<Dispatch> sent = new List<Dispatch>(64);
            readonly int[] outstanding = new int[4096];
            readonly Func<int, bool> ready;
            int count, frame;

            public Player(GameEngine game)
            {
                this.game = game;
                ready = slot => ((frame + slot) & 1) == 0; // hold every other slot back each frame
            }

            /// <summary>Plays like a view: taps when a slot is free, ticks, collects the oldest truck a frame later.</summary>
            public void Play()
            {
                GameState s = game.State;
                for (int guard = 0; game.Status != GameStatus.Won && guard < 100_000; guard++, frame++)
                {
                    if (game.Status == GameStatus.Stuck) game.AddSlot();
                    for (int c = 0; c < s.ColumnCount && s.HasFreeSlot(); c++)
                        if (s.CanTap(c)) game.Tap(c);
                    for (int k = 0; k < s.SlotCount; k++)
                    {
                        s.SlotCanEat(k);
                        s.SlotBlocked(k);
                    }
                    int before = count;
                    game.Tick(ready, sent);
                    for (int k = 0; k < sent.Count; k++) outstanding[count++] = sent[k].Cell;
                    for (int k = 0; k < before; k++) game.Collect(outstanding[k]);
                    Array.Copy(outstanding, before, outstanding, 0, count - before);
                    count -= before;
                    s.TrayCount();
                    s.IsStuck();
                }
            }
        }

        [TestCase("strawberry")]
        [TestCase("ringed-planet")]
        [TestCase("watermelon")]
        public void PlayingALevelAllocatesNothingAfterConstruction(string levelId)
        {
            LevelDefinition level = repo.Get(levelId);
            new Player(level.CreateGame()).Play(); // warm up the JIT

            GameEngine game = level.CreateGame();
            var player = new Player(game);
            game.AddSlot(); // the slot list's one growth happens here, before measuring (capacity is at least 6)
            long before = GC.GetAllocatedBytesForCurrentThread();
            player.Play();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(game.Status, Is.EqualTo(GameStatus.Won));
            Assert.That(allocated, Is.EqualTo(0), $"{levelId}: {allocated} bytes allocated while playing");
        }

        [TestCase("strawberry")]
        [TestCase("ringed-planet")]
        public void FollowingTheBoardWithTheCubeFieldAllocatesNothing(string levelId)
        {
            LevelDefinition level = repo.Get(levelId);
            var changed = new List<int>(level.Picture.Cells.Length);
            var sent = new List<Dispatch>(64);
            void Play(GameEngine g, CubeField f)
            {
                for (int guard = 0; g.Status == GameStatus.Playing && guard < 100_000; guard++)
                {
                    for (int c = 0; c < g.State.ColumnCount && g.State.HasFreeSlot(); c++) g.Tap(c);
                    g.Tick(null, sent);
                    changed.Clear();
                    f.Sync(g.Board, changed); // claims
                    for (int k = 0; k < sent.Count; k++) g.Collect(sent[k].Cell);
                    changed.Clear();
                    f.Sync(g.Board, changed); // collects
                    if (g.Status == GameStatus.Stuck) g.AddSlot();
                }
            }
            Play(level.CreateGame(), new CubeField(level.Picture)); // warm up

            GameEngine game = level.CreateGame();
            var field = new CubeField(level.Picture);
            game.AddSlot(); // the slot list's one growth, before measuring
            long before = GC.GetAllocatedBytesForCurrentThread();
            Play(game, field);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(game.Status, Is.EqualTo(GameStatus.Won));
            Assert.That(field.VisibleCount, Is.EqualTo(0));
            Assert.That(allocated, Is.EqualTo(0), $"{levelId}: {allocated} bytes allocated while following the board");
        }

        /// <summary>A truck moving every frame allocates nothing (GC.GetAllocatedBytesForCurrentThread is not in Unity's profile).</summary>
        [Test]
        public void MovingATruckAllocatesNothing()
        {
            Board board = repo.Get("strawberry").CreateBoard();
            for (int r = 0; r < 6; r++)
                for (int c = 0; c < board.ColorCount; c++)
                {
                    int t = board.PickTarget(c);
                    if (t >= 0) board.Eat(t);
                }
            RoadPathPlanner roads = RoadPathPlanner.ForBoard(board);
            int cell = board.PickTarget(0);
            var truck = new TruckMotion(roads.PlanTruck(cell), roads.Center(cell), roads.CellSize, roads.Ring);
            truck.Advance(1 / 60.0);
            long before = GC.GetAllocatedBytesForCurrentThread();
            double sink = 0;
            while (truck.State != TruckState.Gone)
            {
                truck.Advance(1 / 60.0);
                sink += truck.Pose().X + truck.Fade + truck.DrawScale;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(sink, Is.Not.NaN);
            Assert.That(allocated, Is.EqualTo(0), allocated + " bytes allocated while driving");
        }

        /// <summary>
        /// A whole level through the session loop at 1x and 3x: a frame in which no truck is sent (driving, loading,
        /// returning, cooldowns, ramp lanes, crate flights, the auto player) allocates nothing. Sending a truck allocates
        /// its motion and planned route only.
        /// </summary>
        [TestCase("strawberry", 1)]
        [TestCase("ringed-planet", 3)]
        public void ASessionFrameWithoutNewTrucksAllocatesNothing(string levelId, int speed)
        {
            var loop = new SessionLoop(GameSession.For(repo.Get(levelId))) { AutoPlay = true };
            loop.Session.SetSpeed(speed);
            int sent = 0;
            loop.Session.TruckSent += _ => sent++;
            loop.Frame(1 / 60.0);
            long quiet = 0, quietFrames = 0, withTrucks = 0;
            for (int f = 0; f < 100_000 && loop.Outcome == AutoPlayOutcome.Playing; f++)
            {
                int before = sent;
                long bytes = GC.GetAllocatedBytesForCurrentThread();
                loop.Frame(1 / 60.0);
                bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
                if (sent == before)
                {
                    quiet += bytes;
                    quietFrames++;
                }
                else withTrucks += bytes;
            }
            Assert.That(loop.Outcome, Is.EqualTo(AutoPlayOutcome.Won));
            Assert.That(quietFrames, Is.GreaterThan(100));
            Assert.That(quiet, Is.EqualTo(0), $"{quiet} bytes in {quietFrames} frames without new trucks");
            Assert.That(withTrucks / (double)sent, Is.LessThan(4096), "bytes per truck sent");
        }
    }
}
