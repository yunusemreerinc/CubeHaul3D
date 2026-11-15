using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CubeHaul.Levels.Json;
using NUnit.Framework;

using CubeHaul.Levels;

namespace CubeHaul.Tests.LevelFormat
{
    /// <summary>
    /// The golden fixtures (docs/GOLDEN_FIXTURES.md) as the C# side will use them: intact, readable, and agreeing with
    /// the format and hash definitions. The rule-by-rule replay comes with the C# Core port.
    /// </summary>
    public class GoldenFixtureTests
    {
        static readonly string Repo = Path.GetDirectoryName(Fixtures.Root);
        const string GoldenDir = "Assets/_CubeHaul/Tests/Fixtures/Golden";
        const string LevelsDir = "Assets/_CubeHaul/Levels/Content";

        static string ReadRepo(string path) => new UTF8Encoding(false).GetString(File.ReadAllBytes(Path.Combine(Repo, path)));
        static JsonValue Json(string path) => JsonParser.Parse(ReadRepo(path));

        static IEnumerable<string> ScenarioFiles() =>
            Directory.GetFiles(Path.Combine(Repo, GoldenDir, "scenarios"), "*.json").Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal);

        static IEnumerable<string> LevelFiles() =>
            Directory.GetFiles(Path.Combine(Repo, LevelsDir), "*.json").Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal);

        /// <summary>FNV-1a 32 over int32 values fed as 4 little-endian bytes (scripts/golden/hash.ts).</summary>
        public static string Fnv1a(IEnumerable<int> values)
        {
            uint h = 2166136261;
            foreach (int v in values)
            {
                uint x = unchecked((uint)v);
                for (int k = 0; k < 4; k++)
                {
                    h ^= (x >> (8 * k)) & 0xff;
                    h = unchecked(h * 16777619);
                }
            }
            return h.ToString("x8");
        }

        [Test]
        public void ManifestMatchesEveryFileByteForByte()
        {
            JsonValue manifest = Json(GoldenDir + "/manifest.json");
            Assert.That(manifest.Get("format").String, Is.EqualTo("cubehaul.golden.manifest"));
            var listed = new List<string>();
            using (var sha = SHA256.Create())
            {
                foreach (JsonValue f in manifest.Get("files").Items)
                {
                    string path = f.Get("path").String;
                    listed.Add(path);
                    byte[] bytes = File.ReadAllBytes(Path.Combine(Repo, path));
                    Assert.That(bytes.Length, Is.EqualTo((int)f.Get("bytes").Number), path);
                    string hex = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                    Assert.That(hex, Is.EqualTo(f.Get("sha256").String), path + " (line endings changed?)");
                    Assert.That(Array.IndexOf(bytes, (byte)'\r'), Is.EqualTo(-1), path + " has CR line endings");
                }
            }
            var onDisk = Directory.GetFiles(Path.Combine(Repo, GoldenDir), "*.json", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(Path.Combine(Repo, LevelsDir), "*.json"))
                .Select(p => Path.GetRelativePath(Repo, p).Replace('\\', '/'))
                .Where(p => !p.EndsWith("manifest.json"))
                .OrderBy(p => p, StringComparer.Ordinal);
            CollectionAssert.AreEqual(onDisk, listed.OrderBy(p => p, StringComparer.Ordinal));
        }

        [Test]
        public void TheTenBuiltInLevelsReadWithoutErrorsOrWarnings()
        {
            string[] ids = { "strawberry", "mushroom", "sunset", "ringed-planet", "cat", "cactus", "rocket", "rubber-duck", "ice-cream", "watermelon" };
            string[] files = LevelFiles().ToArray();
            Assert.That(files.Length, Is.EqualTo(10));
            int[] cubes = { 527, 1296, 1296, 2116, 1849, 1720, 1794, 1849, 1656, 1610 };
            int[] crates = { 45, 61, 49, 66, 64, 72, 65, 60, 62, 62 };
            for (int i = 0; i < 10; i++)
            {
                Assert.That(files[i], Is.EqualTo($"level-{i + 1:00}-{ids[i]}.level.json"));
                LevelReadResult r = LevelDefinitionReader.Read(ReadRepo(LevelsDir + "/" + files[i]));
                Assert.That(r.Ok, Is.True, files[i] + ": " + string.Join("; ", r.Errors));
                CollectionAssert.IsEmpty(r.Warnings, files[i]);
                LevelDefinition level = r.Level;
                Assert.That(level.LevelId, Is.EqualTo(ids[i]));
                Assert.That(level.Picture.Cells.Count(c => c >= 0), Is.EqualTo(cubes[i]), ids[i]);
                Assert.That(level.Tray.Columns.Sum(c => c.Length), Is.EqualTo(crates[i]), ids[i]);
                Assert.That(level.Generation, Is.Not.Null);
                Assert.That(level.DifficultyMetadata, Is.Not.Null);
            }
        }

        [Test]
        public void EveryGoldenFileParsesAndNamesItsFormatAndSource()
        {
            foreach (string path in Directory.GetFiles(Path.Combine(Repo, GoldenDir), "*.json", SearchOption.AllDirectories))
            {
                JsonValue v = JsonParser.Parse(new UTF8Encoding(false).GetString(File.ReadAllBytes(path)));
                StringAssert.StartsWith("cubehaul.golden.", v.Get("format").String, path);
                Assert.That(v.Get("version").Number, Is.EqualTo(1), path);
                Assert.That(v.Get("source").Get("commit").String, Is.EqualTo("559dfd90977b1e15367bd7de25aec9fbae4b52f6"), path);
            }
        }

        [Test]
        public void RngValuesAreUint32()
        {
            JsonValue rng = Json(GoldenDir + "/rng.json");
            foreach (JsonValue s in rng.Get("seeds").Items)
            {
                Assert.That(s.Get("values").Items.Count, Is.EqualTo(256));
                foreach (JsonValue v in s.Get("values").Items)
                    Assert.That(v.IsInteger && v.Number >= 0 && v.Number <= uint.MaxValue, Is.True);
            }
        }

        [TestCaseSource(nameof(ScenarioFiles))]
        public void ScenarioIsWellFormed(string file)
        {
            JsonValue s = Json(GoldenDir + "/scenarios/" + file);
            Assert.That(s.Get("name").String + ".json", Is.EqualTo(file));
            LevelReadResult level = s.Has("level")
                ? LevelValidator.Validate(s.Get("level"))
                : LevelDefinitionReader.Read(ReadRepo(s.Get("levelFile").String));
            Assert.That(level.Ok, Is.True, string.Join("; ", level.Errors));

            IReadOnlyList<JsonValue> steps = s.Get("steps").Items;
            Assert.That(steps[0].Get("op").String, Is.EqualTo("start"));
            var ops = new HashSet<string> { "start", "tap", "tick", "step", "collect", "addSlot" };
            int colors = level.Level.Picture.Palette.Length;
            int cubes = level.Level.Picture.Cells.Count(c => c >= 0);
            Assert.That(steps[0].Get("state").Get("remaining").Number, Is.EqualTo(cubes));
            foreach (JsonValue step in steps)
            {
                Assert.That(ops.Contains(step.Get("op").String), Is.True);
                JsonValue state = step.Get("state");
                Assert.That(state.Get("pickTarget").Items.Count, Is.EqualTo(colors));
                Assert.That(state.Get("reachableCount").Items.Count, Is.EqualTo(colors));
                Assert.That(state.Get("boardHash").String, Does.Match("^[0-9a-f]{8}$"));
                Assert.That(state.Get("slotCanEat").Items.Count, Is.EqualTo(state.Get("slots").Items.Count));
            }
        }

        [TestCaseSource(nameof(ScenarioFiles))]
        public void WonScenariosEndWithTheHashOfAClearedBoard(string file)
        {
            // The C# side can compute this without the rules: every cell empty (-1), open (1) and unclaimed (0).
            JsonValue s = Json(GoldenDir + "/scenarios/" + file);
            JsonValue last = s.Get("steps").Items.Last().Get("state");
            if (last.Get("status").String != "won")
            {
                Assert.Pass("ends " + last.Get("status").String);
                return;
            }
            LevelDefinition level = s.Has("level")
                ? LevelValidator.Validate(s.Get("level")).Level
                : LevelDefinitionReader.Read(ReadRepo(s.Get("levelFile").String)).Level;
            int n = level.Picture.Cells.Length;
            string expected = Fnv1a(Enumerable.Repeat(-1, n).Concat(Enumerable.Repeat(1, n)).Concat(Enumerable.Repeat(0, n)));
            Assert.That(last.Get("boardHash").String, Is.EqualTo(expected));
        }

        [Test]
        public void StartHashCoversTheInitialCells()
        {
            // Cross-checks the hash input order on a level whose open cells at the start are known: the 3x3 ring has no empty cell.
            JsonValue s = Json(GoldenDir + "/scenarios/ring-step.json");
            JsonValue start = s.Get("steps").Items[0].Get("state");
            int[] cells = { 0, 0, 0, 0, 1, 0, 0, 0, 0 };
            string expected = Fnv1a(cells.Concat(Enumerable.Repeat(0, 9)).Concat(Enumerable.Repeat(0, 9)));
            Assert.That(start.Get("boardHash").String, Is.EqualTo(expected));
        }
    }
}
