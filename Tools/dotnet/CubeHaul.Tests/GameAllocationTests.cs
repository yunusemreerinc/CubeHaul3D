using System;
using System.Collections.Generic;
using System.IO;
using CubeHaul.Domain;
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
    }
}
