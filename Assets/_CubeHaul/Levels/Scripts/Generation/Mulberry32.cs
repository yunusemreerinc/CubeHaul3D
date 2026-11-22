namespace CubeHaul.Levels.Generation
{
    /// <summary>
    /// cube-hunt's seeded PRNG (src/levels/rng.ts): the same seed gives the same values as in JavaScript, bit for bit.
    ///
    /// JavaScript works on doubles and converts to 32 bits for each bit operation; here every step is uint arithmetic,
    /// which is the same thing modulo 2^32: <c>a + 0x6d2b79f5 &gt;&gt;&gt; 0</c> and <c>Math.imul</c> are wrapping uint
    /// addition and multiplication, and <c>t ^= t + Math.imul(...)</c> (a double sum, then ToInt32) is a wrapping uint
    /// sum. The result <c>x / 2^32</c> is exact in both languages (a uint fits a double's 53-bit mantissa).
    /// </summary>
    public sealed class Mulberry32
    {
        uint a;

        /// <summary>The seed is taken modulo 2^32, as JavaScript's <c>seed &gt;&gt;&gt; 0</c> does for an integer.</summary>
        public Mulberry32(long seed)
        {
            a = unchecked((uint)seed);
        }

        /// <summary>The next value as cube-hunt's <c>rng()</c> returns it: in [0, 1).</summary>
        public double Next() => NextUInt32() / 4294967296.0;

        /// <summary>The next value as a uint32 (Next() * 2^32).</summary>
        public uint NextUInt32()
        {
            unchecked
            {
                a += 0x6d2b79f5;
                uint t = a;
                t = (t ^ (t >> 15)) * (t | 1);
                t ^= t + (t ^ (t >> 7)) * (t | 61);
                return t ^ (t >> 14);
            }
        }

        /// <summary><c>Math.floor(rng() * n)</c>: a uniform index in [0, n) for n &gt; 0.</summary>
        public int NextIndex(int n) => (int)System.Math.Floor(Next() * n);
    }
}
