using System.Linq;
using NUnit.Framework;

namespace CubeHaul.Data.Tests
{
    /// <summary>What the C# model holds for the example files; the values match docs/TS_BASELINE.md.</summary>
    public class ModelTests
    {
        static LevelDefinition Example(string file)
        {
            LevelReadResult r = LevelDefinitionReader.Read(Fixtures.Read("examples", file));
            Assert.That(r.Ok, Is.True, string.Join("\n", r.Errors));
            return r.Level;
        }

        [Test]
        public void StrawberryHasThePictureTrayAndDifficultyOfBuiltInLevel1()
        {
            LevelDefinition level = Example("strawberry.level.json");
            Assert.That(level.SchemaVersion, Is.EqualTo(1));
            Assert.That(level.LevelId, Is.EqualTo("strawberry"));
            Assert.That(level.ThemeId, Is.EqualTo("default"));
            Assert.That(level.Name, Is.EqualTo("Strawberry"));
            Assert.That(level.Edges, Is.EqualTo(EdgeMode.All));
            Assert.That(level.SlotCount, Is.EqualTo(5));

            PictureDefinition p = level.Picture;
            Assert.That((p.Width, p.Height), Is.EqualTo((29, 29)));
            CollectionAssert.AreEqual(new[] { "#ec4a5a", "#48c870", "#165030", "#ffe27a" }, p.Palette);
            Assert.That(p.Cells.Length, Is.EqualTo(29 * 29));
            Assert.That(p.Cells.Count(c => c != LevelDefinition.Empty), Is.EqualTo(527));
            Assert.That(p.CellAt(13, 0), Is.EqualTo(2)); // the stem, top row
            Assert.That(p.CellAt(0, 0), Is.EqualTo(LevelDefinition.Empty));

            Assert.That(level.HasStaticTray, Is.True);
            CrateDefinition[][] cols = level.Tray.Columns;
            CollectionAssert.AreEqual(new[] { 9, 9, 9, 9, 9 }, cols.Select(c => c.Length));
            Assert.That(cols.Sum(c => c.Length), Is.EqualTo(45));
            Assert.That(cols.SelectMany(c => c).Sum(k => k.Count), Is.EqualTo(527));
            Assert.That((cols[0][0].Id, cols[0][0].Color, cols[0][0].Count), Is.EqualTo((2, 0, 10)));
            for (int color = 0; color < p.Palette.Length; color++)
                Assert.That(cols.SelectMany(c => c).Where(k => k.Color == color).Sum(k => k.Count), Is.EqualTo(p.Cells.Count(x => x == color)));

            GenerationSettings g = level.Generation;
            Assert.That((g.Seed, g.Shuffle, g.TargetWinRate, g.Columns, g.BigChance, g.Candidates), Is.EqualTo((11, 0.2, 1.0, 5, 0.15, 16)));
            Assert.That(g.MinBlock, Is.Null);
            Assert.That(g.MaxBlock, Is.Null);

            DifficultyMetadata d = level.Difficulty;
            Assert.That((d.WinRate, d.Label, d.RandomGames), Is.EqualTo((1.0, DifficultyLabel.Easy, (int?)24)));
            Assert.That((d.Greedy.Solved, d.Greedy.Ticks, d.Greedy.Taps), Is.EqualTo((true, 114, 45)));
        }

        [Test]
        public void StaticOnlyLevelHasNoGenerationOrDifficulty()
        {
            LevelDefinition level = Example("ring.static.level.json");
            Assert.That(level.HasStaticTray, Is.True);
            Assert.That(level.Generation, Is.Null);
            Assert.That(level.Difficulty, Is.Null);
            CollectionAssert.AreEqual(new[] { 0, 0, 0, 0, 1, 0, 0, 0, 0 }, level.Picture.Cells);
        }

        [Test]
        public void GenerationOnlyLevelHasNoTrayAndTakesTheDefaults()
        {
            LevelDefinition level = Example("heart.generated.level.json");
            Assert.That(level.HasStaticTray, Is.False);
            Assert.That(level.Tray, Is.Null);
            Assert.That(level.Edges, Is.EqualTo(EdgeMode.Bottom));
            GenerationSettings g = level.Generation;
            Assert.That((g.Seed, g.Shuffle, g.TargetWinRate), Is.EqualTo((7, 0.5, 0.8)));
            Assert.That((g.Columns, g.BigChance, g.Candidates), Is.EqualTo((5, 0.15, 16)));
            Assert.That(level.Picture.Cells.Count(c => c == LevelDefinition.Empty), Is.EqualTo(15));
        }

        [TestCase(1.0, DifficultyLabel.Easy)]
        [TestCase(0.9, DifficultyLabel.Easy)]
        [TestCase(0.8999, DifficultyLabel.Medium)]
        [TestCase(0.65, DifficultyLabel.Medium)]
        [TestCase(0.6499, DifficultyLabel.Hard)]
        [TestCase(0.45, DifficultyLabel.Hard)]
        [TestCase(0.4499, DifficultyLabel.Expert)]
        [TestCase(0.0, DifficultyLabel.Expert)]
        public void DifficultyLabelThresholds(double winRate, DifficultyLabel label)
        {
            Assert.That(DifficultyLabels.FromWinRate(winRate), Is.EqualTo(label));
        }
    }
}
