using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CubeHaul.Domain;
using CubeHaul.Levels;
using CubeHaul.Levels.Generation;
using NUnit.Framework;

namespace CubeHaul.Tests.EditMode
{
    /// <summary>
    /// cube-hunt's generator and solver tests (Tools/ts-reference/tests/generator.test.ts, solver.test.ts and the
    /// "levels" part of tests/core.test.ts) on the C# port, plus the JavaScript-number pitfalls the port has to avoid.
    /// </summary>
    public class GeneratorRuleTests
    {
        static LevelRepository repo;
        static LevelRepository Repo => repo ?? (repo = LevelRepository.LoadDirectory(ProjectPaths.Of("Assets/_CubeHaul/Levels/Content")).ThrowIfErrors());

        static LevelDefinition Level(string id) => Repo.Get(id);

        /// <summary>Rows of characters: '.' empty, a digit is that color; row 0 is the top.</summary>
        static PictureDefinition Pic(params string[] rows)
        {
            int[] cells = rows.SelectMany(r => r.Select(ch => ch == '.' ? Cells.Empty : ch - '0')).ToArray();
            int colors = Math.Max(1, cells.Max() + 1);
            return new PictureDefinition(rows[0].Length, rows.Length, cells, Enumerable.Range(0, colors).Select(c => "#00000" + c).ToArray());
        }

        static PictureDefinition Solid(int w, int h) => Pic(Enumerable.Repeat(new string('0', w), h).ToArray());

        /// <summary>"color:count" crates, front first; columns separated by '|'; ids in reading order.</summary>
        static TrayDefinition Tray(string tray)
        {
            int id = 0;
            return new TrayDefinition
            {
                Columns = tray.Split('|').Select(col => col.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(k => k.Split(':')).Select(p => new BlockDefinition(id++, int.Parse(p[0]), int.Parse(p[1]))).ToArray()).ToArray(),
            };
        }

        static SolveResult Greedy(PictureDefinition p, string tray, int slots, int maxTicks = GreedySolver.DefaultMaxTicks) =>
            GreedySolver.Solve(p, OpenEdges.Bottom, slots, Tray(tray), maxTicks);

        static double WinRate(PictureDefinition p, string tray, int slots, int games = 24, long seed = 1) =>
            RandomPlayer.WinRate(p, OpenEdges.Bottom, slots, Tray(tray), games, seed);

        static string Crates(TrayDefinition t) => string.Join("|", t.Columns.Select(c => string.Join(",", c.Select(b => $"{b.Id}:{b.Color}:{b.Count}"))));

        static GenerationOptions Opts(long seed, double shuffle, OpenEdges edges = OpenEdges.All) => new GenerationOptions { Seed = seed, Shuffle = shuffle, Edges = edges };

        static double[] Draw(long seed, int n)
        {
            var r = new Mulberry32(seed);
            return Enumerable.Range(0, n).Select(_ => r.Next()).ToArray();
        }

        // ---- mulberry32 (B-20, I-21) ----

        [TestCase(0L, new uint[] { 1144304738, 1416247, 958946056, 627933444, 2007157716 })]
        [TestCase(1L, new uint[] { 2693262067, 11749833, 2265367787, 4213581821, 4159151403 })]
        [TestCase(11L, new uint[] { 2197249643, 2276102492, 2611849345, 2534707746, 3654058086 })]
        [TestCase(1234L, new uint[] { 314799534, 3021131492, 3877737075, 4168477787, 175938938 })]
        [TestCase(2147483647L, new uint[] { 1842962257, 546041740, 1654754255, 1702490205, 513796057 })]
        public void Mulberry32GivesTheGoldenFirstFiveValues(long seed, uint[] expected)
        {
            CollectionAssert.AreEqual(expected.Select(v => (double)v), Draw(seed, 5).Select(v => v * 4294967296.0));
        }

        [Test]
        public void Mulberry32ReturnsWholeMultiplesOfTwoToTheMinus32InZeroToOne()
        {
            foreach (double v in Draw(99, 100_000))
            {
                Assert.That(v >= 0 && v < 1, Is.True);
                Assert.That(Math.Floor(v * 4294967296.0), Is.EqualTo(v * 4294967296.0));
            }
        }

        [Test]
        public void Mulberry32TakesTheSeedModulo2To32()
        {
            CollectionAssert.AreEqual(Draw(4294967295L, 8), Draw(-1, 8));
            CollectionAssert.AreEqual(Draw(5, 8), Draw(4294967296L + 5, 8));
            CollectionAssert.AreNotEqual(Draw(2, 8), Draw(1, 8));
        }

        // ---- JavaScript numbers (R-02) ----

        [TestCase(0.5, 1)]
        [TestCase(1.5, 2)]
        [TestCase(2.5, 3)]
        [TestCase(8.5, 9)]
        [TestCase(62.5, 63)]
        [TestCase(-0.5, 0)]
        [TestCase(-2.5, -2)]
        [TestCase(-2.6, -3)]
        [TestCase(0.49999999999999994, 0)]
        [TestCase(4503599627370495.5, 4503599627370496)]
        public void JsRoundRoundsHalvesUp(double x, double expected)
        {
            Assert.That(JsMath.Round(x), Is.EqualTo(expected));
        }

        [Test]
        public void CSharpRoundingWouldGetTheCrateSizesWrong()
        {
            // The reason JsMath exists: these are the cases the golden round-half-* pictures pin down.
            Assert.That(Math.Round(8.5), Is.EqualTo(8), "banker's rounding");
            Assert.That(Math.Floor(0.49999999999999994 + 0.5), Is.EqualTo(1), "floor(x + 0.5)");
            var options = new GenerationOptions();
            Assert.That(CrateSizes.For(1275, options).Min, Is.EqualTo(9));
            Assert.That(CrateSizes.For(1040, options).BigMin, Is.EqualTo(33));
            Assert.That(CrateSizes.For(1000, options).Max, Is.EqualTo(63));
        }

        [Test]
        public void CandidateSeedsPastIntMaxValueDoNotOverflow()
        {
            var options = new GenerationOptions { Seed = int.MaxValue, Shuffle = 0.4, Edges = OpenEdges.Sides, Candidates = 2, TargetWinRate = 0 };
            BuildResult built = LevelBuilder.Build(Level("sunset").Picture, options);
            Assert.That(built.Attempts.Select(a => a.Seed), Is.EqualTo(new[] { 2147483647L, 2147491566L }));
            Assert.That(Crates(built.Attempts[1].Tray), Is.EqualTo(Crates(LevelGenerator.Generate(Level("sunset").Picture, options.With(2147491566L - 4294967296L, 0.4)))),
                "the seed is taken modulo 2^32, so seed + 7919 is the same as its negative int32 twin");
        }

        // ---- generateLevel reproducibility (B-20, I-20) ----

        [TestCase(0)]
        [TestCase(0.2)]
        [TestCase(1)]
        [TestCase(2)]
        public void SameSeedSameTray(double shuffle)
        {
            PictureDefinition sunset = Level("sunset").Picture;
            foreach (OpenEdges edges in new[] { OpenEdges.Bottom, OpenEdges.Sides, OpenEdges.All })
                Assert.That(Crates(LevelGenerator.Generate(sunset, Opts(42, shuffle, edges))), Is.EqualTo(Crates(LevelGenerator.Generate(sunset, Opts(42, shuffle, edges)))));
        }

        [Test]
        public void AnotherSeedGivesAnotherTray()
        {
            PictureDefinition sunset = Level("sunset").Picture;
            Assert.That(Crates(LevelGenerator.Generate(sunset, Opts(1, 0.5))), Is.Not.EqualTo(Crates(LevelGenerator.Generate(sunset, Opts(2, 0.5)))));
        }

        [Test]
        public void TheShuffleOnlyReorders()
        {
            PictureDefinition sunset = Level("sunset").Picture;
            string Sorted(double shuffle) => string.Join(",", LevelGenerator.Generate(sunset, Opts(9, shuffle)).Columns.SelectMany(c => c).OrderBy(b => b.Id).Select(b => $"{b.Id}:{b.Color}:{b.Count}"));
            Assert.That(Sorted(0.7), Is.EqualTo(Sorted(0)));
            Assert.That(Sorted(3), Is.EqualTo(Sorted(0)));
            Assert.That(Crates(LevelGenerator.Generate(sunset, Opts(9, 3))), Is.Not.EqualTo(Crates(LevelGenerator.Generate(sunset, Opts(9, 0)))));
        }

        [Test]
        public void BuildIsReproducibleWinRateIncluded()
        {
            LevelDefinition planet = Level("ringed-planet");
            BuildResult a = LevelBuilder.Build(planet.Picture, GenerationOptions.For(planet));
            BuildResult b = LevelBuilder.Build(planet.Picture, GenerationOptions.For(planet));
            Assert.That(a.WinRate, Is.EqualTo(b.WinRate));
            Assert.That(Crates(a.Tray), Is.EqualTo(Crates(b.Tray)));
        }

        [Test]
        public void BuildsFirstCandidateIsGenerateWithTheLevelsOwnSeed()
        {
            LevelDefinition strawberry = Level("strawberry");
            GenerationOptions options = GenerationOptions.For(strawberry);
            options.Candidates = 1;
            BuildResult one = LevelBuilder.Build(strawberry.Picture, options);
            Assert.That(Crates(one.Tray), Is.EqualTo(Crates(LevelGenerator.Generate(strawberry.Picture, options))));
            Assert.That(one.WinRate, Is.EqualTo(RandomPlayer.WinRate(strawberry.Picture, strawberry.Edges, strawberry.SlotCount, one.Tray, 24, options.Seed)));
        }

        // ---- the ten levels (core.test.ts "levels", generator.test.ts golden) ----

        [Test]
        public void EveryLevelsTrayMatchesItsPictureAndGreedyPlayWinsIt()
        {
            foreach (string id in Repo.LevelIds)
            {
                LevelDefinition level = Level(id);
                for (int c = 0; c < level.Picture.Palette.Length; c++)
                    Assert.That(level.Tray.TrucksOf(c), Is.EqualTo(level.Picture.CountOf(c)), id + " color " + c);
                SolveResult greedy = GreedySolver.Solve(level);
                Assert.That(greedy.Solved, Is.True, id);
                Assert.That((greedy.Ticks, greedy.Taps), Is.EqualTo((level.DifficultyMetadata.Greedy.Ticks, level.DifficultyMetadata.Greedy.Taps)), id);
                Assert.That(RandomPlayer.WinRate(level, 24, level.Generation.Seed), Is.EqualTo(level.DifficultyMetadata.WinRate), id);
            }
        }

        [Test]
        public void LaterLevelsAreHarderForACarelessPlayer()
        {
            Assert.That(Level("strawberry").DifficultyMetadata.WinRate, Is.GreaterThan(Level("watermelon").DifficultyMetadata.WinRate));
        }

        // ---- tray rules (B-22, B-23, B-26, I-01, I-22) ----

        static IEnumerable<TestCaseData> TrayRuleCases()
        {
            foreach (string id in LevelRepository.LoadDirectory(ProjectPaths.Of("Assets/_CubeHaul/Levels/Content")).ThrowIfErrors().LevelIds)
            {
                yield return new TestCaseData(id, 0, -1.0, null).SetName($"TrayRules({id}, own settings)");
                yield return new TestCaseData(id, 1, 0.0, null).SetName($"TrayRules({id}, seed + 1, shuffle 0)");
                yield return new TestCaseData(id, 2, 3.0, OpenEdges.All).SetName($"TrayRules({id}, seed + 2, shuffle 3, all edges)");
            }
        }

        [TestCaseSource(nameof(TrayRuleCases))]
        public void TrayRules(string levelId, int seedOffset, double shuffle, OpenEdges? edges)
        {
            LevelDefinition level = Level(levelId);
            GenerationOptions options = GenerationOptions.For(level);
            options = options.With(options.Seed + seedOffset, shuffle < 0 ? options.Shuffle : shuffle);
            if (edges != null) options.Edges = edges.Value;
            PictureDefinition p = level.Picture;
            TrayDefinition tray = LevelGenerator.Generate(p, options);
            BlockDefinition[] crates = tray.Columns.SelectMany(c => c).ToArray();
            CrateSizes sizes = CrateSizes.For(p.CubeCount, options);
            for (int c = 0; c < p.Palette.Length; c++)
            {
                BlockDefinition[] mine = crates.Where(k => k.Color == c).ToArray();
                Assert.That(mine.Sum(k => k.Count), Is.EqualTo(p.CountOf(c)), "I-01 color " + c);
                foreach (BlockDefinition k in mine)
                {
                    Assert.That(k.Count, Is.InRange(1, sizes.Max));
                    if (k.Count < sizes.Min) Assert.That(mine, Has.Length.EqualTo(1), "a crate under the minimum is its color's only crate");
                }
            }
            CollectionAssert.AreEqual(Enumerable.Range(0, crates.Length), crates.Select(k => k.Id).OrderBy(x => x), "ids 0..n-1");
            Assert.That(tray.Columns.Length, Is.EqualTo(5));
            int[] lengths = tray.Columns.Select(c => c.Length).ToArray();
            Assert.That(lengths.Max() - lengths.Min(), Is.LessThanOrEqualTo(1));
        }

        [Test]
        public void CactusHasAFourTruckCrateBecauseItsFlowerCentreHasOnlyFourCubes()
        {
            LevelDefinition cactus = Level("cactus");
            CrateSizes sizes = CrateSizes.For(cactus.Picture.CubeCount, GenerationOptions.For(cactus));
            BlockDefinition[] small = cactus.Tray.Columns.SelectMany(c => c).Where(k => k.Count < sizes.Min).ToArray();
            Assert.That(small.Select(k => k.Count), Is.EqualTo(new[] { 4 }));
            Assert.That(cactus.Picture.CountOf(small[0].Color), Is.EqualTo(4));
        }

        [TestCase(3)]
        [TestCase(7)]
        public void HonoursTheColumnCount(int columns)
        {
            var options = Opts(3, 0.5);
            options.Columns = columns;
            TrayDefinition tray = LevelGenerator.Generate(Level("strawberry").Picture, options);
            int[] lengths = tray.Columns.Select(c => c.Length).ToArray();
            Assert.That(lengths.Length, Is.EqualTo(columns));
            Assert.That(lengths.Max() - lengths.Min(), Is.LessThanOrEqualTo(1));
        }

        [Test]
        public void Finding_F4_MinAndMaxBlockOverridesAreNotHonouredWhenTheyClash()
        {
            PictureDefinition p = Level("mushroom").Picture;
            var capped = Opts(4, 0, OpenEdges.All);
            capped.MinBlock = 20;
            capped.MaxBlock = 30;
            BlockDefinition[] a = LevelGenerator.Generate(p, capped).Columns.SelectMany(c => c).ToArray();
            Assert.That(a.Max(k => k.Count), Is.EqualTo(39));
            var floored = Opts(5, 0, OpenEdges.All);
            floored.MinBlock = 40;
            BlockDefinition[] b = LevelGenerator.Generate(p, floored).Columns.SelectMany(c => c).ToArray();
            Assert.That(b.Min(k => k.Count), Is.EqualTo(13));
            Assert.That((a.Sum(k => k.Count), b.Sum(k => k.Count)), Is.EqualTo((p.CubeCount, p.CubeCount)));
        }

        [Test]
        public void SplitCountAlwaysAddsUpAndShuffledIsAPermutation()
        {
            var rng = new Mulberry32(17);
            var sizes = new CrateSizes(6, 14, 24, 30, 0.3);
            for (int n = 1; n < 400; n += 7)
            {
                List<int> parts = LevelGenerator.SplitCount(n, sizes, rng);
                Assert.That(parts.Sum(), Is.EqualTo(n));
                Assert.That(parts.All(s => s >= 1 && s <= 30), Is.True);
            }
            int[] items = Enumerable.Range(0, 10).ToArray();
            int[] shuffled = LevelGenerator.Shuffled(items, rng);
            CollectionAssert.AreEquivalent(items, shuffled);
            CollectionAssert.AreEqual(Enumerable.Range(0, 10), items, "the input is not changed");
        }

        // ---- peelLayers (B-21) ----

        [Test]
        public void PeelLayerZeroIsWhatIsReachableAtTheStart()
        {
            PictureDefinition p = Pic("0.1", "011", "000");
            int[] layers = PeelLayers.Of(p, OpenEdges.Bottom);
            Board b = p.CreateBoard(OpenEdges.Bottom);
            int[] first = b.ReachableCells(0).Concat(b.ReachableCells(1)).OrderBy(x => x).ToArray();
            CollectionAssert.AreEqual(Enumerable.Range(0, layers.Length).Where(i => layers[i] == 0), first);
            Assert.That(layers[1], Is.EqualTo(-1));
            for (int i = 0; i < p.Cells.Length; i++)
                if (p.Cells[i] != Cells.Empty) Assert.That(layers[i], Is.GreaterThanOrEqualTo(0));
        }

        // ---- buildLevel fallback (B-24) ----

        [Test]
        public void WhenNothingCanBeVerifiedBuildReturnsTheUnshuffledTrayAndSaysSo()
        {
            PictureDefinition zebra = Pic("1", "0", "1", "0");
            var options = new GenerationOptions { Seed = 1, Shuffle = 0.5, Edges = OpenEdges.Bottom, SlotCount = 1 };
            BuildResult built = LevelBuilder.Build(zebra, options);
            Assert.That((built.Path, built.Verified, built.WinRate), Is.EqualTo((BuildPath.Unverified, false, 0.0)));
            Assert.That(GreedySolver.Solve(zebra, OpenEdges.Bottom, 1, built.Tray).Solved, Is.False);
            Assert.That(Crates(built.Tray), Is.EqualTo(Crates(LevelGenerator.Generate(zebra, options.With(1, 0)))));
            Assert.That(built.Attempts.Select(a => a.Phase).Distinct(), Is.EqualTo(new[] { BuildPath.Candidate, BuildPath.Fallback, BuildPath.Unverified }));
            Assert.That(built.Attempts.Count, Is.EqualTo(16 + 8 + 1));
        }

        // ---- solveGreedy ----

        [Test]
        public void GreedyWinsAOneCrateLevelOneTickPerCube() =>
            Assert.That(Greedy(Solid(3, 1), "0:3", 1), Is.EqualTo(new SolveResult(true, 3, 1)));

        [Test]
        public void GreedyTapsACrateThatCanEatFirst() =>
            Assert.That(Greedy(Pic("1", "0"), "1:1|0:1", 1), Is.EqualTo(new SolveResult(true, 2, 2)));

        [Test]
        public void GreedyIsForcedToTapABlockedCrateAndReportsTheLoss() =>
            Assert.That(Greedy(Pic("1", "0"), "1:1,0:1", 1), Is.EqualTo(new SolveResult(false, 1, 1)));

        [Test]
        public void GreedyGivesUpAfterMaxTicks()
        {
            Assert.That(Greedy(Solid(1, 5), "0:5", 1), Is.EqualTo(new SolveResult(true, 5, 1)));
            Assert.That(Greedy(Solid(1, 5), "0:5", 1, 2), Is.EqualTo(new SolveResult(false, 2, 1)));
        }

        [Test]
        public void SolversDoNotChangeTheLevelTheyAreGiven()
        {
            LevelDefinition level = Level("cat");
            LevelDefinition before = level.DeepCopy();
            GreedySolver.Solve(level);
            RandomPlayer.WinRate(level, 3);
            Assert.That(level.ContentEquals(before), Is.True);
        }

        [Test]
        public void ChooseTapBreaksTiesTowardTheFirstColumn()
        {
            GameEngine g = GameFactory.CreateGame(Solid(4, 1), OpenEdges.Bottom, 2, Tray("0:2|0:2"));
            Assert.That(GreedySolver.ChooseTap(g.State, false), Is.EqualTo(0));
        }

        // ---- randomWinRate ----

        [Test]
        public void RandomWinRateIsOneWhenNoOrderLosesAndZeroWhenEveryOrderLoses()
        {
            Assert.That(WinRate(Solid(2, 1), "0:1|0:1", 2), Is.EqualTo(1));
            Assert.That(WinRate(Pic("1", "0"), "1:1,0:1", 1), Is.EqualTo(0));
        }

        [Test]
        public void RandomWinRateIsTheShareOfGamesSeededSeedPlus101K()
        {
            LevelDefinition sunset = Level("sunset");
            double One(long seed) => RandomPlayer.WinRate(sunset, 1, seed);
            double[] parts = Enumerable.Range(0, 7).Select(k => One(7 + k * 101)).ToArray();
            Assert.That(RandomPlayer.WinRate(sunset, 7, 7), Is.EqualTo(parts.Sum() / 7).Within(1e-12));
            Assert.That(parts.All(p => p == 0 || p == 1), Is.True);
            Assert.That(RandomPlayer.WinRate(sunset, 24, 5), Is.EqualTo(RandomPlayer.WinRate(sunset, 24, 5)));
        }

        [Test]
        public void ATrayWithTooFewTrucksEndsInsteadOfLoopingForever()
        {
            // Two cubes, one truck: TypeScript's randomWinRate would loop forever here. Never produced by the generator.
            Assert.That(WinRate(Solid(2, 1), "0:1", 2, 3), Is.EqualTo(0));
            Assert.That(Greedy(Solid(2, 1), "0:1", 2).Solved, Is.False);
        }

        // ---- C# API ----

        [Test]
        public void BuildCanBeCancelled()
        {
            LevelDefinition cat = Level("cat");
            using (var cancel = new CancellationTokenSource())
            {
                cancel.Cancel();
                Assert.That(() => LevelBuilder.Build(cat.Picture, GenerationOptions.For(cat), cancel.Token), Throws.InstanceOf<OperationCanceledException>());
            }
        }

        [Test]
        public void EnsureTrayKeepsAStaticTrayAndBakesAGeneratedOne()
        {
            LevelDefinition strawberry = Level("strawberry");
            Assert.That(LevelBaker.EnsureTrayAsync(strawberry).Result, Is.SameAs(strawberry));

            LevelReadResult heart = LevelDefinitionReader.Read(System.IO.File.ReadAllText(ProjectPaths.Of("LevelFormat/examples/heart.generated.level.json")));
            Assert.That(heart.Ok, Is.True, heart.Describe());
            Assert.That(heart.Level.Tray, Is.Null);
            LevelDefinition baked = LevelBaker.EnsureTrayAsync(heart.Level).Result;
            Assert.That(heart.Level.Tray, Is.Null, "the input is not changed");
            Assert.That(baked.Tray, Is.Not.Null);
            Assert.That(baked.DifficultyMetadata.RandomGames, Is.EqualTo(24));
            Assert.That(GreedySolver.Solve(baked).Solved, Is.True);
            BakeResult again = LevelBaker.Bake(heart.Level);
            Assert.That(Crates(again.Level.Tray), Is.EqualTo(Crates(baked.Tray)), "baking is deterministic");
            Assert.That(baked.Tray.Columns.SelectMany(c => c).Sum(k => k.Count), Is.EqualTo(heart.Level.Picture.CubeCount));
        }

        [Test]
        public void BadOptionsAreRejected()
        {
            PictureDefinition p = Solid(2, 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => LevelGenerator.Generate(p, new GenerationOptions { Columns = 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => LevelGenerator.Generate(p, new GenerationOptions { MinBlock = 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => LevelGenerator.Generate(p, new GenerationOptions { Shuffle = double.NaN }));
            Assert.Throws<InvalidOperationException>(() => GenerationOptions.For(new LevelDefinition { LevelId = "x" }));
            Assert.Throws<ArgumentOutOfRangeException>(() => RandomPlayer.WinRate(p, OpenEdges.Bottom, 1, Tray("0:2"), 0));
        }
    }
}
