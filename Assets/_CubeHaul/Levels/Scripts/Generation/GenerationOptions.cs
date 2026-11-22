using System;
using CubeHaul.Domain;

namespace CubeHaul.Levels.Generation
{
    /// <summary>
    /// Everything that decides a generated tray: cube-hunt's GenOptions + BuildOptions + buildLevel's candidates
    /// argument. Defaults are cube-hunt's. A level's <see cref="GenerationSettings"/> plus its edges and slot count give
    /// one with <see cref="For(LevelDefinition)"/>.
    /// </summary>
    public sealed class GenerationOptions
    {
        /// <summary>Taken modulo 2^32 by the RNG; long so that <c>seed + 7919k</c> never overflows.</summary>
        public long Seed { get; set; }
        /// <summary>0 = crates arrive in the order the picture opens up (easy), higher = shuffled (hard).</summary>
        public double Shuffle { get; set; }
        public int Columns { get; set; } = GenerationSettings.DefaultColumns;
        public int SlotCount { get; set; } = 5;
        public OpenEdges Edges { get; set; } = OpenEdges.All;
        /// <summary>Null = derived from the picture size.</summary>
        public int? MinBlock { get; set; }
        /// <summary>Null = derived from the picture size.</summary>
        public int? MaxBlock { get; set; }
        public double BigChance { get; set; } = GenerationSettings.DefaultBigChance;
        public double TargetWinRate { get; set; } = GenerationSettings.DefaultTargetWinRate;
        /// <summary>buildLevel's third argument: how many seeds to try before the fallback.</summary>
        public int Candidates { get; set; } = GenerationSettings.DefaultCandidates;

        /// <summary>The options a level's generation settings describe, with the level's edges and slot count.</summary>
        public static GenerationOptions For(LevelDefinition level)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            GenerationSettings g = level.Generation ?? throw new InvalidOperationException($"level \"{level.LevelId}\" has no generation settings");
            return new GenerationOptions
            {
                Seed = g.Seed,
                Shuffle = g.Shuffle,
                Columns = g.EffectiveColumns,
                SlotCount = level.SlotCount,
                Edges = level.Edges,
                MinBlock = g.MinBlock,
                MaxBlock = g.MaxBlock,
                BigChance = g.EffectiveBigChance,
                TargetWinRate = g.EffectiveTargetWinRate,
                Candidates = g.EffectiveCandidates,
            };
        }

        /// <summary>A copy with another seed and shuffle (cube-hunt: <c>{ ...opts, seed, shuffle }</c>).</summary>
        public GenerationOptions With(long seed, double shuffle)
        {
            var copy = (GenerationOptions)MemberwiseClone();
            copy.Seed = seed;
            copy.Shuffle = shuffle;
            return copy;
        }

        internal void Check()
        {
            if (Columns < 1) throw new ArgumentOutOfRangeException(nameof(Columns), Columns, "at least one tray column");
            if (SlotCount < 1) throw new ArgumentOutOfRangeException(nameof(SlotCount), SlotCount, "at least one slot");
            if (MinBlock < 1 || MaxBlock < 1) throw new ArgumentOutOfRangeException(nameof(MinBlock), "block sizes are at least 1");
            if (double.IsNaN(Shuffle) || double.IsInfinity(Shuffle)) throw new ArgumentOutOfRangeException(nameof(Shuffle));
        }
    }

    /// <summary>
    /// Crate sizes for one color (cube-hunt CrateSizes): mostly small (Min..SmallMax), now and then a big one
    /// (BigMin..Max). Derived from the picture size unless MinBlock / MaxBlock are given. A given MinBlock / MaxBlock is
    /// used as is, even when it leaves the other bounds out of order (TS_BASELINE F-4, kept).
    /// </summary>
    public readonly struct CrateSizes
    {
        public int Min { get; }
        public int SmallMax { get; }
        public int BigMin { get; }
        public int Max { get; }
        public double BigChance { get; }

        public CrateSizes(int min, int smallMax, int bigMin, int max, double bigChance)
        {
            Min = min;
            SmallMax = smallMax;
            BigMin = bigMin;
            Max = max;
            BigChance = bigChance;
        }

        /// <summary>The sizes for a picture of <paramref name="total"/> cubes. Math.round is JavaScript's (halves up).</summary>
        public static CrateSizes For(int total, GenerationOptions options)
        {
            int min = options.MinBlock ?? Math.Max(6, JsMath.RoundToInt(total / 150.0));
            int smallMax = Math.Max(min + 8, JsMath.RoundToInt(total / 55.0));
            int bigMin = Math.Max(smallMax + 10, JsMath.RoundToInt(total / 32.0));
            int max = options.MaxBlock ?? Math.Max(bigMin + 6, Math.Min(80, JsMath.RoundToInt(total / 16.0)));
            return new CrateSizes(min, smallMax, bigMin, max, options.BigChance);
        }

        public override string ToString() => $"small {Min}..{SmallMax}, big {BigMin}..{Max} ({BigChance})";
    }
}
