using System;
using System.Collections.Generic;
using System.Linq;
using CubeHaul.Domain;
using CubeHaul.Levels;
using NUnit.Framework;

namespace CubeHaul.Tests.EditMode
{
    /// <summary>
    /// Seeded random play on the ten levels, checking every invariant after every call (as
    /// Tools/ts-reference/tests/invariants.test.ts): random taps, ticks with random slots held back, cubes collected in
    /// random order (trucks arriving out of order), and +1 Slot when stuck.
    /// </summary>
    public class GameInvariantTests
    {
        const int Ops = 1500;

        static IEnumerable<string> LevelIds() =>
            LevelRepository.LoadDirectory(ProjectPaths.Of("Assets/_CubeHaul/Levels/Content")).ThrowIfErrors().LevelIds;

        [TestCaseSource(nameof(LevelIds))]
        public void InvariantsHoldUnderRandomPlay(string levelId)
        {
            LevelDefinition level = LevelRepository.LoadDirectory(ProjectPaths.Of("Assets/_CubeHaul/Levels/Content")).ThrowIfErrors().Get(levelId);
            foreach (int seed in new[] { 1, 2 })
            {
                var rng = new Random(1000 * level.LevelId.Length + seed);
                GameEngine g = level.CreateGame();
                var outstanding = new List<int>();
                var sent = new List<Dispatch>();
                var everSent = new HashSet<int>();
                for (int op = 0; op < Ops && g.Status != GameStatus.Won; op++)
                {
                    double r = rng.NextDouble();
                    string what;
                    if (g.Status == GameStatus.Stuck)
                    {
                        if (g.State.SlotCount >= 6) break;
                        g.AddSlot();
                        what = "addSlot";
                    }
                    else if (r < 0.25 && g.State.HasFreeSlot())
                    {
                        int[] cols = Enumerable.Range(0, g.State.ColumnCount).Where(c => g.State.ColumnLength(c) > 0).ToArray();
                        what = "tap";
                        if (cols.Length > 0)
                        {
                            int free = g.State.FirstFreeSlot();
                            Assert.That(g.Tap(cols[rng.Next(cols.Length)]), Is.EqualTo(free), "I-12 the lowest free slot");
                        }
                    }
                    else if (r < 0.6)
                    {
                        bool[] mask = Enumerable.Range(0, g.State.SlotCount).Select(_ => rng.NextDouble() < 0.7).ToArray();
                        g.Tick(s => mask[s], sent);
                        what = "tick";
                        for (int k = 0; k < sent.Count; k++)
                        {
                            Dispatch d = sent[k];
                            Assert.That(mask[d.Slot], Is.True, "a held-back slot sent a truck");
                            if (k > 0) Assert.That(d.Slot, Is.GreaterThan(sent[k - 1].Slot), "I-13 one truck per slot, in slot order");
                            Assert.That(everSent.Add(d.Cell), Is.True, "cell " + d.Cell + " sent twice");
                            Assert.That((g.Board.Cell(d.Cell), g.Board.IsClaimed(d.Cell)), Is.EqualTo((d.Color, true)), "a tick only claims");
                            outstanding.Add(d.Cell);
                        }
                    }
                    else if (outstanding.Count > 0)
                    {
                        int k = rng.Next(outstanding.Count);
                        g.Collect(outstanding[k]);
                        outstanding.RemoveAt(k);
                        what = "collect";
                    }
                    else
                    {
                        g.Tick(null, sent);
                        foreach (Dispatch d in sent)
                        {
                            Assert.That(everSent.Add(d.Cell), Is.True);
                            outstanding.Add(d.Cell);
                        }
                        what = "tick";
                    }
                    CollectionAssert.IsEmpty(GameInvariants.Violations(g), $"{levelId} seed {seed} op {op} ({what})");
                    if (op % 100 == 0) AssertDistancesAreFresh(g.Board);
                }
                foreach (int cell in outstanding)
                {
                    g.Collect(cell);
                    CollectionAssert.IsEmpty(GameInvariants.Violations(g), $"{levelId} seed {seed} final collect");
                }
                Assert.That(g.Board.Pending, Is.EqualTo(0));
            }
        }

        /// <summary>I-09: the incrementally kept depth/road of every open cell equal a fresh computation.</summary>
        static void AssertDistancesAreFresh(Board b)
        {
            var cells = new int[b.CellCount];
            b.CopyCells(cells);
            var fresh = new Board(b.Width, b.Height, cells, b.ColorCount, b.Edges);
            for (int i = 0; i < b.CellCount; i++)
                if (b.IsOpen(i))
                    Assert.That((b.DepthAt(i), b.RoadAt(i)), Is.EqualTo((fresh.DepthAt(i), fresh.RoadAt(i))), "I-09 cell " + i);
        }
    }
}
