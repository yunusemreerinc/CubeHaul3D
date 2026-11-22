using System;
using System.Collections.Generic;
using System.Linq;
using CubeHaul.Domain;
using NUnit.Framework;

namespace CubeHaul.Tests.EditMode
{
    /// <summary>
    /// cube-hunt's board tests (tests/core.test.ts, Tools/ts-reference/tests/board.edge.test.ts) on the C# Board, with
    /// the same pictures and the same expected values.
    /// </summary>
    public class BoardRuleTests
    {
        /// <summary>Rows of characters: '.' empty, a digit is that color; row 0 is the top.</summary>
        static Board Pic(OpenEdges edges, params string[] rows)
        {
            int w = rows[0].Length;
            int[] cells = rows.SelectMany(r => r.Select(ch => ch == '.' ? Cells.Empty : ch - '0')).ToArray();
            int colors = cells.Length == 0 ? 0 : Math.Max(1, cells.Max() + 1);
            return new Board(w, rows.Length, cells, colors, edges);
        }

        static Board Solid(int w, int h, OpenEdges edges) => Pic(edges, Enumerable.Repeat(new string('0', w), h).ToArray());

        static List<int> ClaimAll(Board b, int color, int max = int.MaxValue)
        {
            var order = new List<int>();
            for (int t = b.PickTarget(color); t >= 0 && order.Count < max; t = b.PickTarget(color))
            {
                order.Add(t);
                b.Claim(t);
            }
            return order;
        }

        static List<int> EatAll(Board b, int color)
        {
            var order = new List<int>();
            for (int t = b.PickTarget(color); t >= 0; t = b.PickTarget(color))
            {
                order.Add(t);
                b.Eat(t);
            }
            return order;
        }

        // ---- core.test.ts ----

        [Test]
        public void OnlyExposesCellsOnTheEdgeOrNextToEatenCells()
        {
            Board b = Pic(OpenEdges.All, "000", "010", "000");
            Assert.That(b.ReachableCount(0), Is.EqualTo(8));
            Assert.That(b.ReachableCount(1), Is.EqualTo(0));
            int t = b.PickTarget(0);
            Assert.That(t, Is.EqualTo(7), "on the edge and nearest the ramp = bottom middle");
            b.Eat(t);
            Assert.That(b.ReachableCount(1), Is.EqualTo(1));
        }

        [Test]
        public void RefusesToEatAnUnreachableCell()
        {
            var e = Assert.Throws<InvalidOperationException>(() => Pic(OpenEdges.All, "000", "010", "000").Eat(4));
            Assert.That(e.Message, Is.EqualTo("cell 4 is not reachable"));
        }

        [TestCase(OpenEdges.Bottom, 3)]
        [TestCase(OpenEdges.Sides, 7)]
        [TestCase(OpenEdges.All, 8)]
        public void ReachableAtTheStartOfASolid3x3(OpenEdges edges, int count)
        {
            Assert.That(Solid(3, 3, edges).ReachableCount(0), Is.EqualTo(count));
        }

        [Test]
        public void ASealedGapOpensOnlyOnceTrucksCanReachIt()
        {
            Board b = Pic(OpenEdges.Bottom, "000", "0.0", "000", "000");
            Assert.That(b.IsOpen(4), Is.False);
            CollectionAssert.AreEqual(new[] { 9, 10, 11 }, b.ReachableCells(0));
            b.Eat(10);
            b.Eat(7);
            Assert.That(b.IsOpen(4), Is.True);
            CollectionAssert.IsSubsetOf(new[] { 1, 3, 5 }, b.ReachableCells(0));
        }

        [Test]
        public void ClaimedCubesAreNeverOfferedToASecondTruck()
        {
            Board b = Solid(2, 2, OpenEdges.Bottom);
            b.Claim(2);
            b.Claim(3);
            b.Eat(2);
            CollectionAssert.AreEqual(new[] { 0 }, b.ReachableCells(0));
            Assert.That(b.IsClaimed(3), Is.True);
        }

        [Test]
        public void TakesACubeNextToTheRoadBeforeANearerOneFurtherIn()
        {
            Board b = Solid(9, 4, OpenEdges.Bottom);
            foreach (int x in new[] { 4, 3, 5, 2, 6 }) b.Eat(27 + x);
            b.Eat(22);
            b.Eat(13);
            Assert.That(b.PickTarget(0), Is.EqualTo(27 + 1));
        }

        [Test]
        public void AmongCubesNextToTheRoadTakesTheShortestDriveThenTheLowerIndex()
        {
            Board b = Solid(7, 1, OpenEdges.Bottom);
            Assert.That(b.PickTarget(0), Is.EqualTo(3));
            b.Eat(3);
            Assert.That(b.PickTarget(0), Is.EqualTo(2));
        }

        // ---- board.edge.test.ts ----

        [Test]
        public void ClaimTakesACubeOffTheReachableListButLeavesItOnTheBoard()
        {
            Board b = Solid(3, 1, OpenEdges.Bottom);
            b.Claim(1);
            Assert.That(b.IsClaimed(1), Is.True);
            Assert.That((b.Pending, b.Remaining, b.RemainingOf(0), b.ReachableCount(0), b.Cell(1)), Is.EqualTo((1, 3, 3, 2, 0)));
        }

        [Test]
        public void AClaimedCubeIsNeverPickedAgain()
        {
            CollectionAssert.AreEqual(new[] { 1, 0, 2 }, ClaimAll(Solid(3, 1, OpenEdges.Bottom), 0));
        }

        [Test]
        public void ClaimingTwiceOrSomethingUnreachableAndEatingTwiceThrow()
        {
            Board b = Solid(1, 2, OpenEdges.Bottom);
            b.Claim(1);
            Assert.That(Assert.Throws<InvalidOperationException>(() => b.Claim(1)).Message, Is.EqualTo("cell 1 is not reachable"));
            Assert.That(Assert.Throws<InvalidOperationException>(() => b.Claim(0)).Message, Is.EqualTo("cell 0 is not reachable"));
            b.Eat(1);
            Assert.That((b.Pending, b.IsClaimed(1), b.Remaining), Is.EqualTo((0, false, 1)));
            Assert.That(Assert.Throws<InvalidOperationException>(() => b.Eat(1)).Message, Is.EqualTo("cell 1 is already empty"));
            Assert.That(Assert.Throws<InvalidOperationException>(() => b.Claim(1)).Message, Is.EqualTo("cell 1 is not reachable"));
        }

        [Test]
        public void TheCubeBehindAClaimedCubeStaysUnreachable()
        {
            Board b = Solid(1, 3, OpenEdges.Bottom);
            b.Claim(2);
            Assert.That(b.ReachableCount(0), Is.EqualTo(0));
            Assert.That(b.IsOpen(2), Is.False);
            b.Eat(2);
            Assert.That(b.IsOpen(2), Is.True);
            CollectionAssert.AreEqual(new[] { 1 }, b.ReachableCells(0));
        }

        [Test]
        public void ASealedGapBehindAClaimedCubeOpensOnlyOnCollect()
        {
            Board b = Pic(OpenEdges.Bottom, "0", ".", "0");
            Assert.That(b.IsOpen(1), Is.False);
            b.Claim(2);
            Assert.That(b.IsOpen(1), Is.False);
            Assert.That(b.ReachableCount(0), Is.EqualTo(0));
            Assert.That(b.OpensUp(0), Is.True, "the truck that is out will uncover it");
            b.Eat(2);
            Assert.That(b.IsOpen(1), Is.True);
            CollectionAssert.AreEqual(new[] { 0 }, b.ReachableCells(0));
        }

        [Test]
        public void OpensUpIsFalseWithNoTruckOutAndSeesOnlyOneCubeDeep()
        {
            Board b = Pic(OpenEdges.Bottom, "2", "1", "0");
            Assert.That(b.OpensUp(1), Is.False);
            b.Claim(2);
            Assert.That(b.OpensUp(1), Is.True);
            Assert.That(b.OpensUp(2), Is.False, "color 1 is still in the way");
            Assert.That(b.OpensUp(0), Is.False, "the only color-0 cube is the claimed one");
        }

        [TestCase(OpenEdges.Bottom, SideMask.Bottom, SideMask.None, SideMask.None, SideMask.Bottom, SideMask.None)]
        [TestCase(OpenEdges.Sides, SideMask.Bottom | SideMask.Left, SideMask.Left, SideMask.Right, SideMask.Bottom | SideMask.Right, SideMask.None)]
        [TestCase(OpenEdges.All, SideMask.Bottom | SideMask.Left, SideMask.Left | SideMask.Top, SideMask.Right | SideMask.Top, SideMask.Bottom | SideMask.Right, SideMask.Top)]
        public void OpenSidesOfTheCorners(OpenEdges edges, SideMask bottomLeft, SideMask topLeft, SideMask topRight, SideMask bottomRight, SideMask topMiddle)
        {
            Board b = Solid(3, 3, edges);
            Assert.That(new[] { b.OpenSides(6), b.OpenSides(0), b.OpenSides(2), b.OpenSides(8), b.OpenSides(1) },
                Is.EqualTo(new[] { bottomLeft, topLeft, topRight, bottomRight, topMiddle }));
        }

        [TestCase(OpenEdges.Bottom, new[] { 6, 7, 8 })]
        [TestCase(OpenEdges.Sides, new[] { 0, 2, 3, 5, 6, 7, 8 })]
        [TestCase(OpenEdges.All, new[] { 0, 1, 2, 3, 5, 6, 7, 8 })]
        public void ReachableCubesOfASolid3x3(OpenEdges edges, int[] expected)
        {
            CollectionAssert.AreEqual(expected, Solid(3, 3, edges).ReachableCells(0));
        }

        [TestCase(OpenEdges.Bottom, new[] { 2, 2, 2, 1, 1, 1, 0, 0, 0 })]
        [TestCase(OpenEdges.Sides, new[] { 0, 1, 0, 0, 1, 0, 0, 0, 0 })]
        [TestCase(OpenEdges.All, new[] { 0, 0, 0, 0, 1, 0, 0, 0, 0 })]
        public void PeelLayersOfASolid3x3(OpenEdges edges, int[] expected)
        {
            Board b = Solid(3, 3, edges);
            var layer = new int[9];
            for (int l = 0; b.Remaining > 0; l++)
                foreach (int i in b.ReachableCells(0))
                {
                    layer[i] = l;
                    b.Eat(i);
                }
            CollectionAssert.AreEqual(expected, layer);
        }

        [Test]
        public void AWallMakesTheSameColorReachableOnlyFromTheSidesOrAllRound()
        {
            Assert.That(Pic(OpenEdges.Bottom, "000", "000", "111").ReachableCount(0), Is.EqualTo(0));
            Assert.That(Pic(OpenEdges.Sides, "000", "000", "111").ReachableCount(0), Is.EqualTo(4));
            Assert.That(Pic(OpenEdges.All, "000", "000", "111").ReachableCount(0), Is.EqualTo(5));
        }

        [Test]
        public void AllEdgesATopRowCornerIsReachedByTheSideRoad()
        {
            Board all = Pic(OpenEdges.All, "000", "111", "111");
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, all.ReachableCells(0));
            CollectionAssert.AreEqual(new[] { 0, 2, 1 }, ClaimAll(all, 0));
            CollectionAssert.AreEqual(new[] { 0, 2 }, Pic(OpenEdges.Sides, "000", "111", "111").ReachableCells(0));
        }

        [TestCase(OpenEdges.Bottom)]
        [TestCase(OpenEdges.Sides)]
        [TestCase(OpenEdges.All)]
        public void TheBottomEdgeIsAlwaysOpen(OpenEdges edges)
        {
            Assert.That(Solid(5, 3, edges).PickTarget(0), Is.EqualTo(12));
        }

        [TestCase(OpenEdges.Bottom, new[] { 2, 1, 3, 0, 4 })]
        [TestCase(OpenEdges.Sides, new[] { 2, 1, 3, 0, 4 })]
        [TestCase(OpenEdges.All, new[] { 0, 4, 1, 3, 2 })]
        public void ClearingOrderOfASolid5x3(OpenEdges edges, int[] topRow)
        {
            int[] lower = { 12, 11, 13, 10, 14, 7, 6, 8, 5, 9 };
            CollectionAssert.AreEqual(lower.Concat(topRow), EatAll(Solid(5, 3, edges), 0));
        }

        [Test]
        public void TheWayInIsChosenByFewestCellsInButCubesAreComparedByTotalDrive()
        {
            // I-08 (TS_BASELINE §3.3): (2,1) goes before the side cube (0,1).
            Board b = Pic(OpenEdges.Sides, "00000", "0.000", "..111");
            CollectionAssert.AreEqual(new[] { 7, 5, 9 }, ClaimAll(b, 0, 3));
        }

        // ---- C# specifics ----

        [Test]
        public void IncrementalDistancesEqualAFreshComputation()
        {
            // I-09: after every pickup, depth/road of every open cell equal those of a Board built from scratch.
            Board b = Pic(OpenEdges.All, "0000000", "0.1.1.0", "0011100", "01.0.10", "0000000");
            var cells = new int[b.CellCount];
            for (int guard = 0; b.Remaining > 0 && guard < 100; guard++)
            {
                int t = b.ReachableCount(0) > 0 ? b.PickTarget(0) : b.PickTarget(1);
                b.Eat(t);
                b.CopyCells(cells);
                var fresh = new Board(b.Width, b.Height, cells, 2, OpenEdges.All);
                for (int i = 0; i < b.CellCount; i++)
                {
                    if (!b.IsOpen(i)) continue;
                    Assert.That((b.DepthAt(i), b.RoadAt(i)), Is.EqualTo((fresh.DepthAt(i), fresh.RoadAt(i))), "cell " + i);
                }
            }
            Assert.That(b.Remaining, Is.EqualTo(0));
        }

        [Test]
        public void TheBoardKeepsItsOwnCopyOfTheCells()
        {
            int[] cells = { 0, 0, 0 };
            var b = new Board(3, 1, cells, 1, OpenEdges.Bottom);
            cells[1] = Cells.Empty;
            Assert.That(b.Cell(1), Is.EqualTo(0));
        }

        [Test]
        public void BadInputIsRejectedAtConstruction()
        {
            Assert.Throws<ArgumentException>(() => new Board(2, 2, new int[3], 1));
            Assert.Throws<ArgumentException>(() => new Board(2, 1, new[] { 0, 1 }, 1));
            Assert.Throws<ArgumentException>(() => new Board(2, 1, new[] { 0, -2 }, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Board(0, 1, new int[0], 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Board(1, 1, new[] { 0 }, 1, (OpenEdges)9));
        }

        [Test]
        public void ColorsOutsideThePaletteHaveNothing()
        {
            Board b = Solid(2, 1, OpenEdges.Bottom);
            Assert.That((b.ReachableCount(5), b.RemainingOf(-1), b.ReachableCells(3).Length, b.OpensUp(7)), Is.EqualTo((0, 0, 0, false)));
            Assert.Throws<ArgumentOutOfRangeException>(() => b.PickTarget(1));
        }
    }
}
