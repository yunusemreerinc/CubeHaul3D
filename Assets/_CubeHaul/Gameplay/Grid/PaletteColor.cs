using System;

namespace CubeHaul.Gameplay.Grid
{
    /// <summary>A palette entry ("#rrggbb", as in LevelDefinition.picture.palette) as 8-bit sRGB channels.</summary>
    public readonly struct PaletteColor : IEquatable<PaletteColor>
    {
        public byte R { get; }
        public byte G { get; }
        public byte B { get; }

        public PaletteColor(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        /// <summary>Parses "#rrggbb" (the only form the level format allows), case-insensitive.</summary>
        public static bool TryParse(string text, out PaletteColor color)
        {
            color = default;
            if (text == null || text.Length != 7 || text[0] != '#') return false;
            int r = Hex(text[1]) * 16 + Hex(text[2]), g = Hex(text[3]) * 16 + Hex(text[4]), b = Hex(text[5]) * 16 + Hex(text[6]);
            if (r < 0 || g < 0 || b < 0) return false;
            color = new PaletteColor((byte)r, (byte)g, (byte)b);
            return true;
        }

        public static PaletteColor Parse(string text) =>
            TryParse(text, out PaletteColor c) ? c : throw new FormatException($"\"{text}\" is not a #rrggbb color");

        static int Hex(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1000;
        }

        /// <summary>
        /// cube-hunt view/draw.ts shade: mixes toward black (amount &lt; 0) or white (amount &gt; 0), each channel rounded
        /// like Math.round.
        /// </summary>
        public PaletteColor Shade(double amount)
        {
            byte Mix(byte v) => (byte)Levels.Generation.JsMath.RoundToInt(amount < 0 ? v * (1 + amount) : v + (255 - v) * amount);
            return new PaletteColor(Mix(R), Mix(G), Mix(B));
        }

        /// <summary>draw.ts luminance: 0 (black) .. 1 (white), Rec. 601 weights.</summary>
        public double Luminance => (0.299 * R + 0.587 * G + 0.114 * B) / 255;

        /// <summary>Light colors get dark lettering and fills (draw.ts: luminance &gt; 0.72).</summary>
        public bool IsLight => Luminance > 0.72;

        public bool Equals(PaletteColor o) => R == o.R && G == o.G && B == o.B;
        public override bool Equals(object obj) => obj is PaletteColor o && Equals(o);
        public override int GetHashCode() => (R << 16) | (G << 8) | B;
        public override string ToString() => $"#{R:x2}{G:x2}{B:x2}";
    }
}
