using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CubeHaul.Levels;
using NUnit.Framework;

namespace CubeHaul.Tests.EditMode
{
    public class LevelRepositoryTests
    {
        static readonly string Dir = ProjectPaths.Of("Assets/_CubeHaul/Levels/Content");
        static string File1 => File.ReadAllText(Path.Combine(Dir, "level-01-strawberry.level.json"), Encoding.UTF8);
        static string File2 => File.ReadAllText(Path.Combine(Dir, "level-02-mushroom.level.json"), Encoding.UTF8);

        [Test]
        public void LoadsTheTenLevelsInPlayOrder()
        {
            LevelRepository repo = LevelRepository.LoadDirectory(Dir);
            Assert.That(repo.HasErrors, Is.False, string.Join("\n", repo.Errors));
            CollectionAssert.AreEqual(
                new[] { "strawberry", "mushroom", "sunset", "ringed-planet", "cat", "cactus", "rocket", "rubber-duck", "ice-cream", "watermelon" },
                repo.LevelIds);
            Assert.That(repo.Count, Is.EqualTo(10));
            Assert.That(repo.IndexOf("cat"), Is.EqualTo(4));
            Assert.That(repo.GetAt(4).Name, Is.EqualTo("Cat"));
            Assert.That(repo.Get("ringed-planet").Picture.Width, Is.EqualTo(46));
        }

        [Test]
        public void ABadFileIsLeftOutAndReportedByName()
        {
            LevelRepository repo = LevelRepository.Load(new[]
            {
                new LevelSource("level-01.json", File1),
                new LevelSource("broken.json", "{ \"schemaVersion\": 1, }"),
                new LevelSource("level-02.json", File2),
            });
            CollectionAssert.AreEqual(new[] { "strawberry", "mushroom" }, repo.LevelIds);
            Assert.That(repo.Errors.Count, Is.EqualTo(1));
            Assert.That(repo.Errors[0].SourceName, Is.EqualTo("broken.json"));
            Assert.That(repo.Errors[0].Issues[0].Code, Is.EqualTo(IssueCodes.JsonSyntax));

            var e = Assert.Throws<LevelLoadException>(() => repo.ThrowIfErrors());
            StringAssert.Contains("1 level file could not be loaded", e.Message);
            StringAssert.Contains("broken.json", e.Message);
            StringAssert.Contains("JSON_SYNTAX at (root)", e.Message);
            StringAssert.Contains("line 1, column", e.Message);
        }

        [Test]
        public void ASecondLevelWithTheSameIdIsRejected()
        {
            LevelRepository repo = LevelRepository.Load(new[] { new LevelSource("a.json", File1), new LevelSource("b.json", File1) });
            Assert.That(repo.Count, Is.EqualTo(1));
            LevelLoadError error = repo.Errors.Single();
            Assert.That(error.SourceName, Is.EqualTo("b.json"));
            Assert.That(error.Issues.Single().Code, Is.EqualTo(IssueCodes.DuplicateLevelId));
            StringAssert.Contains("\"strawberry\" is already used by level 1", error.ToString());
        }

        [Test]
        public void AnUnknownIdSaysWhichIdsExist()
        {
            LevelRepository repo = LevelRepository.Load(new[] { new LevelSource("a.json", File1) });
            Assert.That(repo.TryGet("nope", out LevelDefinition none), Is.False);
            Assert.That(none, Is.Null);
            var e = Assert.Throws<KeyNotFoundException>(() => repo.Get("nope"));
            StringAssert.Contains("known: strawberry", e.Message);
            Assert.That(repo.IndexOf("nope"), Is.EqualTo(-1));
        }
    }
}
