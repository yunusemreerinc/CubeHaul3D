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
    /// After construction the Board's hot paths allocate nothing (mobile GC budget, PORTING_PLAN R-11). Checked here
    /// rather than in Unity: GC.GetAllocatedBytesForCurrentThread is not in Unity's .NET profile.
    /// </summary>
    public class BoardAllocationTests
    {
        static LevelRepository repo;

        [OneTimeSetUp]
        public void Load()
        {
            string dir = Path.Combine(Path.GetDirectoryName(Fixtures.Root), "Assets", "_CubeHaul", "Levels", "Content");
            repo = LevelRepository.LoadDirectory(dir).ThrowIfErrors();
        }

        /// <summary>Clears the whole board the way a game does: every color picks and claims, OpensUp per color, trucks collect late.</summary>
        static void PlayThrough(Board b, int[] claimedBuffer)
        {
            int colors = b.ColorCount;
            while (b.Remaining > 0)
            {
                int claimed = 0;
                for (int c = 0; c < colors; c++)
                {
                    b.ReachableCount(c);
                    b.OpensUp(c);
                    int t = b.PickTarget(c);
                    if (t < 0) continue;
                    b.Claim(t);
                    claimedBuffer[claimed++] = t;
                }
                for (int c = 0; c < colors; c++) b.OpensUp(c);
                for (int k = 0; k < claimed; k++) b.Eat(claimedBuffer[k]);
            }
        }

        [TestCase("strawberry")]
        [TestCase("ringed-planet")]
        [TestCase("cat")]
        public void ClearingALevelAllocatesNothingAfterConstruction(string levelId)
        {
            LevelDefinition level = repo.Get(levelId);
            var buffer = new int[64];
            PlayThrough(level.CreateBoard(), buffer); // warm up the JIT

            Board board = level.CreateBoard();
            long before = GC.GetAllocatedBytesForCurrentThread();
            PlayThrough(board, buffer);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(board.Remaining, Is.EqualTo(0));
            Assert.That(allocated, Is.EqualTo(0), $"{levelId}: {allocated} bytes allocated while clearing the board");
        }

        [Test]
        public void ABoardForTheLargestLevelStaysSmall()
        {
            LevelDefinition planet = repo.Get("ringed-planet"); // 46 x 46 = 2116 cells
            long before = GC.GetAllocatedBytesForCurrentThread();
            Board b = planet.CreateBoard();
            long size = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(b.CellCount, Is.EqualTo(2116));
            Assert.That(size, Is.LessThan(200_000), size + " bytes");
            TestContext.WriteLine($"Board for {b.Width}x{b.Height}: {size:N0} bytes");
        }
    }
}
