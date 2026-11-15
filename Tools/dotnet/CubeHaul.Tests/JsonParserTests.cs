using System;
using CubeHaul.Levels.Json;
using NUnit.Framework;

using CubeHaul.Levels;

namespace CubeHaul.Tests.LevelFormat
{
    /// <summary>The parser accepts exactly what JavaScript's JSON.parse accepts, and reads values the same way.</summary>
    public class JsonParserTests
    {
        [TestCase("{}")]
        [TestCase("[]")]
        [TestCase(" \t\r\n[1, -2, 3.5, -0, 1e3, 1E-2, 2.5e+1, true, false, null, \"x\"]\n")]
        [TestCase("{\"a\": {\"b\": [[], {}]}}")]
        [TestCase("\"\\u00e7\\u011f\\u0131 \\\" \\\\ \\/ \\b \\f \\n \\r \\t\"")]
        [TestCase("123")]
        public void Accepts(string text)
        {
            Assert.DoesNotThrow(() => JsonParser.Parse(text));
        }

        [TestCase("")]
        [TestCase("{")]
        [TestCase("[1,]")]
        [TestCase("{\"a\":1,}")]
        [TestCase("{'a':1}")]
        [TestCase("{a:1}")]
        [TestCase("// c\n{}")]
        [TestCase("NaN")]
        [TestCase("Infinity")]
        [TestCase("01")]
        [TestCase("1.")]
        [TestCase(".5")]
        [TestCase("+1")]
        [TestCase("0x10")]
        [TestCase("\"tab\there\"")]
        [TestCase("\"\\x41\"")]
        [TestCase("\"\\u12\"")]
        [TestCase("tru")]
        [TestCase("{} {}")]
        [TestCase("[1 2]")]
        public void Rejects(string text)
        {
            Assert.Throws<JsonParseException>(() => JsonParser.Parse(text));
        }

        [Test]
        public void NumbersAreDoublesAndIntegersAreWhatNumberIsIntegerSays()
        {
            Assert.That(JsonParser.Parse("1.0").IsInteger, Is.True);
            Assert.That(JsonParser.Parse("5e0").IsInteger, Is.True);
            Assert.That(JsonParser.Parse("2.5").IsInteger, Is.False);
            Assert.That(JsonParser.Parse("-0").IsInteger, Is.True);
            Assert.That(JsonParser.Parse("0.1").Number, Is.EqualTo(0.1));
            Assert.That(JsonParser.Parse("2147483648").Number, Is.EqualTo(2147483648d));
        }

        [Test]
        public void TooLargeNumbersBecomeInfinityLikeJavaScript()
        {
            JsonValue v = JsonParser.Parse("[1e400, -1e400]");
            Assert.That(double.IsPositiveInfinity(v.Items[0].Number), Is.True);
            Assert.That(double.IsNegativeInfinity(v.Items[1].Number), Is.True);
            Assert.That(v.Items[0].IsInteger, Is.False);
        }

        [Test]
        public void RepeatedKeyKeepsItsFirstPlaceAndTakesTheLastValue()
        {
            JsonValue v = JsonParser.Parse("{\"a\": 1, \"b\": 2, \"a\": 3}");
            Assert.That(v.Members.Count, Is.EqualTo(2));
            Assert.That(v.Members[0].Key, Is.EqualTo("a"));
            Assert.That(v.Get("a").Number, Is.EqualTo(3));
        }

        [Test]
        public void EscapesAndSurrogatesDecode()
        {
            Assert.That(JsonParser.Parse("\"\\u00e7\\u011f\\u0131\"").String, Is.EqualTo("çğı"));
            Assert.That(JsonParser.Parse("\"\\ud83d\\ude9a\"").String, Is.EqualTo("\U0001F69A"));
            Assert.That(JsonParser.Parse("\"İstanbul\"").String, Is.EqualTo("İstanbul"));
        }

        [Test]
        public void DeepNestingIsRejectedInsteadOfOverflowingTheStack()
        {
            Assert.DoesNotThrow(() => JsonParser.Parse(new string('[', JsonParser.MaxDepth) + new string(']', JsonParser.MaxDepth)));
            Assert.Throws<JsonParseException>(() => JsonParser.Parse(new string('[', JsonParser.MaxDepth + 1) + new string(']', JsonParser.MaxDepth + 1)));
        }

        [Test]
        public void ErrorsSayWhere()
        {
            var e = Assert.Throws<JsonParseException>(() => JsonParser.Parse("{\n  \"a\": 1,\n}"));
            Assert.That(e.Line, Is.EqualTo(3));
            Assert.That(e.Column, Is.EqualTo(1));
        }

        [Test]
        public void ReaderNeverThrowsForBadInput()
        {
            Assert.That(LevelDefinitionReader.Read(null).Errors[0].Code, Is.EqualTo(IssueCodes.JsonSyntax));
            Assert.That(LevelDefinitionReader.Read("\uFEFF").Errors[0].Code, Is.EqualTo(IssueCodes.JsonSyntax));
            Assert.That(LevelDefinitionReader.Read("null").Errors[0].Code, Is.EqualTo(IssueCodes.WrongType));
        }
    }
}
