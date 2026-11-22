using System.Diagnostics;
using System.IO;
using CubeHaul.Levels;
using CubeHaul.Levels.Generation;
using CubeHaul.Tests.LevelFormat;
using NUnit.Framework;

namespace CubeHaul.Tests.Domain
{
    /// <summary>
    /// How long baking each level takes (docs/LEVEL_GENERATION.md §5). Reported, not asserted tightly: machines differ.
    /// The bound only catches an accidental slowdown by an order of magnitude.
    /// </summary>
    public class GeneratorTimingTests
    {
        [Test]
        public void BakingEveryLevelTakesSeconds()
        {
            string dir = Path.Combine(Path.GetDirectoryName(Fixtures.Root), "Assets", "_CubeHaul", "Levels", "Content");
            LevelRepository repo = LevelRepository.LoadDirectory(dir).ThrowIfErrors();
            LevelBaker.Bake(repo.Get("strawberry")); // warm up the JIT
            var total = Stopwatch.StartNew();
            foreach (string id in repo.LevelIds)
            {
                LevelDefinition level = repo.Get(id);
                var watch = Stopwatch.StartNew();
                BakeResult baked = LevelBaker.Bake(level);
                watch.Stop();
                int games = 0;
                foreach (BuildAttempt a in baked.Build.Attempts) games += 1 + (a.Games?.Length ?? 0);
                TestContext.WriteLine($"{id,-14} {level.Picture.Width}x{level.Picture.Height} {baked.Build.Attempts.Count,2} trays, {games,4} games: {watch.ElapsedMilliseconds,6} ms");
            }
            total.Stop();
            TestContext.WriteLine($"all ten: {total.ElapsedMilliseconds} ms");
            Assert.That(total.ElapsedMilliseconds, Is.LessThan(120_000));
        }
    }
}
