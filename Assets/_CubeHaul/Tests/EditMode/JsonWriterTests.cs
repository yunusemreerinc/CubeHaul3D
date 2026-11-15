using CubeHaul.Levels.Json;
using NUnit.Framework;

namespace CubeHaul.Tests.EditMode
{
    /// <summary>The writer prints what JavaScript's JSON.stringify prints (values taken from Node).</summary>
    public class JsonWriterTests
    {
        [TestCase(0.0, "0")]
        [TestCase(-0.0, "0")]
        [TestCase(1.0, "1")]
        [TestCase(-5.5, "-5.5")]
        [TestCase(0.2, "0.2")]
        [TestCase(0.1 + 0.2, "0.30000000000000004")]
        [TestCase(20.0 / 24, "0.8333333333333334")]
        [TestCase(13.0 / 24, "0.5416666666666666")]
        [TestCase(2147483647.0, "2147483647")]
        [TestCase(1e20, "100000000000000000000")]
        [TestCase(1e21, "1e+21")]
        [TestCase(1.5e300, "1.5e+300")]
        [TestCase(0.000001, "0.000001")]
        [TestCase(1e-7, "1e-7")]
        [TestCase(1.25e-9, "1.25e-9")]
        [TestCase(123.456, "123.456")]
        public void NumbersAsJavaScriptPrintsThem(double value, string expected)
        {
            Assert.That(JsonWriter.FormatNumber(value), Is.EqualTo(expected));
        }

        [Test]
        public void StringsEscapeLikeJsonStringify()
        {
            JsonValue v = JsonParser.Parse("\"a\\\"b\\\\c\\n\\t\\u0001/ç\\ud83d\\ude9a\\ud800\"");
            Assert.That(JsonWriter.Stringify(v), Is.EqualTo("\"a\\\"b\\\\c\\n\\t\\u0001/ç\U0001F69A\\ud800\""));
        }

        [Test]
        public void IntegerKeysComeFirstLikeInJavaScript()
        {
            JsonValue v = JsonParser.Parse("{\"b\": 1, \"10\": 2, \"a\": [true, null], \"2\": {}}");
            Assert.That(JsonWriter.Stringify(v), Is.EqualTo("{\"2\":{},\"10\":2,\"b\":1,\"a\":[true,null]}"));
            Assert.That(JsonWriter.Stringify(v, 2), Is.EqualTo("{\n  \"2\": {},\n  \"10\": 2,\n  \"b\": 1,\n  \"a\": [\n    true,\n    null\n  ]\n}"));
        }

        [Test]
        public void ParseThenStringifyIsStable()
        {
            const string text = "{\"x\":[1,2.5,-3e-7,\"s\"],\"y\":{\"z\":false}}";
            Assert.That(JsonWriter.Stringify(JsonParser.Parse(text)), Is.EqualTo("{\"x\":[1,2.5,-3e-7,\"s\"],\"y\":{\"z\":false}}"));
        }
    }
}
