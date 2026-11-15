using System.IO;
using System.Linq;
using System.Text;
using CubeHaul.Domain;
using CubeHaul.Levels;
using NUnit.Framework;

namespace CubeHaul.Tests.EditMode
{
    /// <summary>Two real level files read field by field (values as exported from cube-hunt, docs/TS_BASELINE.md).</summary>
    public class RealLevelTests
    {
        static LevelDefinition Load(string file)
        {
            LevelReadResult r = LevelDefinitionReader.Read(File.ReadAllText(ProjectPaths.Of("Assets/_CubeHaul/Levels/Content/" + file), Encoding.UTF8));
            Assert.That(r.Ok, Is.True, r.Describe());
            CollectionAssert.IsEmpty(r.Warnings);
            return r.Level;
        }

        [Test]
        public void Sunset()
        {
            LevelDefinition level = Load("level-03-sunset.level.json");
            Assert.That(level.SchemaVersion, Is.EqualTo(1));
            Assert.That(level.LevelId, Is.EqualTo("sunset"));
            Assert.That(level.ThemeId, Is.EqualTo("default"));
            Assert.That(level.Name, Is.EqualTo("Sunset"));
            Assert.That(level.Edges, Is.EqualTo(OpenEdges.Bottom));
            Assert.That(level.SlotCount, Is.EqualTo(5));

            PictureDefinition p = level.Picture;
            Assert.That(p.Width, Is.EqualTo(36));
            Assert.That(p.Height, Is.EqualTo(36));
            CollectionAssert.AreEqual(new[] { "#7a5cc9", "#f07aa8", "#ff6d1a", "#ffe63d", "#2f7fd6", "#86d6f2" }, p.Palette);
            CollectionAssert.AreEqual(new[] { 176, 256, 216, 168, 402, 78 }, Enumerable.Range(0, 6).Select(p.CountOf));
            Assert.That(p.CubeCount, Is.EqualTo(1296));
            Assert.That(p.CellAt(0, 0), Is.EqualTo(0));   // purple sky, top left
            Assert.That(p.CellAt(18, 18), Is.EqualTo(3)); // the sun
            Assert.That(p.CellAt(0, 35), Is.EqualTo(4));  // the sea, bottom left
            Assert.That(Enumerable.Range(0, 36).All(x => p.CellAt(x, 10) == 1), Is.True, "row 10 is the pink band");

            BlockDefinition[][] cols = level.Tray.Columns;
            CollectionAssert.AreEqual(new[] { 10, 10, 10, 9, 10 }, cols.Select(c => c.Length));
            Assert.That(level.Tray.BlockCount, Is.EqualTo(49));
            Assert.That((cols[0][0].Id, cols[0][0].Color, cols[0][0].Count), Is.EqualTo((47, 5, 64)));
            Assert.That((cols[0][9].Id, cols[0][9].Color, cols[0][9].Count), Is.EqualTo((3, 0, 23)));
            Assert.That(cols.SelectMany(c => c).Min(b => b.Count), Is.EqualTo(9));

            GenerationSettings g = level.Generation;
            Assert.That((g.Seed, g.Shuffle, g.TargetWinRate, g.Columns, g.BigChance, g.Candidates),
                Is.EqualTo((12, 0.2, (double?)0.6, (int?)5, (double?)0.25, (int?)16)));
            Assert.That(g.MinBlock, Is.Null);
            DifficultyMetadata d = level.DifficultyMetadata;
            Assert.That(d.WinRate, Is.EqualTo(13.0 / 24));
            Assert.That(d.Label, Is.EqualTo(DifficultyLabel.Hard));
            Assert.That((d.Greedy.Solved, d.Greedy.Ticks, d.Greedy.Taps), Is.EqualTo((true, 329, 49)));
        }

        [Test]
        public void Cactus()
        {
            LevelDefinition level = Load("level-06-cactus.level.json");
            Assert.That(level.LevelId, Is.EqualTo("cactus"));
            Assert.That((level.Picture.Width, level.Picture.Height), Is.EqualTo((40, 43)));
            CollectionAssert.AreEqual(new[] { "#8ed0f5", "#4cbb4f", "#14522a", "#ff4fa0", "#ffd21f", "#b84a24" }, level.Picture.Palette);
            CollectionAssert.AreEqual(new[] { 967, 319, 177, 33, 4, 220 }, Enumerable.Range(0, 6).Select(level.Picture.CountOf));
            Assert.That(level.Picture.CellAt(14, 10), Is.EqualTo(2)); // the cactus outline
            Assert.That(level.Picture.CellAt(15, 10), Is.EqualTo(1));

            BlockDefinition[][] cols = level.Tray.Columns;
            CollectionAssert.AreEqual(new[] { 15, 14, 14, 14, 15 }, cols.Select(c => c.Length));
            // The flower's yellow centre has only 4 cubes, so it is one block of 4 (TS_BASELINE §4).
            BlockDefinition yellow = cols.SelectMany(c => c).Single(b => b.Color == 4);
            Assert.That((yellow.Id, yellow.Count), Is.EqualTo((62, 4)));
            Assert.That(cols[0].Last(), Is.SameAs(yellow));

            Assert.That(level.Generation.Shuffle, Is.EqualTo(1));
            Assert.That(level.DifficultyMetadata.Label, Is.EqualTo(DifficultyLabel.Medium));
            Assert.That(level.DifficultyMetadata.WinRate, Is.EqualTo(19.0 / 24));
        }
    }
}
