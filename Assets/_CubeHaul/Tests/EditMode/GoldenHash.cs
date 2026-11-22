using System.Collections.Generic;

namespace CubeHaul.Tests.EditMode
{
    /// <summary>FNV-1a 32 over int32 values fed as 4 little-endian bytes, as Tools/ts-reference/scripts/golden/hash.ts.</summary>
    static class GoldenHash
    {
        public static string Fnv1a(IEnumerable<int> values)
        {
            uint h = 2166136261;
            foreach (int v in values)
            {
                uint x = unchecked((uint)v);
                for (int k = 0; k < 4; k++)
                {
                    h ^= (x >> (8 * k)) & 0xff;
                    h = unchecked(h * 16777619);
                }
            }
            return h.ToString("x8");
        }
    }
}
