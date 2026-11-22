using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CubeHaul.Levels;
using CubeHaul.Levels.Generation;
using CubeHaul.Levels.Json;
using NUnit.Framework;

namespace CubeHaul.Tests.EditMode.Parity
{
    /// <summary>
    /// The C# core acceptance test (Task 10, docs/PARITY.md): 24 parity scenarios recorded from the TypeScript game,
    /// replayed on the C# rules step by step. Every level is covered by at least two; a failure names the first step
    /// that differs with the expected and actual values.
    /// </summary>
    public class ParityTests
    {
        const string Content = "Assets/_CubeHaul/Levels/Content";

        static readonly Dictionary<string, ParityScenario> Loaded = new Dictionary<string, ParityScenario>();

        static ParityScenario Scenario(string file)
        {
            lock (Loaded)
            {
                if (!Loaded.TryGetValue(file, out ParityScenario s)) Loaded[file] = s = ParityScenario.Load(file);
                return s;
            }
        }

        static IEnumerable<string> Files() => ParityScenario.Files();

        static IEnumerable<string> LevelIds() => LevelRepository.LoadDirectory(ProjectPaths.Of(Content)).ThrowIfErrors().LevelIds;

        static List<ParityScenario> All() => Files().Select(Scenario).ToList();

        [TestCaseSource(nameof(Files))]
        public void ScenarioMatchesTypeScriptStepByStep(string file)
        {
            ParityResult r = ParityRunner.Run(Scenario(file));
            Assert.That(r.Ok, Is.True, ParityReport.Describe(r));
            Assert.That(r.StepsMatched, Is.EqualTo(r.Scenario.Steps.Count));
            Assert.That(r.ActualOutcome, Is.EqualTo(r.Scenario.Outcome));
        }

        /// <summary>One row per level in the Test Runner: everything recorded about the level, checked together.</summary>
        [TestCaseSource(nameof(LevelIds))]
        public void LevelPassesTheGoldenAcceptance(string levelId)
        {
            string path = Directory.GetFiles(ProjectPaths.Of(Content), "*-" + levelId + ".level.json").Single();
            string levelFile = Content + "/" + Path.GetFileName(path);
            var problems = new List<string>();

            // 1. The file is valid and its tray and difficulty are exactly what its generation settings give.
            BakeFileResult bake = LevelBaker.BakeFile(File.ReadAllText(path, Encoding.UTF8));
            if (bake.ReadFailed) problems.Add("cannot be read: " + bake.Read.Describe());
            else if (bake.Changed) problems.Add("rebaking its generation settings does not give the file back");

            // 2. generateLevel for the reference seeds.
            JsonValue generate = GoldenScenarios.Json(GoldenScenarios.GoldenDir + "/generate.json").Get("levels").Items.Single(l => l.Get("levelId").String == levelId);
            LevelDefinition level = LevelDefinitionReader.Read(File.ReadAllText(path, Encoding.UTF8)).Level;
            foreach (JsonValue run in generate.Get("runs").Items)
            {
                TrayDefinition tray = LevelGenerator.Generate(level.Picture, GenerationOptions.For(level).With((long)run.Get("seed").Number, run.Get("shuffle").Number));
                if (GeneratorGoldenTests.ColumnsSha256(tray) != run.Get("columnsSha256").String)
                    problems.Add($"generate seed {run.Get("seed").Number} shuffle {run.Get("shuffle").Number}: another tray");
            }

            // 3. Every parity scenario on this level.
            List<ParityScenario> mine = All().Where(s => s.LevelFile == levelFile).ToList();
            if (mine.Count < 2) problems.Add($"only {mine.Count} parity scenarios");
            foreach (ParityScenario s in mine)
            {
                ParityResult r = ParityRunner.Run(s);
                if (!r.Ok) problems.Add(ParityReport.Describe(r));
            }
            Assert.That(problems, Is.Empty, levelId + ":\n" + string.Join("\n", problems));
        }

        [Test]
        public void TheReportIsWrittenAndShowsNoDifferences()
        {
            List<ParityResult> results = All().Select(ParityRunner.Run).ToList();
            string report = ParityReport.Format(results);
            string path = ParityReport.Write(report);
            TestContext.WriteLine(report);
            TestContext.WriteLine("written to " + path);
            Assert.That(results.Where(r => !r.Ok), Is.Empty, report);
            StringAssert.Contains("no differences", report);
            foreach (ParityResult r in results) StringAssert.Contains(r.Scenario.Name, report);
        }

        // ---- the report itself ----

        static ParityScenario Tampered(string file, Action<ParityScenario> change)
        {
            ParityScenario s = ParityScenario.Load(file); // a fresh copy, not the cached one
            change(s);
            return s;
        }

        [Test]
        public void AWrongValueIsReportedAtItsStepWithExpectedAndActual()
        {
            const string file = "p05-mushroom-abuse.json";
            ParityScenario original = Scenario(file);
            int k = original.Steps.FindIndex(st => st.Op == "collect");
            string truth = original.Steps[k].Expected["pick"];
            ParityScenario s = Tampered(file, t =>
            {
                for (int j = k; j < t.Steps.Count; j++) t.Steps[j].Expected["pick"] = "[1,2,3]";
            });
            ParityResult r = ParityRunner.Run(s);
            Assert.That(r.Ok, Is.False);
            Assert.That((r.Divergence.Step, r.Divergence.Field, r.Divergence.Expected, r.Divergence.Actual), Is.EqualTo((k, "pick", "[1,2,3]", truth)));
            Assert.That(r.StepsMatched, Is.EqualTo(k), "steps before it matched");
            Assert.That(r.Divergence.Call, Does.StartWith("collect(cell "));
            Assert.That(r.Divergence.PreviousCall, Does.StartWith("step " + (k - 1) + ": "));
            string text = ParityReport.Format(new[] { r });
            StringAssert.Contains($"first difference at step {k} of {s.Steps.Count}", text);
            StringAssert.Contains("expected:  [1,2,3]   (TypeScript)", text);
            StringAssert.Contains("actual:    " + truth + "   (C#)", text);
        }

        [Test]
        public void AWrongCallResultIsReportedBeforeTheStateItCauses()
        {
            const string file = "p07-sunset-random-step.json";
            int k = Scenario(file).Steps.FindIndex(st => st.Op == "step" && st.Sent != "[]");
            ParityScenario s = Tampered(file, t => t.Steps[k].Expected["remaining"] = "-1");
            s.Steps[k].Sent = "[[0,0,0]]";
            ParityResult r = ParityRunner.Run(s);
            Assert.That((r.Divergence.Step, r.Divergence.Field, r.Divergence.Expected), Is.EqualTo((k, "sent", "[[0,0,0]]")));
            CollectionAssert.Contains(r.Divergence.AlsoDifferent, "remaining");
        }

        [Test]
        public void AnotherTrayIsReportedAtSetup()
        {
            ParityScenario s = Tampered("p13-cat-generated-max-seed-random.json", t => t.TraySha256 = "0000");
            ParityResult r = ParityRunner.Run(s);
            Assert.That((r.Divergence.Step, r.Divergence.Field, r.Divergence.Expected), Is.EqualTo((-1, "traySha256", "0000")));
            StringAssert.Contains("differs while setting up", ParityReport.Format(new[] { r }));
        }

        [Test]
        public void AnExceptionIsReportedAsADifference()
        {
            const string file = "p04-mushroom-fifo.json";
            int k = Scenario(file).Steps.FindIndex(st => st.Op == "collect");
            ParityScenario s = Tampered(file, t => t.Steps[k].Cell = 0); // cell 0 is far from the bottom edge: not reachable
            ParityResult r = ParityRunner.Run(s);
            Assert.That((r.Divergence.Step, r.Divergence.Field), Is.EqualTo((k, "exception")));
            StringAssert.Contains("InvalidOperationException", r.Divergence.Actual);
        }

        // ---- repeatability ----

        [Test]
        public void ReplaysAreRepeatableAndIndependentOfOrder()
        {
            List<ParityScenario> all = All();
            Dictionary<string, string> first = all.ToDictionary(s => s.Name, s => ParityRunner.Run(s).TraceHash);
            all.Reverse();
            foreach (ParityScenario s in all)
                Assert.That(ParityRunner.Run(s).TraceHash, Is.EqualTo(first[s.Name]), s.Name);
            Assert.That(first.Values.Distinct().Count(), Is.EqualTo(first.Count), "every scenario plays a different game");
        }

        // ---- coverage ----

        [Test]
        public void TheScenariosCoverWhatTheTaskAsksFor()
        {
            List<ParityScenario> all = All();
            List<ParityStep> steps = all.SelectMany(s => s.Steps).ToList();
            var report = new StringBuilder();
            void Need(bool ok, string what)
            {
                report.Append(ok ? "  ok   " : "  MISSING ").Append(what).Append('\n');
                Assert.That(ok, Is.True, what + "\n" + report);
            }

            Need(all.Count >= 20, $"at least 20 scenarios ({all.Count})");
            foreach (string id in LevelIds())
                Need(all.Count(s => s.LevelFile.EndsWith("-" + id + ".level.json", StringComparison.Ordinal)) >= 2, $"level {id} in at least 2 scenarios");
            Need(all.Select(s => s.Seed).Distinct().Count() >= 15, "at least 15 different policy seeds");
            Need(all.Where(s => s.TraySource != "file").Select(s => s.Tray.Get("seed").Number).Distinct().Count() >= 6, "at least 6 different tray seeds");
            Need(new[] { "file", "generate", "build" }.All(src => all.Any(s => s.TraySource == src)), "trays from the file, generated and built");
            Need(Enum.GetValues(typeof(CubeHaul.Domain.OpenEdges)).Cast<CubeHaul.Domain.OpenEdges>().All(e => all.Any(s => s.Edges == e)), "all three edge modes");
            Need(all.Select(s => s.SlotCount).Distinct().Count() >= 5, "at least 5 starting slot counts");
            Need(all.Select(s => s.Policy.Split(' ')[0]).Distinct().Count() >= 7, "at least 7 input policies");

            // Win, jam, reachability, slot count, dispatched and collected cells.
            Need(all.Count(s => s.Outcome == "won") >= 3, "at least 3 games won");
            Need(all.Count(s => s.Outcome == "stuck") >= 2, "at least 2 games ending stuck");
            Need(steps.Any(st => st.Expected["status"] == "\"stuck\"") && steps.Any(st => st.Op == "addSlot"), "stuck, then +1 Slot");
            int[] slotCounts = steps.Select(st => JsonParser.Parse(st.Expected["slots"]).Items.Count).Distinct().OrderBy(x => x).ToArray();
            Need(slotCounts.First() <= 1 && slotCounts.Last() >= 7, "slot counts from 1 up to 7 and more (" + string.Join(", ", slotCounts) + ")");
            Need(steps.Any(st => st.Expected["blocked"].Contains("true")), "blocked slots");
            Need(steps.Any(st => st.Expected["canEat"].Contains("false") && st.Expected["canEat"].Contains("true")), "slots that can and cannot eat at the same time");
            Need(steps.Any(st => st.Expected["reachable"].Split(',').Contains("0")), "colors with nothing reachable");
            Need(steps.Count(st => st.Op == "tap" && st.Result == "-1") >= 100, "at least 100 ignored taps (bad, empty, full, after the end)");
            Need(steps.Count(st => st.Op == "tick" && st.Ready != null && st.Ready.Contains(false)) >= 100, "at least 100 ticks with slots held back");
            Need(steps.Count(st => st.Op == "tick" && st.Ready != null && st.Ready.All(x => !x)) >= 10, "ticks with every slot held back");
            Need(steps.Count(st => st.Op == "collect") >= 2000, "at least 2000 cubes collected");
            Need(steps.Count(st => st.Sent != null && st.Sent != "[]") >= 1000, "at least 1000 calls that dispatch trucks");
            Need(all.Any(s => OutOfOrder(s)), "pickups out of dispatch order");
            Need(steps.Any(st => st.Expected["pending"] != "0" && st.Expected["status"] == "\"playing\"" && st.Expected["canEat"].Contains("false")), "trucks out while a slot waits");
            Need(all.Any(s => s.Outcome == "won" && s.Steps.Last().Op == "tap" && s.Steps.Last().Result == "-1"), "input after the win is ignored");
            TestContext.WriteLine(report.ToString());
        }

        /// <summary>True if some collect picks a truck other than the oldest one out.</summary>
        static bool OutOfOrder(ParityScenario s)
        {
            var outstanding = new List<int>();
            foreach (ParityStep st in s.Steps)
            {
                if (st.Sent != null && st.Op == "tick")
                    outstanding.AddRange(JsonParser.Parse(st.Sent).Items.Select(d => (int)d.Items[1].Number));
                if (st.Op != "collect") continue;
                if (outstanding.Count > 0 && outstanding[0] != st.Cell) return true;
                outstanding.Remove(st.Cell.Value);
            }
            return false;
        }
    }
}
