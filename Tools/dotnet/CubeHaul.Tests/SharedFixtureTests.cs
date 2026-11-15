using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

using CubeHaul.Levels;

namespace CubeHaul.Tests.LevelFormat
{
    /// <summary>The same fixtures and expectations as Tools/level-format/tests/validate.test.ts.</summary>
    public class SharedFixtureTests
    {
        static IEnumerable<string> InvalidFiles() => Fixtures.Files("fixtures/invalid");
        static IEnumerable<string> ValidFiles() => Fixtures.Files("fixtures/valid");
        static IEnumerable<string> ExampleFiles() => Fixtures.Files("examples");

        [Test]
        public void EveryFixtureHasAnExpectedResultAndViceVersa()
        {
            CollectionAssert.AreEqual(Fixtures.Expected("fixtures/invalid").Members.Select(m => m.Key).OrderBy(k => k, System.StringComparer.Ordinal), InvalidFiles());
            CollectionAssert.AreEqual(Fixtures.Expected("fixtures/valid").Members.Select(m => m.Key).OrderBy(k => k, System.StringComparer.Ordinal), ValidFiles());
            Assert.That(InvalidFiles().Count(), Is.GreaterThanOrEqualTo(50));
        }

        [TestCaseSource(nameof(InvalidFiles))]
        public void InvalidFixtureIsRejectedWithExactlyTheExpectedErrors(string file)
        {
            LevelReadResult result = LevelDefinitionReader.Read(Fixtures.ReadRaw("fixtures/invalid", file));
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Level, Is.Null);
            var expected = Fixtures.ExpectedKeys(Fixtures.Expected("fixtures/invalid").Get(file).Get("errors"));
            CollectionAssert.AreEqual(expected, Fixtures.Keys(result.Errors), string.Join("\n", result.Errors));
            foreach (LevelIssue e in result.Errors) Assert.That(e.Message, Is.Not.Empty);
        }

        [TestCaseSource(nameof(ValidFiles))]
        public void ValidFixtureIsAcceptedWithExactlyTheExpectedWarnings(string file)
        {
            LevelReadResult result = LevelDefinitionReader.Read(Fixtures.ReadRaw("fixtures/valid", file));
            CollectionAssert.IsEmpty(result.Errors, string.Join("\n", result.Errors));
            Assert.That(result.Ok, Is.True);
            Assert.That(result.Level, Is.Not.Null);
            var expected = Fixtures.ExpectedKeys(Fixtures.Expected("fixtures/valid").Get(file).Get("warnings"));
            CollectionAssert.AreEqual(expected, Fixtures.Keys(result.Warnings));
        }

        [TestCaseSource(nameof(ExampleFiles))]
        public void ExampleIsAcceptedWithoutWarnings(string file)
        {
            LevelReadResult result = LevelDefinitionReader.Read(Fixtures.ReadRaw("examples", file));
            CollectionAssert.IsEmpty(result.Errors, string.Join("\n", result.Errors));
            CollectionAssert.IsEmpty(result.Warnings, string.Join("\n", result.Warnings));
        }
    }
}
