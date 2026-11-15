using System.IO;
using System.Text;
using CubeHaul.Domain;
using CubeHaul.Levels;
using NUnit.Framework;

namespace CubeHaul.Tests.EditMode
{
    /// <summary>DeepCopy and the repository's copies keep every caller's changes to itself.</summary>
    public class DeepCopyTests
    {
        static LevelDefinition Strawberry() =>
            LevelDefinitionReader.Read(File.ReadAllText(ProjectPaths.Of("Assets/_CubeHaul/Levels/Content/level-01-strawberry.level.json"), Encoding.UTF8)).Level;

        [Test]
        public void ACopyHasTheSameContentButSharesNothingMutable()
        {
            LevelDefinition a = Strawberry();
            LevelDefinition b = a.DeepCopy();
            Assert.That(b.ContentEquals(a), Is.True);
            Assert.That(b, Is.Not.SameAs(a));
            Assert.That(b.Picture, Is.Not.SameAs(a.Picture));
            Assert.That(b.Picture.Cells, Is.Not.SameAs(a.Picture.Cells));
            Assert.That(b.Picture.Palette, Is.Not.SameAs(a.Picture.Palette));
            Assert.That(b.Tray.Columns, Is.Not.SameAs(a.Tray.Columns));
            Assert.That(b.Tray.Columns[0], Is.Not.SameAs(a.Tray.Columns[0]));
            Assert.That(b.Tray.Columns[0][0], Is.Not.SameAs(a.Tray.Columns[0][0]));
            Assert.That(b.Generation, Is.Not.SameAs(a.Generation));
            Assert.That(b.DifficultyMetadata, Is.Not.SameAs(a.DifficultyMetadata));
            Assert.That(b.DifficultyMetadata.Greedy, Is.Not.SameAs(a.DifficultyMetadata.Greedy));
        }

        [Test]
        public void ChangingTheCopyLeavesTheOriginalAlone()
        {
            LevelDefinition original = Strawberry();
            LevelDefinition copy = original.DeepCopy();
            copy.Picture.Cells[0] = 3;
            copy.Picture.Palette[0] = "#000000";
            copy.Tray.Columns[0][0].Count = 1;          // what a game does when a truck leaves
            copy.Tray.Columns[1] = new BlockDefinition[0];
            copy.Generation.Seed = 999;
            copy.DifficultyMetadata.Greedy.Ticks = 0;
            copy.Edges = OpenEdges.Bottom;

            LevelDefinition fresh = Strawberry();
            Assert.That(original.ContentEquals(fresh), Is.True);
            Assert.That(copy.ContentEquals(fresh), Is.False);
            Assert.That(original.Tray.Columns[0][0].Count, Is.EqualTo(10));
        }

        [Test]
        public void ConstructorsCopyTheArraysTheyAreGiven()
        {
            int[] cells = { 0, 1, -1, 0 };
            string[] palette = { "#ff0000", "#00ff00" };
            var picture = new PictureDefinition(2, 2, cells, palette);
            cells[0] = 1;
            palette[0] = "#000000";
            Assert.That(picture.Cells[0], Is.EqualTo(0));
            Assert.That(picture.Palette[0], Is.EqualTo("#ff0000"));

            var columns = new[] { new[] { new BlockDefinition(0, 0, 2) } };
            var tray = new TrayDefinition(columns);
            columns[0][0].Count = 9;
            Assert.That(tray.Columns[0][0].Count, Is.EqualTo(2));
        }

        [Test]
        public void TheRepositoryNeverHandsOutItsOwnLevels()
        {
            LevelRepository repo = LevelRepository.LoadDirectory(ProjectPaths.Of("Assets/_CubeHaul/Levels/Content")).ThrowIfErrors();
            LevelDefinition first = repo.Get("strawberry");
            first.Tray.Columns[0][0].Count = 0;
            first.Picture.Cells[100] = 2;
            first.Name = "Changed";

            LevelDefinition second = repo.Get("strawberry");
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.Tray.Columns[0][0].Count, Is.EqualTo(10));
            Assert.That(second.Name, Is.EqualTo("Strawberry"));
            Assert.That(second.ContentEquals(repo.GetAt(0)), Is.True);
            Assert.That(second.ContentEquals(Strawberry()), Is.True);
        }
    }
}
