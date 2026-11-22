using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CubeHaul.Domain;
using CubeHaul.Levels;
using CubeHaul.Levels.Generation;

namespace CubeHaul.Tests.EditMode.Parity
{
    /// <summary>Where C# first disagreed with TypeScript.</summary>
    sealed class ParityDivergence
    {
        /// <summary>Step index (0 = the start); -1 = setting up the level and tray.</summary>
        public int Step { get; set; }
        public string Call { get; set; }
        public string PreviousCall { get; set; }
        public string Field { get; set; }
        public string Expected { get; set; }
        public string Actual { get; set; }
        /// <summary>Other fields that also differ at that step.</summary>
        public List<string> AlsoDifferent { get; } = new List<string>();
    }

    sealed class ParityResult
    {
        public ParityScenario Scenario { get; set; }
        public int StepsMatched { get; set; }
        public ParityDivergence Divergence { get; set; }
        public string ActualOutcome { get; set; }
        /// <summary>fnv1a over every actual snapshot: equal for two runs of the same scenario.</summary>
        public string TraceHash { get; set; }
        public bool Ok => Divergence == null;
    }

    /// <summary>Replays a parity scenario on the C# rules and compares every value after every call.</summary>
    static class ParityRunner
    {
        public static ParityResult Run(ParityScenario s)
        {
            var result = new ParityResult { Scenario = s };
            GameEngine game;
            try
            {
                game = Setup(s, out string traySha);
                if (traySha != s.TraySha256)
                {
                    result.Divergence = new ParityDivergence
                    {
                        Step = -1, Call = "setup: tray from " + JsonText(s), PreviousCall = "-", Field = "traySha256",
                        Expected = s.TraySha256, Actual = traySha,
                    };
                    return result;
                }
            }
            catch (Exception e)
            {
                result.Divergence = new ParityDivergence { Step = -1, Call = "setup", PreviousCall = "-", Field = "exception", Expected = "no exception", Actual = e.GetType().Name + ": " + e.Message };
                return result;
            }

            var trace = new StringBuilder();
            var sent = new List<Dispatch>();
            int[] previous = Cells(game.Board);
            for (int k = 0; k < s.Steps.Count; k++)
            {
                ParityStep step = s.Steps[k];
                string previousCall = k == 0 ? "-" : $"step {k - 1}: {s.Steps[k - 1].Describe()}";
                var actualOp = new Dictionary<string, string>();
                try
                {
                    switch (step.Op)
                    {
                        case "start": break;
                        case "tap": actualOp["result"] = game.Tap(step.Col.Value).ToString(); break;
                        case "tick":
                            bool[] ready = step.Ready;
                            game.Tick(ready == null ? (Func<int, bool>)null : slot => slot < ready.Length && ready[slot], sent);
                            actualOp["sent"] = Dispatches(sent);
                            break;
                        case "step":
                            game.Step(sent);
                            actualOp["sent"] = Dispatches(sent);
                            break;
                        case "collect": game.Collect(step.Cell.Value); break;
                        case "addSlot": game.AddSlot(); break;
                        default: throw new InvalidDataException("unknown op " + step.Op);
                    }
                }
                catch (Exception e)
                {
                    result.Divergence = new ParityDivergence { Step = k, Call = step.Describe(), PreviousCall = previousCall, Field = "exception", Expected = "no exception", Actual = e.GetType().Name + ": " + e.Message };
                    return result;
                }

                Dictionary<string, string> actual = Snapshot(game, previous);
                previous = Cells(game.Board);
                var diffs = new List<(string field, string expected, string actual)>();
                if (step.Result != null && actualOp.TryGetValue("result", out string r) && r != step.Result) diffs.Add(("result", step.Result, r));
                if (step.Sent != null && actualOp.TryGetValue("sent", out string d) && d != step.Sent) diffs.Add(("sent", step.Sent, d));
                foreach (string field in ParityScenario.Fields)
                    if (actual[field] != step.Expected[field]) diffs.Add((field, step.Expected[field], actual[field]));
                if (diffs.Count > 0)
                {
                    var div = new ParityDivergence
                    {
                        Step = k, Call = step.Describe(), PreviousCall = previousCall,
                        Field = diffs[0].field, Expected = diffs[0].expected, Actual = diffs[0].actual,
                    };
                    div.AlsoDifferent.AddRange(diffs.Skip(1).Select(x => x.field));
                    result.Divergence = div;
                    return result;
                }
                foreach (string field in ParityScenario.Fields) trace.Append(actual[field]).Append('\n');
                result.StepsMatched = k + 1;
            }
            result.ActualOutcome = StatusText(game.Status);
            result.TraceHash = GoldenHash.Fnv1a(Encoding.UTF8.GetBytes(trace.ToString()).Select(b => (int)b));
            return result;
        }

        /// <summary>The scenario's level with its edges and slot count, and its tray: from the file, generated or built.</summary>
        public static GameEngine Setup(ParityScenario s, out string traySha256)
        {
            LevelReadResult read = LevelDefinitionReader.Read(File.ReadAllText(ProjectPaths.Of(s.LevelFile), Encoding.UTF8));
            if (!read.Ok) throw new InvalidDataException(s.LevelFile + ": " + read.Describe());
            LevelDefinition level = read.Level;
            level.Edges = s.Edges;
            level.SlotCount = s.SlotCount;
            TrayDefinition tray;
            switch (s.TraySource)
            {
                case "file":
                    tray = level.Tray;
                    break;
                case "generate":
                    tray = LevelGenerator.Generate(level.Picture, Options(level, s));
                    break;
                case "build":
                    GenerationOptions o = Options(level, s);
                    if (s.Tray.Has("targetWinRate")) o.TargetWinRate = s.Tray.Get("targetWinRate").Number;
                    o.Candidates = s.Tray.Has("candidates") ? (int)s.Tray.Get("candidates").Number : GenerationSettings.DefaultCandidates;
                    tray = LevelBuilder.Build(level.Picture, o).Tray;
                    break;
                default: throw new InvalidDataException("unknown tray source " + s.TraySource);
            }
            traySha256 = GeneratorGoldenTests.ColumnsSha256(tray);
            return GameFactory.CreateGame(level.Picture, level.Edges, level.SlotCount, tray);
        }

        static GenerationOptions Options(LevelDefinition level, ParityScenario s) =>
            GenerationOptions.For(level).With((long)s.Tray.Get("seed").Number, s.Tray.Get("shuffle").Number);

        static string JsonText(ParityScenario s) => CubeHaul.Levels.Json.JsonWriter.Stringify(s.Tray);

        static int[] Cells(Board b)
        {
            var cells = new int[b.CellCount];
            b.CopyCells(cells);
            return cells;
        }

        /// <summary>What the rules report, as JSON text, field by field (Tools/ts-reference/scripts/golden/parity.ts).</summary>
        public static Dictionary<string, string> Snapshot(GameEngine game, int[] previous)
        {
            GameState g = game.State;
            Board b = g.Board;
            int n = b.CellCount, colors = b.ColorCount;
            return new Dictionary<string, string>
            {
                ["status"] = "\"" + StatusText(g.Status) + "\"",
                ["remaining"] = b.Remaining.ToString(),
                ["pending"] = b.Pending.ToString(),
                ["tray"] = g.TrayCount().ToString(),
                ["columns"] = List(Enumerable.Range(0, g.ColumnCount).Select(c => g.ColumnLength(c).ToString())),
                ["fronts"] = List(Enumerable.Range(0, g.ColumnCount).Select(c => (g.Front(c)?.Id ?? -1).ToString())),
                ["slots"] = List(Enumerable.Range(0, g.SlotCount).Select(g.SlotAt).Select(k => k == null ? "null" : $"[{k.Id},{k.Color},{k.Count}]")),
                ["emptied"] = List(Enumerable.Range(0, n).Where(i => previous[i] != CubeHaul.Domain.Cells.Empty && b.Cell(i) == CubeHaul.Domain.Cells.Empty).Select(i => i.ToString())),
                ["canEat"] = List(Enumerable.Range(0, g.SlotCount).Select(k => Bool(g.SlotCanEat(k)))),
                ["blocked"] = List(Enumerable.Range(0, g.SlotCount).Select(k => Bool(g.SlotBlocked(k)))),
                ["stuck"] = Bool(g.IsStuck()),
                ["reachable"] = List(Enumerable.Range(0, colors).Select(c => b.ReachableCount(c).ToString())),
                ["pick"] = List(Enumerable.Range(0, colors).Select(c => b.PickTarget(c).ToString())),
                ["reachHash"] = "\"" + GoldenHash.Fnv1a(Enumerable.Range(0, colors).SelectMany(c => b.ReachableCells(c).Concat(new[] { -1 }))) + "\"",
                ["boardHash"] = "\"" + GoldenHash.Fnv1a(GoldenScenarios.BoardValues(b)) + "\"",
            };
        }

        static string Dispatches(List<Dispatch> sent) => List(sent.Select(d => $"[{d.Slot},{d.Cell},{d.Color}]"));

        static string List(IEnumerable<string> items) => "[" + string.Join(",", items) + "]";

        static string Bool(bool b) => b ? "true" : "false";

        public static string StatusText(GameStatus s) => s == GameStatus.Playing ? "playing" : s == GameStatus.Won ? "won" : "stuck";
    }
}
