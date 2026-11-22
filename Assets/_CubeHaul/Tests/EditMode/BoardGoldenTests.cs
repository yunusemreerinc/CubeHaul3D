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
    /// <summary>
    /// The C# Board against the TypeScript reference (docs/GOLDEN_FIXTURES.md). Every golden scenario is replayed at
    /// the board level: each truck the TypeScript game sent must be the cube C# PickTarget chooses at that moment, and
    /// after every operation the whole board state must match the recorded snapshot.
    /// </summary>
    public class BoardGoldenTests
    {
        static JsonValue Json(string path) => GoldenScenarios.Json(path);
        static IEnumerable<string> Scenarios() => GoldenScenarios.Files();
        static int[] Ints(JsonValue array) => GoldenScenarios.Ints(array);
        const string GoldenDir = GoldenScenarios.GoldenDir;

        [TestCaseSource(nameof(Scenarios))]
        public void BoardReplaysTheScenarioExactly(string file)
        {
            JsonValue scenario = GoldenScenarios.Load(file);
            Board board = GoldenScenarios.LevelOf(scenario).CreateBoard();
            bool detail = scenario.Get("detail").Boolean;
            int[] previous = null;
            IReadOnlyList<JsonValue> steps = scenario.Get("steps").Items;
            for (int s = 0; s < steps.Count; s++)
            {
                JsonValue step = steps[s];
                string op = step.Get("op").String;
                string where = $"{file} step {s} ({op})";
                if (op == "tick" || op == "step")
                {
                    var sent = new List<int>();
                    foreach (JsonValue d in step.Get("result").Items)
                    {
                        int cell = (int)d.Items[1].Number;
                        int color = (int)d.Items[2].Number;
                        Assert.That(board.PickTarget(color), Is.EqualTo(cell), where + ": the cube a truck of color " + color + " takes");
                        board.Claim(cell);
                        sent.Add(cell);
                    }
                    if (op == "step")
                        foreach (int cell in sent) board.Eat(cell);
                }
                else if (op == "collect") board.Eat((int)step.Get("cell").Number);

                Compare(step.Get("state"), board, previous, detail, where);
                previous = GoldenScenarios.CellsOf(board);
            }
        }

        static void Compare(JsonValue state, Board board, int[] previous, bool detail, string where)
        {
            GoldenScenarios.CompareBoard(state, board, previous, detail, where);

            // slotCanEat and slotBlocked depend only on the board and the slot's color: they check ReachableCount and OpensUp.
            IReadOnlyList<JsonValue> slots = state.Get("slots").Items;
            IReadOnlyList<JsonValue> canEat = state.Get("slotCanEat").Items;
            IReadOnlyList<JsonValue> blocked = state.Get("slotBlocked").Items;
            for (int k = 0; k < slots.Count; k++)
            {
                if (slots[k].Kind == JsonKind.Null) continue;
                int color = (int)slots[k].Items[1].Number;
                Assert.That(board.ReachableCount(color) > 0, Is.EqualTo(canEat[k].Boolean), where + ": slotCanEat " + k);
                Assert.That(board.ReachableCount(color) == 0 && !board.OpensUp(color), Is.EqualTo(blocked[k].Boolean), where + ": slotBlocked (OpensUp) " + k);
            }
        }

        [Test]
        public void PeelLayersOfTheTenLevelsMatch()
        {
            JsonValue peel = Json(GoldenDir + "/peel-layers.json");
            LevelRepository repo = LevelRepository.LoadDirectory(ProjectPaths.Of("Assets/_CubeHaul/Levels/Content")).ThrowIfErrors();
            Assert.That(peel.Get("levels").Items.Count, Is.EqualTo(10));
            foreach (JsonValue entry in peel.Get("levels").Items)
            {
                string id = entry.Get("levelId").String;
                LevelDefinition level = repo.Get(id);
                Assert.That(OpenEdgesJson.ToJson(level.Edges), Is.EqualTo(entry.Get("edges").String), id);
                CollectionAssert.AreEqual(Ints(entry.Get("layers")), PeelLayers(level.CreateBoard()), id + ": peel layers");
            }
        }

        /// <summary>cube-hunt peelLayers: eat every reachable cube, one full ring at a time.</summary>
        static int[] PeelLayers(Board board)
        {
            var layer = Enumerable.Repeat(-1, board.CellCount).ToArray();
            for (int l = 0; board.Remaining > 0; l++)
            {
                int[] ring = Enumerable.Range(0, board.ColorCount).SelectMany(board.ReachableCells).ToArray();
                Assert.That(ring, Is.Not.Empty, "cubes left but none reachable");
                foreach (int i in ring)
                {
                    layer[i] = l;
                    board.Eat(i);
                }
            }
            return layer;
        }
    }
}
