using System;
using System.Globalization;
using System.Numerics;
using CubeHaul.Levels.Json;
using NUnit.Framework;

namespace CubeHaul.Tests.LevelFormat
{
    /// <summary>
    /// JsonNumber against .NET 8's double.Parse, which is correctly rounded for any input (.NET Core 3.0+), on random
    /// doubles in every format, random digit strings and exact halfway points between neighbouring doubles (and just
    /// either side of them), the hardest inputs for rounding. Only here: Unity's Mono parser is the one being replaced.
    /// </summary>
    public class JsonNumberFuzzTests
    {
        static void Same(string text)
        {
            double expected = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            double actual = JsonNumber.Parse(text);
            if (BitConverter.DoubleToInt64Bits(expected) != BitConverter.DoubleToInt64Bits(actual))
                Assert.Fail($"{text}: expected {expected:R} ({BitConverter.DoubleToInt64Bits(expected):x16}), got {actual:R} ({BitConverter.DoubleToInt64Bits(actual):x16})");
        }

        [Test]
        public void RandomDoublesInEveryFormat()
        {
            var rng = new Random(1);
            var bytes = new byte[8];
            string[] formats = { "R", "G17", "G15", "E16", "E10", "E3", "F6" };
            for (int k = 0; k < 300_000; k++)
            {
                rng.NextBytes(bytes);
                double d = BitConverter.ToDouble(bytes, 0);
                if (double.IsNaN(d) || double.IsInfinity(d)) continue;
                string f = formats[k % formats.Length];
                if (f == "F6" && Math.Abs(d) > 1e30) continue;
                Same(d.ToString(f, CultureInfo.InvariantCulture));
            }
        }

        [Test]
        public void RandomDigitStrings()
        {
            var rng = new Random(2);
            for (int k = 0; k < 300_000; k++)
            {
                int length = 1 + rng.Next(30);
                var chars = new char[length];
                for (int i = 0; i < length; i++) chars[i] = (char)('0' + rng.Next(10));
                string digits = new string(chars);
                int point = rng.Next(length + 1);
                string text = digits.Substring(0, point) + (point < length ? "." + digits.Substring(point) : "");
                if (text.StartsWith(".")) text = "0" + text;
                text += "e" + (rng.Next(680) - 345);
                Same(text);
            }
        }

        [Test]
        public void HalfwayBetweenTwoDoublesAndJustEitherSide()
        {
            var rng = new Random(3);
            var bytes = new byte[8];
            for (int k = 0; k < 60_000; k++)
            {
                rng.NextBytes(bytes);
                long bits = BitConverter.ToInt64(bytes, 0) & long.MaxValue;
                if (k % 4 == 0) bits &= 0x000F_FFFF_FFFF_FFFF; // subnormals too
                if ((bits >> 52) >= 0x7FE) continue;
                long mantissa = bits & 0x000F_FFFF_FFFF_FFFF;
                int exp = (int)(bits >> 52);
                BigInteger m = exp == 0 ? mantissa : mantissa | (1L << 52);
                int e = exp == 0 ? -1074 : exp - 1075;
                // Halfway to the next double up: (2m + 1) · 2^(e - 1), written out exactly.
                string mid = Exact(2 * m + 1, e - 1);
                Same(mid);
                Same(mid + "000000000000000000001");
                Same(Below(mid));
            }
        }

        /// <summary>n · 2^p as an exact decimal string.</summary>
        static string Exact(BigInteger n, int p)
        {
            if (p >= 0) return (n << p).ToString(CultureInfo.InvariantCulture);
            string digits = (n * BigInteger.Pow(5, -p)).ToString(CultureInfo.InvariantCulture);
            int scale = -p;
            if (digits.Length <= scale) digits = new string('0', scale - digits.Length + 1) + digits;
            return digits.Substring(0, digits.Length - scale) + "." + digits.Substring(digits.Length - scale);
        }

        /// <summary>A decimal just under an exact one: its last digit lowered by one (it ends in 5).</summary>
        static string Below(string exact)
        {
            char last = exact[exact.Length - 1];
            if (last == '0' || !exact.Contains(".")) return exact + ".9";
            return exact.Substring(0, exact.Length - 1) + (char)(last - 1) + "9";
        }
    }
}
