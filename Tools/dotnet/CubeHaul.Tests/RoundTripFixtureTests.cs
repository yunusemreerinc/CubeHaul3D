using System.Collections.Generic;
using CubeHaul.Levels;
using CubeHaul.Levels.Json;
using NUnit.Framework;

namespace CubeHaul.Tests.LevelFormat
{
    /// <summary>
    /// Every valid file of the shared fixtures goes through the C# model without losing anything (extensions, "$schema",
    /// optional fields left out, upper-case hex), and the examples written by the TypeScript tools come out of the C#
    /// writer byte for byte.
    /// </summary>
    public class RoundTripFixtureTests
    {
        static IEnumerable<string> ValidFiles() => Fixtures.Files("fixtures/valid");
        static IEnumerable<string> ExampleFiles() => Fixtures.Files("examples");

        [TestCaseSource(nameof(ValidFiles))]
        public void ValidFixtureRoundTripsThroughTheModel(string file)
        {
            string text = Fixtures.ReadRaw("fixtures/valid", file);
            LevelReadResult r = LevelDefinitionReader.Read(text);
            Assert.That(r.Ok, Is.True, r.Describe());
            JsonValue original = JsonParser.Parse(text.TrimStart('﻿'));
            Assert.That(LevelDefinitionWriter.ToJsonValue(r.Level).DeepEquals(original), Is.True);
            Assert.That(LevelValidator.Validate(r.Level).Level.ContentEquals(r.Level), Is.True);
        }

        [TestCaseSource(nameof(ExampleFiles))]
        public void ExampleIsWrittenBackByteForByte(string file)
        {
            string text = Fixtures.ReadRaw("examples", file);
            Assert.That(LevelDefinitionWriter.ToJson(LevelDefinitionReader.Read(text).Level), Is.EqualTo(text));
        }

        [Test]
        public void ExtensionsAreKeptAsRead()
        {
            LevelDefinition level = LevelDefinitionReader.Read(Fixtures.ReadRaw("fixtures/valid", "uppercase-hex-and-extensions.json")).Level;
            Assert.That(level.Extensions.Get("editor").Get("author").String, Is.EqualTo("x"));
            Assert.That(level.Picture.Palette[0], Is.EqualTo("#FF0000"), "colors are kept as written, not normalised");
            Assert.That(level.DeepCopy().Extensions.DeepEquals(level.Extensions), Is.True);
        }
    }
}
