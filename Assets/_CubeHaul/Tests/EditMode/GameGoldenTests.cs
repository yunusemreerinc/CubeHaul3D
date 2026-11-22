using System;
using System.Collections.Generic;
using System.Linq;
using CubeHaul.Domain;
using CubeHaul.Levels;
using CubeHaul.Levels.Json;
using NUnit.Framework;

namespace CubeHaul.Tests.EditMode
{
    /// <summary>
    /// The C# game against the TypeScript reference: every golden scenario (docs/GOLDEN_FIXTURES.md §3) is replayed
    /// on a fresh <see cref="GameEngine"/> from its recorded tap / tick(ready) / step / collect / addSlot calls alone.
    /// Every call must return what the TypeScript Game returned (slot index, trucks sent) and leave exactly the
    /// recorded state: status, tray columns, slots, the whole board and every rule query.
    /// </summary>
    public class GameGoldenTests
    {
        static IEnumerable<string> Scenarios() => GoldenScenarios.Files();

        static GameStatus StatusOf(string s)
        {
            switch (s)
            {
                case "playing": return GameStatus.Playing;
                case "won": return GameStatus.Won;
                case "stuck": return GameStatus.Stuck;
                default: throw new ArgumentException("unknown status " + s);
            }
        }

        static int[][] Dispatches(JsonValue result) => result.Items.Select(GoldenScenarios.Ints).ToArray();

        static int[][] Dispatches(IEnumerable<Dispatch> sent) => sent.Select(d => new[] { d.Slot, d.Cell, d.Color }).ToArray();

        [TestCaseSource(nameof(Scenarios))]
        public void GameReplaysTheScenarioExactly(string file)
        {
            JsonValue scenario = GoldenScenarios.Load(file);
            GameEngine game = GoldenScenarios.LevelOf(scenario).CreateGame();
            Board board = game.Board;
            bool detail = scenario.Get("detail").Boolean;
            var sent = new List<Dispatch>();
            int[] previous = null;
            IReadOnlyList<JsonValue> steps = scenario.Get("steps").Items;
            Assert.That(steps[0].Get("op").String, Is.EqualTo("start"));
            for (int s = 0; s < steps.Count; s++)
            {
                JsonValue step = steps[s];
                string op = step.Get("op").String;
                string where = $"{file} step {s} ({op})";
                int remainingBefore = board.Remaining, pendingBefore = board.Pending;
                switch (op)
                {
                    case "start":
                        break;
                    case "tap":
                        Assert.That(game.Tap(GoldenScenarios.Int(step.Get("col"))), Is.EqualTo(GoldenScenarios.Int(step.Get("result"))), where + ": slot");
                        break;
                    case "tick":
                    {
                        JsonValue ready = step.Get("ready");
                        Func<int, bool> mask = null;
                        if (ready.Kind != JsonKind.Null)
                        {
                            bool[] flags = ready.Items.Select(v => v.Boolean).ToArray();
                            Assert.That(flags.Length, Is.EqualTo(game.State.SlotCount), where + ": ready has one entry per slot");
                            mask = slot => flags[slot];
                        }
                        game.Tick(mask, sent);
                        Assert.That(Dispatches(sent), Is.EqualTo(Dispatches(step.Get("result"))), where + ": trucks sent [slot, cell, color]");
                        // The timing contract: a tick only claims. Each cube stays on the board, claimed, until collected.
                        foreach (Dispatch d in sent)
                            Assert.That((board.Cell(d.Cell), board.IsClaimed(d.Cell)), Is.EqualTo((d.Color, true)), where + ": cell " + d.Cell + " is claimed, still there");
                        Assert.That((board.Remaining, board.Pending), Is.EqualTo((remainingBefore, pendingBefore + sent.Count)), where + ": remaining, pending");
                        break;
                    }
                    case "step":
                        game.Step(sent);
                        Assert.That(Dispatches(sent), Is.EqualTo(Dispatches(step.Get("result"))), where + ": trucks sent [slot, cell, color]");
                        foreach (Dispatch d in sent) Assert.That(board.Cell(d.Cell), Is.EqualTo(Cells.Empty), where + ": a step collects at once");
                        Assert.That((board.Remaining, board.Pending), Is.EqualTo((remainingBefore - sent.Count, pendingBefore)), where + ": remaining, pending");
                        break;
                    case "collect":
                    {
                        int cell = GoldenScenarios.Int(step.Get("cell"));
                        Assert.That(board.IsClaimed(cell), Is.True, where + ": only claimed cubes are collected in the recordings");
                        game.Collect(cell);
                        Assert.That(board.Cell(cell), Is.EqualTo(Cells.Empty), where + ": collected cube leaves the board");
                        Assert.That((board.Remaining, board.Pending), Is.EqualTo((remainingBefore - 1, pendingBefore - 1)), where + ": remaining, pending");
                        break;
                    }
                    case "addSlot":
                        game.AddSlot();
                        break;
                    default:
                        Assert.Fail(where + ": unknown op");
                        break;
                }
                CompareGame(step.Get("state"), game, previous, detail, where);
                previous = GoldenScenarios.CellsOf(board);
            }
            Assert.That(game.Status, Is.EqualTo(StatusOf(steps[steps.Count - 1].Get("state").Get("status").String)));
        }

        /// <summary>Every field of a recorded state, game and board.</summary>
        static void CompareGame(JsonValue state, GameEngine game, int[] previous, bool detail, string where)
        {
            GameState g = game.State;
            Assert.That(g.Status, Is.EqualTo(StatusOf(state.Get("status").String)), where + ": status");
            Assert.That(g.TrayCount(), Is.EqualTo(GoldenScenarios.Int(state.Get("trayCount"))), where + ": trayCount");

            IReadOnlyList<JsonValue> columns = state.Get("columns").Items;
            Assert.That(g.ColumnCount, Is.EqualTo(columns.Count), where + ": columns");
            for (int c = 0; c < columns.Count; c++)
            {
                int[] ids = Enumerable.Range(0, g.ColumnLength(c)).Select(r => g.CrateAt(c, r).Id).ToArray();
                CollectionAssert.AreEqual(GoldenScenarios.Ints(columns[c]), ids, where + ": column " + c);
                Assert.That(g.Front(c)?.Id, Is.EqualTo(ids.Length > 0 ? ids[0] : (int?)null), where + ": front " + c);
            }

            IReadOnlyList<JsonValue> slots = state.Get("slots").Items;
            Assert.That(g.SlotCount, Is.EqualTo(slots.Count), where + ": slot count");
            for (int k = 0; k < slots.Count; k++)
            {
                Crate crate = g.SlotAt(k);
                if (slots[k].Kind == JsonKind.Null) Assert.That(crate, Is.Null, where + ": slot " + k + " free");
                else
                {
                    Assert.That(crate, Is.Not.Null, where + ": slot " + k + " taken");
                    CollectionAssert.AreEqual(GoldenScenarios.Ints(slots[k]), new[] { crate.Id, crate.Color, crate.Count }, where + ": slot " + k + " [id, color, count]");
                }
            }
            CollectionAssert.AreEqual(state.Get("slotCanEat").Items.Select(v => v.Boolean), Enumerable.Range(0, g.SlotCount).Select(g.SlotCanEat), where + ": slotCanEat");
            CollectionAssert.AreEqual(state.Get("slotBlocked").Items.Select(v => v.Boolean), Enumerable.Range(0, g.SlotCount).Select(g.SlotBlocked), where + ": slotBlocked");
            Assert.That(g.IsStuck(), Is.EqualTo(state.Get("isStuck").Boolean), where + ": isStuck");
            Assert.That(g.HasFreeSlot(), Is.EqualTo(slots.Any(v => v.Kind == JsonKind.Null)), where + ": hasFreeSlot");
            for (int c = -1; c <= g.ColumnCount; c++)
                Assert.That(g.CanTap(c), Is.EqualTo(g.Status == GameStatus.Playing && g.ColumnLength(c) > 0 && g.HasFreeSlot()), where + ": canTap " + c);

            GoldenScenarios.CompareBoard(state, g.Board, previous, detail, where);
        }

        /// <summary>
        /// The acceptance check in its plainest form: the same call sequence on two fresh games, one through the
        /// list-returning API and one through the allocation-free one, gives the same results and the same state after
        /// every call.
        /// </summary>
        [TestCaseSource(nameof(Scenarios))]
        public void TheSameCallsGiveTheSameGameThroughEitherApi(string file)
        {
            JsonValue scenario = GoldenScenarios.Load(file);
            LevelDefinition level = GoldenScenarios.LevelOf(scenario);
            GameEngine a = level.CreateGame(), b = level.CreateGame();
            var buffer = new List<Dispatch>();
            foreach (JsonValue step in scenario.Get("steps").Items)
            {
                string op = step.Get("op").String;
                switch (op)
                {
                    case "tap":
                        int col = GoldenScenarios.Int(step.Get("col"));
                        Assert.That(a.Tap(col), Is.EqualTo(b.Tap(col)));
                        break;
                    case "tick":
                        JsonValue ready = step.Get("ready");
                        bool[] flags = ready.Kind == JsonKind.Null ? null : ready.Items.Select(v => v.Boolean).ToArray();
                        List<Dispatch> listed = flags == null ? a.Tick() : a.Tick(s => flags[s]);
                        b.Tick(flags == null ? (Func<int, bool>)null : s => flags[s], buffer);
                        CollectionAssert.AreEqual(listed, buffer);
                        break;
                    case "step":
                        List<Dispatch> stepped = a.Step();
                        b.Step(buffer);
                        CollectionAssert.AreEqual(stepped, buffer);
                        break;
                    case "collect":
                        int cell = GoldenScenarios.Int(step.Get("cell"));
                        a.Collect(cell);
                        b.Collect(cell);
                        break;
                    case "addSlot":
                        a.AddSlot();
                        b.AddSlot();
                        break;
                }
                Assert.That(GameRuleTests.Snapshot(a), Is.EqualTo(GameRuleTests.Snapshot(b)), op);
            }
        }

        [Test]
        public void EveryOpKindIsCovered()
        {
            var ops = new HashSet<string>();
            int maskedTicks = 0;
            foreach (string file in Scenarios())
            foreach (JsonValue step in GoldenScenarios.Load(file).Get("steps").Items)
            {
                ops.Add(step.Get("op").String);
                if (step.Get("op").String == "tick" && step.Get("ready").Kind != JsonKind.Null) maskedTicks++;
            }
            CollectionAssert.AreEquivalent(new[] { "start", "tap", "tick", "step", "collect", "addSlot" }, ops);
            Assert.That(maskedTicks, Is.GreaterThan(0));
            Assert.That(Scenarios().Count(), Is.EqualTo(10));
        }
    }
}
