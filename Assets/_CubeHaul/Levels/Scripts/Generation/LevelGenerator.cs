using System;
using System.Collections.Generic;
using CubeHaul.Domain;

namespace CubeHaul.Levels.Generation
{
    /// <summary>
    /// cube-hunt generateLevel (src/levels/generator.ts @ 559dfd9): a tray for a picture, not checked for
    /// solvability (see <see cref="LevelBuilder"/>). Same seed, same tray as TypeScript, crate for crate.
    ///
    /// Every JavaScript sort here is stable (Array.prototype.sort since ES2019), so equal keys keep their earlier
    /// order. C#'s Array.Sort / List.Sort are not stable; each sort below therefore compares the key and then the
    /// position the item had before the sort, which gives exactly the stable order whatever algorithm sorts
    /// (PORTING_PLAN R-01).
    /// </summary>
    public static class LevelGenerator
    {
        public static TrayDefinition Generate(PictureDefinition picture, GenerationOptions options)
        {
            if (picture == null) throw new ArgumentNullException(nameof(picture));
            if (options == null) throw new ArgumentNullException(nameof(options));
            options.Check();
            var rng = new Mulberry32(options.Seed);
            int columns = options.Columns;
            int[] cells = picture.Cells;
            int total = 0;
            foreach (int c in cells)
                if (c != Cells.Empty) total++;
            CrateSizes sizes = CrateSizes.For(total, options);

            int[] layers = PeelLayers.Of(picture, options.Edges);
            int maxLayer = 0;
            foreach (int l in layers) maxLayer = Math.Max(maxLayer, l);

            // Each color's cells in peel order (ties: cell index, i.e. the order they were collected in), cut into crates.
            int colors = picture.Palette.Length;
            var byColor = new List<int>[colors];
            for (int c = 0; c < colors; c++) byColor[c] = new List<int>();
            for (int i = 0; i < cells.Length; i++)
                if (cells[i] != Cells.Empty) byColor[cells[i]].Add(i);

            var blocks = new List<BlockDefinition>();
            var times = new List<double>();
            int id = 0;
            for (int color = 0; color < colors; color++)
            {
                List<int> own = byColor[color];
                if (own.Count == 0) continue;
                own.Sort((a, b) => layers[a] != layers[b] ? layers[a].CompareTo(layers[b]) : a.CompareTo(b));
                int offset = 0;
                foreach (int size in SplitCount(own.Count, sizes, rng))
                {
                    // A crate is "due" when its first cells open up.
                    times.Add(layers[own[offset]]);
                    blocks.Add(new BlockDefinition(id++, color, size));
                    offset += size;
                }
            }

            double noise = options.Shuffle * (maxLayer + 1);
            for (int k = 0; k < times.Count; k++) times[k] += rng.Next() * noise;
            var order = new int[blocks.Count];
            for (int k = 0; k < order.Length; k++) order[k] = k;
            Array.Sort(order, (a, b) =>
            {
                int byTime = times[a].CompareTo(times[b]);
                return byTime != 0 ? byTime : a.CompareTo(b);
            });

            // Deal row by row so the earliest crates end up at the front, in random column order.
            var cols = new List<BlockDefinition>[columns];
            for (int c = 0; c < columns; c++) cols[c] = new List<BlockDefinition>();
            var columnIndices = new int[columns];
            for (int c = 0; c < columns; c++) columnIndices[c] = c;
            for (int i = 0; i < order.Length; i += columns)
            {
                int[] deal = Shuffled(columnIndices, rng);
                // Prefer shorter columns for a ragged last row (stable: ties keep the shuffled order).
                SortStable(deal, c => cols[c].Count);
                for (int k = 0; k < columns && i + k < order.Length; k++) cols[deal[k]].Add(blocks[order[i + k]]);
            }

            var result = new BlockDefinition[columns][];
            for (int c = 0; c < columns; c++) result[c] = cols[c].ToArray();
            return new TrayDefinition { Columns = result };
        }

        /// <summary>cube-hunt splitCount: one color's cube count cut into crate sizes.</summary>
        public static List<int> SplitCount(int n, CrateSizes sz, Mulberry32 rng)
        {
            var output = new List<int>();
            int rem = n;
            while (rem > 0)
            {
                int s;
                if (rem <= sz.SmallMax) s = rem;
                else
                {
                    // The rng is drawn first, whatever the second test says (&& evaluates left to right in both languages).
                    bool big = rng.Next() < sz.BigChance && rem >= sz.BigMin + sz.Min;
                    int lo = big ? sz.BigMin : sz.Min;
                    int hi = Math.Min(big ? sz.Max : sz.SmallMax, rem - sz.Min);
                    s = lo + (int)Math.Floor(rng.Next() * (hi - lo + 1));
                }
                if (s <= 0) throw new InvalidOperationException($"crate size {s} for {n} cubes with sizes {sz}"); // cannot happen; guards an endless loop
                output.Add(s);
                rem -= s;
            }
            return output;
        }

        /// <summary>cube-hunt shuffled: Fisher-Yates driven by the rng, on a copy.</summary>
        public static int[] Shuffled(int[] items, Mulberry32 rng)
        {
            var output = (int[])items.Clone();
            for (int i = output.Length - 1; i > 0; i--)
            {
                int j = (int)Math.Floor(rng.Next() * (i + 1));
                int t = output[i];
                output[i] = output[j];
                output[j] = t;
            }
            return output;
        }

        /// <summary>Stable insertion sort by key (at most 10 tray columns).</summary>
        static void SortStable(int[] items, Func<int, int> key)
        {
            for (int i = 1; i < items.Length; i++)
            {
                int x = items[i], kx = key(x), j = i - 1;
                while (j >= 0 && key(items[j]) > kx)
                {
                    items[j + 1] = items[j];
                    j--;
                }
                items[j + 1] = x;
            }
        }
    }
}
