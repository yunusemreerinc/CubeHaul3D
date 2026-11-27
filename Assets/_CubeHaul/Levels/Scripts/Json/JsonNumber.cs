using System;
using System.Numerics;

namespace CubeHaul.Levels.Json
{
    /// <summary>
    /// Reads a JSON number as the nearest double (ties to even), as JavaScript and .NET Core 3.0+ do. Unity's Mono keeps
    /// the old .NET Framework <c>double.Parse</c>, which can be one unit in the last place off for numbers with 16–17
    /// significant digits (e.g. 435.27586206896723); golden fixtures written by Node have such numbers everywhere, and
    /// exact comparisons need them read back bit for bit. Short numbers take the exact fast path (Clinger: at most 15
    /// digits and |exponent| ≤ 22 is one correctly rounded multiply or divide); the rest are converted exactly with
    /// big integers.
    /// </summary>
    public static class JsonNumber
    {
        static readonly double[] Pow10 =
        {
            1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22,
        };

        /// <summary>
        /// Parses <c>-?digits(.digits)?([eE][+-]?digits)?</c> (also a leading '+', digits after a bare '.', and any number
        /// of leading zeros). Too large: ±infinity; too small: ±0.
        /// </summary>
        public static double Parse(string s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            int i = 0, n = s.Length;
            bool negative = false;
            if (i < n && (s[i] == '-' || s[i] == '+')) negative = s[i++] == '-';

            // Significant digits, without leading zeros, and the decimal exponent they are scaled by.
            var digits = new System.Text.StringBuilder(24);
            long exponent = 0;
            bool any = false;
            for (; i < n && IsDigit(s[i]); i++)
            {
                any = true;
                if (digits.Length > 0 || s[i] != '0') digits.Append(s[i]);
            }
            if (i < n && s[i] == '.')
            {
                for (i++; i < n && IsDigit(s[i]); i++)
                {
                    any = true;
                    if (digits.Length > 0 || s[i] != '0') digits.Append(s[i]);
                    exponent--;
                }
            }
            if (!any) throw new FormatException($"not a number: \"{s}\"");
            if (i < n && (s[i] == 'e' || s[i] == 'E'))
            {
                i++;
                bool negativeExponent = false;
                if (i < n && (s[i] == '-' || s[i] == '+')) negativeExponent = s[i++] == '-';
                if (i >= n || !IsDigit(s[i])) throw new FormatException($"not a number: \"{s}\"");
                long e = 0;
                for (; i < n && IsDigit(s[i]); i++)
                    if (e < 100_000_000) e = e * 10 + (s[i] - '0');
                exponent += negativeExponent ? -e : e;
            }
            if (i != n) throw new FormatException($"not a number: \"{s}\"");

            // Trailing zeros only scale.
            int count = digits.Length;
            while (count > 0 && digits[count - 1] == '0')
            {
                count--;
                exponent++;
            }
            double magnitude = count == 0 ? 0 : Magnitude(digits.ToString(0, count), exponent);
            return negative ? -magnitude : magnitude;
        }

        static double Magnitude(string digits, long exponent)
        {
            int count = digits.Length;
            // The value is between 10^(count - 1 + exponent) and 10^(count + exponent).
            if (count + exponent > 310) return double.PositiveInfinity;
            if (count + exponent < -330) return 0;

            if (count <= 15 && exponent >= -22 && exponent <= 22)
            {
                double m = long.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);
                return exponent >= 0 ? m * Pow10[exponent] : m / Pow10[-exponent];
            }

            BigInteger num = BigInteger.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);
            BigInteger den = BigInteger.One;
            if (exponent >= 0) num *= BigInteger.Pow(10, (int)exponent);
            else den = BigInteger.Pow(10, (int)-exponent);

            // Scale by 2^shift so that the quotient has 53 bits: value = q · 2^-shift.
            int shift = 52 - (BitLength(num) - BitLength(den));
            BigInteger q, r, d;
            Divide(num, den, shift, out q, out r, out d);
            if (q < TwoPow52)
            {
                shift++;
                Divide(num, den, shift, out q, out r, out d);
            }
            int binaryExponent = 52 - shift; // value = (q / 2^52) · 2^binaryExponent
            bool subnormal = binaryExponent < -1022;
            if (subnormal)
            {
                // Fixed point at 2^-1074: fewer bits are kept.
                shift = 1074;
                Divide(num, den, shift, out q, out r, out d);
            }

            // Round to nearest, ties to even.
            int c = (r * 2).CompareTo(d);
            if (c > 0 || (c == 0 && !q.IsEven)) q += 1;
            if (subnormal) return BitConverter.Int64BitsToDouble((long)q); // q = 2^52 is the smallest normal: still right
            if (q == TwoPow53)
            {
                q >>= 1;
                binaryExponent++;
            }
            if (binaryExponent > 1023) return double.PositiveInfinity;
            long bits = ((long)(binaryExponent + 1023) << 52) | (long)(q - TwoPow52);
            return BitConverter.Int64BitsToDouble(bits);
        }

        static readonly BigInteger TwoPow52 = BigInteger.One << 52;
        static readonly BigInteger TwoPow53 = BigInteger.One << 53;

        static void Divide(BigInteger num, BigInteger den, int shift, out BigInteger q, out BigInteger r, out BigInteger d)
        {
            BigInteger n = num;
            d = den;
            if (shift >= 0) n <<= shift;
            else d <<= -shift;
            q = BigInteger.DivRem(n, d, out r);
        }

        /// <summary>Bits in a positive integer (BigInteger.GetBitLength is not in .NET Standard 2.1).</summary>
        static int BitLength(BigInteger v)
        {
            byte[] bytes = v.ToByteArray(); // little-endian, with a sign byte when the top bit is set
            int top = bytes.Length - 1;
            while (top > 0 && bytes[top] == 0) top--;
            int bits = top * 8;
            for (int b = bytes[top]; b != 0; b >>= 1) bits++;
            return bits;
        }

        static bool IsDigit(char c) => c >= '0' && c <= '9';
    }
}
