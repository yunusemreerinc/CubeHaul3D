using System;
using System.IO;
using System.Linq;
using CubeHaul.Domain;
using CubeHaul.Levels;
using NUnit.Framework;

namespace CubeHaul.Tests.EditMode
{
    /// <summary>The game's level files (exported from cube-hunt, docs/GOLDEN_FIXTURES.md) load with the game's reader.</summary>
    public class LevelContentTests
    {
        static readonly string[] Ids = { "strawberry", "mushroom", "sunset", "ringed-planet", "cat", "cactus", "rocket", "rubber-duck", "ice-cream", "watermelon" };

        [Test]
        public void TheTenBuiltInLevelsLoadInOrder()
        {
            string[] files = Directory.GetFiles(ProjectPaths.Of("Assets/_CubeHaul/Levels/Content"), "*.level.json")
                .Select(Path.GetFileName)
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToArray();
            Assert.That(files.Length, Is.EqualTo(Ids.Length));
            for (int i = 0; i < files.Length; i++)
            {
                string text = File.ReadAllText(ProjectPaths.Of("Assets/_CubeHaul/Levels/Content/" + files[i]));
                LevelReadResult result = LevelDefinitionReader.Read(text);
                Assert.That(result.Ok, Is.True, files[i] + ": " + string.Join("; ", result.Errors.Select(e => e.ToString())));
                Assert.That(result.Level.LevelId, Is.EqualTo(Ids[i]));
                Assert.That(result.Level.HasStaticTray, Is.True);
            }
        }

        [Test]
        public void StrawberryHasOneTruckPerCube()
        {
            LevelDefinition level = LevelDefinitionReader.Read(File.ReadAllText(ProjectPaths.Of("Assets/_CubeHaul/Levels/Content/level-01-strawberry.level.json"))).Level;
            int cubes = level.Picture.Cells.Count(c => c != Cells.Empty);
            int trucks = level.Tray.Columns.SelectMany(c => c).Sum(k => k.Count);
            Assert.That(cubes, Is.EqualTo(527));
            Assert.That(trucks, Is.EqualTo(cubes));
            Assert.That(LevelDefinition.Empty, Is.EqualTo(Cells.Empty));
        }
    }
}
