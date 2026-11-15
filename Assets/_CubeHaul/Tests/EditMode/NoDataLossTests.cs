using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CubeHaul.Levels;
using CubeHaul.Levels.Json;
using NUnit.Framework;

namespace CubeHaul.Tests.EditMode
{
    /// <summary>
    /// The 10 built-in levels survive the C# model without losing anything, shown three independent ways:
    /// the JSON tree rebuilt from the model equals the file's, the file written back is byte-identical to what the
    /// TypeScript exporter wrote, and the tray hashes to what the TypeScript generator recorded in build.json.
    /// </summary>
    public class NoDataLossTests
    {
        const string LevelsDir = "Assets/_CubeHaul/Levels/Content";

        static string[] Files() =>
            Directory.GetFiles(ProjectPaths.Of(LevelsDir), "*.level.json").Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal).ToArray();

        static string Text(string file) => File.ReadAllText(ProjectPaths.Of(LevelsDir + "/" + file), Encoding.UTF8);

        static LevelDefinition Read(string file)
        {
            LevelReadResult r = LevelDefinitionReader.Read(Text(file));
            Assert.That(r.Ok, Is.True, file + "\n" + r.Describe());
            return r.Level;
        }

        [TestCaseSource(nameof(Files))]
        public void TheModelHoldsEveryValueOfTheFile(string file)
        {
            JsonValue original = JsonParser.Parse(Text(file));
            JsonValue fromModel = LevelDefinitionWriter.ToJsonValue(Read(file));
            Assert.That(fromModel.DeepEquals(original), Is.True, file);
        }

        [TestCaseSource(nameof(Files))]
        public void WritingTheModelBackGivesTheSameFileByteForByte(string file)
        {
            Assert.That(LevelDefinitionWriter.ToJson(Read(file)), Is.EqualTo(Text(file)));
        }

        [Test]
        public void TraysMatchTheHashesTheTypeScriptGeneratorRecorded()
        {
            JsonValue build = JsonParser.Parse(File.ReadAllText(ProjectPaths.Of("Assets/_CubeHaul/Tests/Fixtures/Golden/build.json"), Encoding.UTF8));
            string[] files = Files();
            Assert.That(build.Get("levels").Items.Count, Is.EqualTo(files.Length));
            for (int i = 0; i < files.Length; i++)
            {
                JsonValue entry = build.Get("levels").Items[i];
                JsonValue chosen = entry.Get("candidates").Items[(int)entry.Get("chosen").Number];
                LevelDefinition level = Read(files[i]);
                Assert.That(level.LevelId, Is.EqualTo(entry.Get("levelId").String));
                Assert.That(ColumnsSha256(level.Tray), Is.EqualTo(chosen.Get("columnsSha256").String), files[i]);
                Assert.That(level.DifficultyMetadata.WinRate, Is.EqualTo(entry.Get("winRate").Number), files[i]);
            }
        }

        [TestCaseSource(nameof(Files))]
        public void EveryColorHasOneTruckPerCube(string file)
        {
            LevelDefinition level = Read(file);
            for (int color = 0; color < level.Picture.Palette.Length; color++)
                Assert.That(level.Tray.TrucksOf(color), Is.EqualTo(level.Picture.CountOf(color)), file + " color " + color);
            Assert.That(level.Picture.Cells.Length, Is.EqualTo(level.Picture.Width * level.Picture.Height));
        }

        /// <summary>sha256 of JSON.stringify(columns), as scripts/golden/hash.ts computes it.</summary>
        static string ColumnsSha256(TrayDefinition tray)
        {
            var sb = new StringBuilder("[");
            for (int c = 0; c < tray.Columns.Length; c++)
            {
                if (c > 0) sb.Append(',');
                sb.Append('[');
                for (int k = 0; k < tray.Columns[c].Length; k++)
                {
                    BlockDefinition b = tray.Columns[c][k];
                    if (k > 0) sb.Append(',');
                    sb.Append("{\"id\":").Append(b.Id).Append(",\"color\":").Append(b.Color).Append(",\"count\":").Append(b.Count).Append('}');
                }
                sb.Append(']');
            }
            sb.Append(']');
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
