using System;
using System.Globalization;
using CubeHaul.Levels.Json;
using NUnit.Framework;

namespace CubeHaul.Tests.EditMode
{
    /// <summary>
    /// JSON numbers read back bit for bit, on every runtime (Task 15 fix). Unity's Mono keeps .NET Framework's
    /// double.Parse, which can be a last bit off for 16–17 digit numbers; the golden sessions failed in Unity on exactly
    /// that (e.g. 435.27586206896723 read as 435.27586206896717). Expected bits are JavaScript's (Node: Number(text)).
    /// </summary>
    public class JsonNumberTests
    {
        // The first five are truck positions from the sessions that Unity read one bit off.
        [TestCase("435.27586206896723", "407b3469ee5846bd")]
        [TestCase("386.30344827586123", "407824daec944da0")]
        [TestCase("393.44827586206833", "4078972c234f72b7")]
        [TestCase("174.71264367816121", "4065d6cdfa1d6cea")]
        [TestCase("320.29885057471313", "407404c8178a4c8a")]
        [TestCase("0.1", "3fb999999999999a")]
        [TestCase("0.3", "3fd3333333333333")]
        [TestCase("2.2250738585072011e-308", "000fffffffffffff")]
        [TestCase("2.2250738585072014e-308", "0010000000000000")]
        [TestCase("2.2250738585072012e-308", "0010000000000000")]
        [TestCase("4.9e-324", "0000000000000001")]
        [TestCase("5e-324", "0000000000000001")]
        [TestCase("2.4703282292062327e-324", "0000000000000000")]
        [TestCase("2.4703282292062328e-324", "0000000000000001")]
        [TestCase("1.7976931348623157e308", "7fefffffffffffff")]
        [TestCase("1.7976931348623158e308", "7fefffffffffffff")]
        [TestCase("1.7976931348623159e308", "7ff0000000000000")]
        [TestCase("9007199254740993", "4340000000000000")]
        [TestCase("9007199254740995", "4340000000000002")]
        [TestCase("123456789012345678901234567890", "45f8ee90ff6c373e")]
        [TestCase("0.000001", "3eb0c6f7a0b5ed8d")]
        [TestCase("1e-7", "3e7ad7f29abcaf48")]
        [TestCase("1e23", "44b52d02c7e14af6")]
        [TestCase("8.98846567431158e307", "7fe0000000000000")]
        [TestCase("7.205759403792794e16", "4370000000000000")]
        [TestCase("3.0000000000000004", "4008000000000001")]
        [TestCase("0.30000000000000004", "3fd3333333333334")]
        [TestCase("1e22", "4480f0cf064dd592")]
        [TestCase("1e-22", "3b5e392010175ee6")]
        [TestCase("0.016666666666666666", "3f91111111111111")]
        [TestCase("33.7833333333327", "4040e444444443eb")]
        [TestCase("100000000000000000000000000000000000001e-38", "3ff0000000000000")]
        [TestCase("0.1000000000000000055511151231257827021181583404541015625", "3fb999999999999a")]
        public void ReadsTheNearestDouble(string text, string bits)
        {
            long expected = long.Parse(bits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            Assert.That(BitConverter.DoubleToInt64Bits(JsonNumber.Parse(text)), Is.EqualTo(expected), text);
            Assert.That(BitConverter.DoubleToInt64Bits(JsonNumber.Parse("-" + text)), Is.EqualTo(expected | long.MinValue), "-" + text);
            Assert.That(BitConverter.DoubleToInt64Bits(JsonParser.Parse("[" + text + "]").Items[0].Number), Is.EqualTo(expected), "through the parser");
        }

        [Test]
        public void ZerosInfinitiesAndOddForms()
        {
            Assert.That(JsonNumber.Parse("0"), Is.EqualTo(0.0));
            Assert.That(BitConverter.DoubleToInt64Bits(JsonNumber.Parse("-0")), Is.EqualTo(long.MinValue), "-0 keeps its sign");
            Assert.That(JsonNumber.Parse("0.000e5"), Is.EqualTo(0.0));
            Assert.That(JsonNumber.Parse("1e400"), Is.EqualTo(double.PositiveInfinity));
            Assert.That(JsonNumber.Parse("-1e400"), Is.EqualTo(double.NegativeInfinity));
            Assert.That(JsonNumber.Parse("1e-400"), Is.EqualTo(0.0));
            Assert.That(JsonNumber.Parse("1E+002"), Is.EqualTo(100.0));
            Assert.That(JsonNumber.Parse("00012.5000"), Is.EqualTo(12.5));
            Assert.That(JsonNumber.Parse("1e999999999999"), Is.EqualTo(double.PositiveInfinity));
            Assert.Throws<FormatException>(() => JsonNumber.Parse(""));
            Assert.Throws<FormatException>(() => JsonNumber.Parse("1e"));
            Assert.Throws<FormatException>(() => JsonNumber.Parse("1x"));
            Assert.Throws<FormatException>(() => JsonNumber.Parse("."));
        }

        [Test]
        public void EveryDoubleWrittenAsJsonReadsBackTheSame()
        {
            var rng = new Random(15);
            var bytes = new byte[8];
            for (int k = 0; k < 20000; k++)
            {
                rng.NextBytes(bytes);
                double d = BitConverter.ToDouble(bytes, 0);
                if (double.IsNaN(d) || double.IsInfinity(d)) continue;
                string text = JsonWriter.FormatNumber(d);
                Assert.That(JsonNumber.Parse(text), Is.EqualTo(d), text);
            }
        }
    }
}
