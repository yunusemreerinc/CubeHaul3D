using CubeHaul.Domain;
using CubeHaul.Levels;
using NUnit.Framework;

namespace CubeHaul.Tests.EditMode
{
    /// <summary>Bad input gives an error that says what is wrong and where, never an exception.</summary>
    public class ErrorReportTests
    {
        const string Ring = @"{
  ""schemaVersion"": 1, ""levelId"": ""ring"", ""themeId"": ""default"", ""name"": ""Ring"",
  ""picture"": { ""width"": 3, ""height"": 3, ""palette"": [""#ff0000"", ""#0000ff""], ""cells"": [0,0,0, 0,1,0, 0,0,0] },
  ""edges"": ""all"", ""slotCount"": 5,
  ""tray"": { ""columns"": [[{ ""id"": 0, ""color"": 0, ""count"": 8 }], [{ ""id"": 1, ""color"": 1, ""count"": 1 }]] }
}";

        [Test]
        public void TheRingIsValid()
        {
            LevelReadResult r = LevelDefinitionReader.Read(Ring);
            Assert.That(r.Ok, Is.True, r.Describe());
            Assert.That(r.Describe(), Is.EqualTo("no issues"));
        }

        [Test]
        public void BrokenJsonSaysWhere()
        {
            LevelReadResult r = LevelDefinitionReader.Read(Ring.Replace("\"slotCount\": 5,", "\"slotCount\": 5,,"));
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Level, Is.Null);
            StringAssert.StartsWith("1 error:\n  - JSON_SYNTAX at (root): ", r.Describe());
            StringAssert.Contains("line 4, column", r.Describe());
        }

        [Test]
        public void ANewerSchemaVersionIsRefusedClearly()
        {
            LevelReadResult r = LevelDefinitionReader.Read(Ring.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2"));
            Assert.That(r.Describe(), Is.EqualTo("1 error:\n  - UNSUPPORTED_SCHEMA_VERSION at /schemaVersion: schema version 2 is not supported (this reader knows 1)"));
        }

        [Test]
        public void FieldErrorsNameThePathAndTheReason()
        {
            string bad = Ring.Replace("[0,0,0, 0,1,0, 0,0,0]", "[0,0,0, 0,1,0, 0,0]").Replace("\"edges\": \"all\"", "\"edges\": \"top\"");
            LevelReadResult r = LevelDefinitionReader.Read(bad);
            string report = r.Describe();
            StringAssert.StartsWith("2 errors:", report);
            StringAssert.Contains("INVALID_VALUE at /edges: \"top\" is not one of bottom, sides, all", report);
            StringAssert.Contains("CELLS_LENGTH at /picture/cells: has 8 cells, expected width * height = 9", report);
        }

        [Test]
        public void TotalsThatDoNotMatchTheCubesAreCaught()
        {
            LevelReadResult r = LevelDefinitionReader.Read(Ring.Replace("\"count\": 8", "\"count\": 7"));
            StringAssert.Contains("TRAY_TOTAL_MISMATCH at /picture/palette/0: color 0 has 8 cubes but 7 trucks in the tray", r.Describe());
        }

        [Test]
        public void ALevelBuiltInCodeIsCheckedWithTheSameRules()
        {
            LevelDefinition level = LevelDefinitionReader.Read(Ring).Level;
            Assert.That(LevelValidator.Validate(level).Ok, Is.True);
            Assert.That(LevelValidator.Validate(level).Level.ContentEquals(level), Is.True);

            level.Tray.Columns[0][0].Count = 9;
            level.Picture.Cells = null;
            level.Edges = (OpenEdges)42;
            LevelReadResult r = LevelValidator.Validate(level);
            Assert.That(r.Ok, Is.False);
            StringAssert.Contains("WRONG_TYPE at /picture/cells: expected an array, got null", r.Describe());
            StringAssert.Contains("INVALID_VALUE at /edges", r.Describe());
        }

        [Test]
        public void AnEmptyOrMissingLevelIsAnErrorNotACrash()
        {
            Assert.That(LevelValidator.Validate((LevelDefinition)null).Errors[0].Code, Is.EqualTo(IssueCodes.WrongType));
            LevelReadResult r = LevelValidator.Validate(new LevelDefinition());
            Assert.That(r.Ok, Is.False);
            StringAssert.Contains("MISSING_FIELD at /picture", r.Describe());
            StringAssert.Contains("WRONG_TYPE at /levelId", r.Describe());
        }
    }
}
