using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CubeHaul.Domain;
using CubeHaul.Levels;
using CubeHaul.Levels.Json;
using NUnit.Framework;

namespace CubeHaul.Tests.EditMode
{
    /// <summary>Reading the golden scenarios (docs/GOLDEN_FIXTURES.md §3) and comparing a board with a recorded state.</summary>
    static class GoldenScenarios
    {
        public const string GoldenDir = "Assets/_CubeHaul/Tests/Fixtures/Golden";

        public static JsonValue Json(string path) => JsonParser.Parse(File.ReadAllText(ProjectPaths.Of(path), Encoding.UTF8));

        public static IEnumerable<string> Files() =>
            Directory.GetFiles(ProjectPaths.Of(GoldenDir + "/scenarios"), "*.json").Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal);

        public static JsonValue Load(string file) => Json(GoldenDir + "/scenarios/" + file);

        public static LevelDefinition LevelOf(JsonValue scenario)
        {
            LevelReadResult r = scenario.Has("level")
                ? LevelValidator.Validate(scenario.Get("level"))
                : LevelDefinitionReader.Read(File.ReadAllText(ProjectPaths.Of(scenario.Get("levelFile").String), Encoding.UTF8));
            Assert.That(r.Ok, Is.True, r.Describe());
            return r.Level;
        }

        public static int[] Ints(JsonValue array) => array.Items.Select(v => (int)v.Number).ToArray();

        public static int Int(JsonValue v) => (int)v.Number;

        /// <summary>Every board-level field of a recorded state: remaining, pending, claimed, emptied, reachable*, pickTarget, hashes.</summary>
        public static void CompareBoard(JsonValue state, Board board, int[] previous, bool detail, string where)
        {
            int n = board.CellCount;
            Assert.That(board.Remaining, Is.EqualTo(Int(state.Get("remaining"))), where + ": remaining");
            Assert.That(board.Pending, Is.EqualTo(Int(state.Get("pending"))), where + ": pending");
            CollectionAssert.AreEqual(Ints(state.Get("claimed")), Enumerable.Range(0, n).Where(board.IsClaimed), where + ": claimed");
            if (previous != null)
                CollectionAssert.AreEqual(Ints(state.Get("emptied")), Enumerable.Range(0, n).Where(i => previous[i] != Cells.Empty && board.Cell(i) == Cells.Empty), where + ": emptied");

            int colors = board.ColorCount;
            CollectionAssert.AreEqual(Ints(state.Get("reachableCount")), Enumerable.Range(0, colors).Select(board.ReachableCount), where + ": reachableCount");
            CollectionAssert.AreEqual(Ints(state.Get("pickTarget")), Enumerable.Range(0, colors).Select(board.PickTarget), where + ": pickTarget");
            if (detail)
            {
                IReadOnlyList<JsonValue> reachable = state.Get("reachable").Items;
                for (int c = 0; c < colors; c++) CollectionAssert.AreEqual(Ints(reachable[c]), board.ReachableCells(c), where + ": reachable " + c);
            }
            Assert.That(GoldenHash.Fnv1a(Enumerable.Range(0, colors).SelectMany(c => board.ReachableCells(c).Concat(new[] { -1 }))),
                Is.EqualTo(state.Get("reachableHash").String), where + ": reachableHash");
            Assert.That(GoldenHash.Fnv1a(BoardValues(board)), Is.EqualTo(state.Get("boardHash").String), where + ": boardHash (cells, open, claimed)");
        }

        /// <summary>The values boardHash is taken over: every cell's color, then isOpen, then isClaimed.</summary>
        public static IEnumerable<int> BoardValues(Board board)
        {
            int n = board.CellCount;
            return Enumerable.Range(0, n).Select(board.Cell)
                .Concat(Enumerable.Range(0, n).Select(i => board.IsOpen(i) ? 1 : 0))
                .Concat(Enumerable.Range(0, n).Select(i => board.IsClaimed(i) ? 1 : 0));
        }

        public static int[] CellsOf(Board board)
        {
            var cells = new int[board.CellCount];
            board.CopyCells(cells);
            return cells;
        }
    }
}
