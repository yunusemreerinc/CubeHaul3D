using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CubeHaul.Domain;
using CubeHaul.Levels;
using CubeHaul.Levels.Generation;
using CubeHaul.Levels.Json;
using NUnit.Framework;

namespace CubeHaul.Tests.EditMode
{
    /// <summary>
    /// The C# generator, solvers and RNG against the TypeScript reference (docs/GOLDEN_FIXTURES.md): rng.json,
    /// generate.json, build.json, generator-cases.json, and the ten level files, which must come out of
    /// <see cref="LevelBaker"/> byte for byte.
    /// </summary>
    public class GeneratorGoldenTests
    {
        const string Golden = GoldenScenarios.GoldenDir;
        const string Content = "Assets/_CubeHaul/Levels/Content";

        static LevelRepository repo;

        static LevelRepository Repo => repo ?? (repo = LevelRepository.LoadDirectory(ProjectPaths.Of(Content)).ThrowIfErrors());

        static int Int(JsonValue v) => (int)v.Number;

        /// <summary>sha256 of JavaScript's JSON.stringify(columns): keys id, color, count, no spaces.</summary>
        internal static string ColumnsSha256(TrayDefinition tray)
        {
            var text = new StringBuilder("[");
            for (int c = 0; c < tray.Columns.Length; c++)
            {
                if (c > 0) text.Append(',');
                text.Append('[');
                for (int k = 0; k < tray.Columns[c].Length; k++)
                {
                    BlockDefinition b = tray.Columns[c][k];
                    if (k > 0) text.Append(',');
                    text.Append("{\"id\":").Append(b.Id).Append(",\"color\":").Append(b.Color).Append(",\"count\":").Append(b.Count).Append('}');
                }
                text.Append(']');
            }
            text.Append(']');
            using (SHA256 sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())).Select(x => x.ToString("x2")));
        }

        static int[][] Crates(TrayDefinition tray) => tray.Columns.SelectMany(col => col.Select(b => new[] { b.Id, b.Color, b.Count })).ToArray();

        static int[][] Crates(JsonValue columns) => columns.Items.SelectMany(col => col.Items.Select(GoldenScenarios.Ints)).ToArray();

        static int[] ColumnShape(JsonValue columns) => columns.Items.Select(c => c.Items.Count).ToArray();

        static int[] ColumnShape(TrayDefinition tray) => tray.Columns.Select(c => c.Length).ToArray();

        // ---- rng.json ----

        [Test]
        public void Mulberry32GivesTheGoldenValues()
        {
            JsonValue rng = GoldenScenarios.Json(Golden + "/rng.json");
            int count = Int(rng.Get("count"));
            Assert.That(rng.Get("seeds").Items.Count, Is.EqualTo(6));
            foreach (JsonValue s in rng.Get("seeds").Items)
            {
                long seed = (long)s.Get("seed").Number;
                var a = new Mulberry32(seed);
                var b = new Mulberry32(seed);
                IReadOnlyList<JsonValue> values = s.Get("values").Items;
                Assert.That(values.Count, Is.EqualTo(count));
                for (int k = 0; k < count; k++)
                {
                    Assert.That(a.NextUInt32(), Is.EqualTo((uint)values[k].Number), $"seed {seed} value {k}");
                    Assert.That(b.Next() * 4294967296.0, Is.EqualTo(values[k].Number), $"seed {seed} value {k} as a double");
                }
            }
        }

        // ---- generate.json ----

        [Test]
        public void GenerateMatchesForEveryLevelSeedAndShuffle()
        {
            JsonValue generate = GoldenScenarios.Json(Golden + "/generate.json");
            Assert.That(generate.Get("levels").Items.Count, Is.EqualTo(10));
            int runs = 0;
            foreach (JsonValue entry in generate.Get("levels").Items)
            {
                LevelDefinition level = Repo.Get(entry.Get("levelId").String);
                foreach (JsonValue run in entry.Get("runs").Items)
                {
                    long seed = (long)run.Get("seed").Number;
                    double shuffle = run.Get("shuffle").Number;
                    string where = $"{level.LevelId} seed {seed} shuffle {shuffle}";
                    TrayDefinition tray = LevelGenerator.Generate(level.Picture, GenerationOptions.For(level).With(seed, shuffle));
                    Assert.That(tray.BlockCount, Is.EqualTo(Int(run.Get("crates"))), where + ": crates");
                    CollectionAssert.AreEqual(GoldenScenarios.Ints(run.Get("columnLengths")), ColumnShape(tray), where + ": column lengths");
                    for (int c = 0; c < tray.Columns.Length; c++)
                    {
                        BlockDefinition f = tray.Columns[c][0];
                        CollectionAssert.AreEqual(GoldenScenarios.Ints(run.Get("fronts").Items[c]), new[] { f.Id, f.Color, f.Count }, where + ": front of column " + c);
                    }
                    Assert.That(ColumnsSha256(tray), Is.EqualTo(run.Get("columnsSha256").String), where + ": every crate (columnsSha256)");
                    runs++;
                }
            }
            Assert.That(runs, Is.EqualTo(40));
        }

        // ---- build.json ----

        static IEnumerable<string> LevelIds() => LevelRepository.LoadDirectory(ProjectPaths.Of(Content)).ThrowIfErrors().LevelIds;

        [TestCaseSource(nameof(LevelIds))]
        public void BuildTriesTheSameCandidatesAndChoosesTheSameTray(string levelId)
        {
            JsonValue entry = GoldenScenarios.Json(Golden + "/build.json").Get("levels").Items.Single(l => l.Get("levelId").String == levelId);
            LevelDefinition level = Repo.Get(levelId);
            GenerationOptions options = GenerationOptions.For(level);
            JsonValue o = entry.Get("options");
            Assert.That((options.Seed, options.Shuffle, options.TargetWinRate, options.Columns, options.SlotCount, options.BigChance, OpenEdgesJson.ToJson(options.Edges)),
                Is.EqualTo(((long)o.Get("seed").Number, o.Get("shuffle").Number, o.Get("targetWinRate").Number, Int(o.Get("columns")), Int(o.Get("slots")),
                    o.Get("bigChance").Number, o.Get("edges").String)), "the level file's generation settings are the options TypeScript built with");

            BuildResult built = LevelBuilder.Build(level.Picture, options);
            IReadOnlyList<JsonValue> candidates = entry.Get("candidates").Items;
            Assert.That(built.Attempts.Count, Is.EqualTo(candidates.Count), "candidates tried");
            for (int k = 0; k < candidates.Count; k++)
            {
                JsonValue want = candidates[k];
                BuildAttempt got = built.Attempts[k];
                string where = $"{levelId} candidate {k}";
                Assert.That((got.Phase, got.K, got.Seed), Is.EqualTo((BuildPath.Candidate, Int(want.Get("k")), (long)want.Get("seed").Number)), where);
                Assert.That(got.Tray.BlockCount, Is.EqualTo(Int(want.Get("crates"))), where + ": crates");
                Assert.That(ColumnsSha256(got.Tray), Is.EqualTo(want.Get("columnsSha256").String), where + ": tray");
                AssertGreedy(want.Get("greedy"), got.Greedy, where);
                AssertGames(want, got, where);
            }
            Assert.That((built.Path, built.Chosen), Is.EqualTo((BuildPath.Candidate, Int(entry.Get("chosen")))));
            Assert.That(built.WinRate, Is.EqualTo(entry.Get("winRate").Number), "win rate, exactly");
            Assert.That(ColumnsSha256(built.Tray), Is.EqualTo(ColumnsSha256(level.Tray)), "the chosen tray is the level file's tray");
        }

        static void AssertGreedy(JsonValue want, SolveResult got, string where)
        {
            IReadOnlyList<JsonValue> g = want.Items;
            Assert.That((got.Solved, got.Ticks, got.Taps), Is.EqualTo((g[0].Boolean, Int(g[1]), Int(g[2]))), where + ": greedy [solved, ticks, taps]");
        }

        static void AssertGames(JsonValue want, BuildAttempt got, string where)
        {
            if (!want.Has("games"))
            {
                Assert.That((got.Games, got.WinRate), Is.EqualTo(((bool[])null, (double?)null)), where + ": no random games for an unsolved tray");
                return;
            }
            CollectionAssert.AreEqual(want.Get("games").Items.Select(v => v.Number == 1), got.Games, where + ": random games, one by one");
            Assert.That(got.WinRate, Is.EqualTo(want.Get("winRate").Number), where + ": win rate");
        }

        // ---- generator-cases.json ----

        static IEnumerable<string> CaseNames() =>
            GoldenScenarios.Json(Golden + "/generator-cases.json").Get("cases").Items.Select(c => c.Get("name").String);

        static GenerationOptions OptionsOf(JsonValue c)
        {
            JsonValue g = c.Get("generation");
            OpenEdgesJson.TryParse(c.Get("edges").String, out OpenEdges edges);
            var o = new GenerationOptions
            {
                Seed = (long)g.Get("seed").Number,
                Shuffle = g.Get("shuffle").Number,
                Edges = edges,
                SlotCount = Int(c.Get("slotCount")),
            };
            if (g.Has("targetWinRate")) o.TargetWinRate = g.Get("targetWinRate").Number;
            if (g.Has("columns")) o.Columns = Int(g.Get("columns"));
            if (g.Has("bigChance")) o.BigChance = g.Get("bigChance").Number;
            if (g.Has("minBlock")) o.MinBlock = Int(g.Get("minBlock"));
            if (g.Has("maxBlock")) o.MaxBlock = Int(g.Get("maxBlock"));
            if (g.Has("candidates")) o.Candidates = Int(g.Get("candidates"));
            return o;
        }

        static PictureDefinition PictureOf(JsonValue c)
        {
            if (c.Has("pictureFrom"))
                return LevelDefinitionReader.Read(File.ReadAllText(ProjectPaths.Of(c.Get("pictureFrom").String), Encoding.UTF8)).Level.Picture;
            JsonValue p = c.Get("picture");
            return new PictureDefinition(Int(p.Get("width")), Int(p.Get("height")), GoldenScenarios.Ints(p.Get("cells")), p.Get("palette").Items.Select(v => v.String).ToArray());
        }

        [TestCaseSource(nameof(CaseNames))]
        public void GeneratorCaseMatches(string name)
        {
            JsonValue c = GoldenScenarios.Json(Golden + "/generator-cases.json").Get("cases").Items.Single(x => x.Get("name").String == name);
            PictureDefinition picture = PictureOf(c);
            GenerationOptions options = OptionsOf(c);

            TrayDefinition generated = LevelGenerator.Generate(picture, options);
            Assert.That(Crates(generated), Is.EqualTo(Crates(c.Get("generate").Get("columns"))), "generate: every crate [id, color, count]");
            CollectionAssert.AreEqual(ColumnShape(c.Get("generate").Get("columns")), ColumnShape(generated), "generate: columns");
            Assert.That(ColumnsSha256(generated), Is.EqualTo(c.Get("generate").Get("columnsSha256").String));

            BuildResult built = LevelBuilder.Build(picture, options);
            IReadOnlyList<JsonValue> tried = c.Get("build").Get("tried").Items;
            Assert.That(built.Attempts.Count, Is.EqualTo(tried.Count), "trays tried");
            for (int k = 0; k < tried.Count; k++)
            {
                JsonValue want = tried[k];
                BuildAttempt got = built.Attempts[k];
                string where = $"{name} try {k}";
                Assert.That(PhaseName(got.Phase), Is.EqualTo(want.Get("phase").String), where + ": phase");
                Assert.That((got.K, got.Seed, got.Shuffle), Is.EqualTo((Int(want.Get("k")), (long)want.Get("seed").Number, want.Get("shuffle").Number)), where + ": k, seed, shuffle");
                Assert.That(ColumnsSha256(got.Tray), Is.EqualTo(want.Get("columnsSha256").String), where + ": tray");
                AssertGreedy(want.Get("greedy"), got.Greedy, where);
                AssertGames(want, got, where);
            }
            JsonValue result = c.Get("build").Get("result");
            Assert.That(PhaseName(built.Path), Is.EqualTo(result.Get("path").String), "path");
            Assert.That(built.Verified, Is.EqualTo(result.Get("path").String != "unverified"));
            Assert.That(built.Chosen, Is.EqualTo(Int(result.Get("tried"))), "chosen try");
            Assert.That(built.WinRate, Is.EqualTo(result.Get("winRate").Number), "win rate, exactly");
            Assert.That(Crates(built.Tray), Is.EqualTo(Crates(result.Get("columns"))), "the tray buildLevel returns");
            AssertGreedy(result.Get("greedy"), GreedySolver.Solve(picture, options.Edges, options.SlotCount, built.Tray), name + " result");
        }

        static string PhaseName(BuildPath p) => p == BuildPath.Candidate ? "candidate" : p == BuildPath.Fallback ? "fallback" : "unverified";

        [Test]
        public void TheGeneratorCasesReachEveryBranch()
        {
            var paths = GoldenScenarios.Json(Golden + "/generator-cases.json").Get("cases").Items.Select(c => c.Get("build").Get("result").Get("path").String).ToList();
            CollectionAssert.IsSubsetOf(new[] { "candidate", "fallback", "unverified" }, paths);
        }

        // ---- the level files themselves ----

        [TestCaseSource(nameof(LevelIds))]
        public void BakingTheGenerationSettingsGivesTheLevelFileByteForByte(string levelId)
        {
            string path = Directory.GetFiles(ProjectPaths.Of(Content), "*-" + levelId + ".level.json").Single();
            string text = File.ReadAllText(path, Encoding.UTF8);
            LevelDefinition level = LevelDefinitionReader.Read(text).Level;
            LevelDefinition stripped = level.DeepCopy();
            stripped.Tray = null;
            stripped.DifficultyMetadata = null;

            BakeResult baked = LevelBaker.Bake(stripped);
            Assert.That(baked.Verified, Is.True);
            Assert.That(baked.Level.ContentEquals(level), Is.True, "tray and difficulty");
            Assert.That(LevelDefinitionWriter.ToJson(baked.Level), Is.EqualTo(text), "the whole file");
            Assert.That(stripped.Tray, Is.Null, "the input level is not changed");
        }

        [Test]
        public void TheBakeMenuLeavesUpToDateFilesAloneAndFixesStaleOnes()
        {
            foreach (string path in Directory.GetFiles(ProjectPaths.Of(Content), "*.level.json"))
            {
                BakeFileResult fresh = LevelBaker.BakeFile(File.ReadAllText(path, Encoding.UTF8));
                Assert.That((fresh.ReadFailed, fresh.HasGeneration, fresh.Changed), Is.EqualTo((false, true, false)), path);
            }

            string text = File.ReadAllText(Directory.GetFiles(ProjectPaths.Of(Content), "*-cat.level.json").Single(), Encoding.UTF8);
            LevelDefinition stale = LevelDefinitionReader.Read(text).Level;
            BlockDefinition[] col = stale.Tray.Columns[0];
            (col[0], col[1]) = (col[1], col[0]);
            stale.DifficultyMetadata.WinRate = 0.3; // still "Expert": the validator checks the label against the win rate
            BakeFileResult fixedUp = LevelBaker.BakeFile(LevelDefinitionWriter.ToJson(stale));
            Assert.That((fixedUp.ReadFailed, fixedUp.Changed), Is.EqualTo((false, true)), fixedUp.Read.Describe());
            Assert.That(fixedUp.Text, Is.EqualTo(text), "baking restores the file exactly");

            string staticOnly = File.ReadAllText(ProjectPaths.Of("LevelFormat/examples/ring.static.level.json"), Encoding.UTF8);
            BakeFileResult none = LevelBaker.BakeFile(staticOnly);
            Assert.That((none.HasGeneration, none.Text, none.Changed), Is.EqualTo((false, (string)null, false)));
            Assert.That(LevelBaker.BakeFile("{ not json").ReadFailed, Is.True);
        }
    }
}
