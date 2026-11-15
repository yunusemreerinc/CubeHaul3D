/**
 * Seeded RNG and level generation: reproducibility, golden outputs for the 10 built-in levels,
 * crate-size and tray-shape rules, and the fallback when no tray can be proven winnable.
 */
import { EMPTY, type Picture } from "@ref/core/types";
import { Board } from "@ref/core/board";
import { builtInLevels } from "@ref/levels/art";
import { buildLevel, generateLevel, peelLayers, type GenOptions } from "@ref/levels/generator";
import { mulberry32 } from "@ref/levels/rng";
import { randomWinRate, solveGreedy } from "@ref/levels/solver";
import { createHash } from "node:crypto";
import { describe, expect, it, vi } from "vitest";
import { pic } from "./helpers";

const specs = builtInLevels();
const draw = (seed: number, n: number) => {
  const r = mulberry32(seed);
  return Array.from({ length: n }, () => r());
};

describe("mulberry32 (B-20, I-21)", () => {
  it.each([
    [0, [1144304738, 1416247, 958946056, 627933444, 2007157716]],
    [1, [2693262067, 11749833, 2265367787, 4213581821, 4159151403]],
    [11, [2197249643, 2276102492, 2611849345, 2534707746, 3654058086]],
    [1234, [314799534, 3021131492, 3877737075, 4168477787, 175938938]],
    [2147483647, [1842962257, 546041740, 1654754255, 1702490205, 513796057]],
  ])("seed %i gives the golden first five values (as uint32 / 2^32)", (seed, expected) => {
    expect(draw(seed, 5).map((v) => v * 2 ** 32)).toEqual(expected);
  });

  it("returns whole multiples of 2^-32 in [0, 1)", () => {
    for (const v of draw(99, 100_000)) {
      expect(v >= 0 && v < 1).toBe(true);
      expect(Number.isInteger(v * 2 ** 32)).toBe(true);
    }
  });

  it("takes the seed modulo 2^32, so -1 and 2^32 - 1 are the same seed", () => {
    expect(draw(-1, 8)).toEqual(draw(2 ** 32 - 1, 8));
    expect(draw(2 ** 32 + 5, 8)).toEqual(draw(5, 8));
    expect(draw(1, 8)).not.toEqual(draw(2, 8));
  });
});

describe("generateLevel reproducibility (B-20, I-20)", () => {
  const sunset = specs[2].picture;

  it.each([0, 0.2, 1, 2])("same seed, same tray (shuffle %s)", (shuffle) => {
    for (const edges of ["bottom", "sides", "all"] as const) {
      const opts: GenOptions = { seed: 42, shuffle, edges };
      expect(generateLevel(sunset, opts)).toEqual(generateLevel(sunset, opts));
    }
  });

  it("another seed gives another tray", () => {
    const a = generateLevel(sunset, { seed: 1, shuffle: 0.5 });
    const b = generateLevel(sunset, { seed: 2, shuffle: 0.5 });
    expect(a.columns).not.toEqual(b.columns);
  });

  it("the shuffle only reorders: the same seed cuts the same crates whatever the shuffle", () => {
    // splitCount draws first, then one noise draw per crate (even when the noise is 0), then the deal.
    const crates = (shuffle: number) =>
      generateLevel(sunset, { seed: 9, shuffle })
        .columns.flat()
        .sort((x, y) => x.id - y.id);
    expect(crates(0)).toEqual(crates(0.7));
    expect(crates(0)).toEqual(crates(3));
    expect(generateLevel(sunset, { seed: 9, shuffle: 0 }).columns).not.toEqual(
      generateLevel(sunset, { seed: 9, shuffle: 3 }).columns,
    );
  });

  it("buildLevel is reproducible, winRate included", () => {
    const spec = specs[3];
    const a = buildLevel(spec.picture, spec);
    const b = buildLevel(spec.picture, spec);
    expect(a.winRate).toBe(b.winRate);
    expect(a.level.columns).toEqual(b.level.columns);
  });

  it("buildLevel's first candidate is generateLevel with the level's own seed", () => {
    const spec = specs[0];
    const one = buildLevel(spec.picture, spec, 1);
    expect(one.level.columns).toEqual(generateLevel(spec.picture, spec).columns);
    expect(one.winRate).toBe(randomWinRate(one.level, 24, spec.seed));
  });
});

describe("golden built-in levels (B-24, B-25)", () => {
  // Measured at cube-hunt 559dfd9. Any change here means the C# port has a new reference to match.
  const golden = [
    { name: "Strawberry", size: "29x29", cubes: 527, crates: 45, columns: [9, 9, 9, 9, 9], winRate: 1, greedy: [114, 45], sha256: "5c52ecd9bcdaa7e8" },
    { name: "Mushroom", size: "36x36", cubes: 1296, crates: 61, columns: [12, 12, 12, 13, 12], winRate: 20 / 24, greedy: [311, 61], sha256: "fcdd58e556b5cf46" },
    { name: "Sunset", size: "36x36", cubes: 1296, crates: 49, columns: [10, 10, 10, 9, 10], winRate: 13 / 24, greedy: [329, 49], sha256: "c7687aa439cb8477" },
    { name: "Ringed Planet", size: "46x46", cubes: 2116, crates: 66, columns: [13, 13, 13, 14, 13], winRate: 12 / 24, greedy: [492, 66], sha256: "1f92abe91f2a3cd1" },
    { name: "Cat", size: "43x43", cubes: 1849, crates: 64, columns: [13, 12, 13, 13, 13], winRate: 10 / 24, greedy: [409, 64], sha256: "c956ceb7fdfef8ed" },
    { name: "Cactus", size: "40x43", cubes: 1720, crates: 72, columns: [15, 14, 14, 14, 15], winRate: 19 / 24, greedy: [357, 72], sha256: "8f4a47b37b1b2eb5" },
    { name: "Rocket", size: "39x46", cubes: 1794, crates: 65, columns: [13, 13, 13, 13, 13], winRate: 17 / 24, greedy: [567, 65], sha256: "6ea12d3b67acf4cd" },
    { name: "Rubber Duck", size: "43x43", cubes: 1849, crates: 60, columns: [12, 12, 12, 12, 12], winRate: 17 / 24, greedy: [453, 60], sha256: "df6975f47b8b9d65" },
    { name: "Ice Cream", size: "36x46", cubes: 1656, crates: 62, columns: [13, 12, 12, 13, 12], winRate: 12 / 24, greedy: [385, 62], sha256: "b3ff881236c7c61a" },
    { name: "Watermelon", size: "46x35", cubes: 1610, crates: 62, columns: [12, 13, 13, 12, 12], winRate: 8 / 24, greedy: [360, 62], sha256: "328b3036a808916e" },
  ];

  it.each(specs.map((spec, i) => [spec.picture.name, i] as const))("%s", (_name, i) => {
    const spec = specs[i];
    const built = buildLevel(spec.picture, spec);
    const greedy = solveGreedy(built.level);
    expect(greedy.solved).toBe(true);
    expect({
      name: spec.picture.name,
      size: `${spec.picture.width}x${spec.picture.height}`,
      cubes: spec.picture.cells.filter((c) => c !== EMPTY).length,
      crates: built.level.columns.flat().length,
      columns: built.level.columns.map((c) => c.length),
      winRate: built.winRate,
      greedy: [greedy.ticks, greedy.taps],
      sha256: createHash("sha256").update(JSON.stringify(built.level.columns)).digest("hex").slice(0, 16),
    }).toEqual(golden[i]);
    expect(built.level.slotCount).toBe(5);
    expect(built.level.edges).toBe(spec.edges);
  });
});

/** The crate-size limits generateLevel derives for a picture (B-22). */
function limits(picture: Picture, opts: GenOptions) {
  const total = picture.cells.filter((c) => c !== EMPTY).length;
  const min = opts.minBlock ?? Math.max(6, Math.round(total / 150));
  const smallMax = Math.max(min + 8, Math.round(total / 55));
  const bigMin = Math.max(smallMax + 10, Math.round(total / 32));
  const max = opts.maxBlock ?? Math.max(bigMin + 6, Math.min(80, Math.round(total / 16)));
  return { min, max };
}

describe("tray rules (B-22, B-23, B-26, I-01, I-22)", () => {
  const cases = specs.flatMap((spec) =>
    [
      { ...spec },
      { ...spec, seed: spec.seed + 1, shuffle: 0 },
      { ...spec, seed: spec.seed + 2, shuffle: 3, edges: "all" as const },
    ].map((opts) => [spec.picture.name, opts] as const),
  );

  it.each(cases)("%s: truck totals, crate sizes and column balance", (_name, opts) => {
    const p = opts.picture;
    const lv = generateLevel(p, opts);
    const crates = lv.columns.flat();
    const { min, max } = limits(p, opts);
    for (let c = 0; c < p.palette.length; c++) {
      const cubes = p.cells.filter((x) => x === c).length;
      const mine = crates.filter((k) => k.color === c);
      expect(mine.reduce((n, k) => n + k.count, 0)).toBe(cubes);
      for (const k of mine) {
        expect(k.count).toBeGreaterThanOrEqual(1);
        expect(k.count).toBeLessThanOrEqual(max);
        // A crate under the minimum only happens for a color with fewer cubes than that: its one crate.
        if (k.count < min) expect(mine).toEqual([k]);
      }
    }
    expect(new Set(crates.map((k) => k.id)).size).toBe(crates.length);
    const lengths = lv.columns.map((c) => c.length);
    expect(lv.columns).toHaveLength(5);
    expect(Math.max(...lengths) - Math.min(...lengths)).toBeLessThanOrEqual(1);
  });

  it("Cactus has a 4-truck crate because its flower centre has only 4 cubes", () => {
    const cactus = specs[5];
    const lv = buildLevel(cactus.picture, cactus).level;
    const small = lv.columns.flat().filter((k) => k.count < limits(cactus.picture, cactus).min);
    expect(small).toHaveLength(1);
    expect(small[0].count).toBe(4);
    expect(cactus.picture.cells.filter((c) => c === small[0].color).length).toBe(4);
  });

  it.each([3, 7])("honours columns = %i, and slots and edges defaults", (columns) => {
    const lv = generateLevel(specs[0].picture, { seed: 3, shuffle: 0.5, columns });
    const lengths = lv.columns.map((c) => c.length);
    expect(lv.columns).toHaveLength(columns);
    expect(Math.max(...lengths) - Math.min(...lengths)).toBeLessThanOrEqual(1);
    expect(lv.slotCount).toBe(5);
    expect(lv.edges).toBe("all");
    expect(generateLevel(specs[0].picture, { seed: 3, shuffle: 0.5, slots: 4, edges: "sides" })).toMatchObject({
      slotCount: 4,
      edges: "sides",
    });
  });

  it("FINDING: minBlock / maxBlock overrides are not honoured when they clash with the derived sizes", () => {
    // splitCount draws from lo..hi with hi = min(max, rem - min). With maxBlock below the derived bigMin
    // (Mushroom: bigMin 41 > 30) a big crate's range is inverted and lands in 31..40; with a large
    // minBlock, rem - min can drop under min and crates come out smaller than minBlock. Totals stay right.
    // No built-in level sets either option, and with the derived sizes no crate is ever out of range
    // (checked above, and 86 426 crates over 2 000 seeds in docs/TS_BASELINE.md). Recorded as current behaviour.
    const p = specs[1].picture;
    const capped = generateLevel(p, { seed: 4, shuffle: 0, minBlock: 20, maxBlock: 30 }).columns.flat();
    expect(Math.max(...capped.map((k) => k.count))).toBe(39);
    const floored = generateLevel(p, { seed: 5, shuffle: 0, minBlock: 40 }).columns.flat();
    expect(Math.min(...floored.map((k) => k.count))).toBe(13);
    for (const crates of [capped, floored]) {
      expect(crates.reduce((n, k) => n + k.count, 0)).toBe(p.cells.filter((c) => c !== EMPTY).length);
    }
  });
});

describe("peelLayers (B-21)", () => {
  it("layer 0 is what is reachable at the start; empty cells get -1", () => {
    const p = pic(["0.1", "011", "000"]);
    const layers = peelLayers(p, "bottom");
    const b = new Board(p, "bottom");
    const first = [0, 1].flatMap((c) => b.reachableCells(c)).sort((x, y) => x - y);
    expect(first).toEqual([...layers.keys()].filter((i) => layers[i] === 0));
    expect(layers[1]).toBe(-1);
    for (let i = 0; i < p.cells.length; i++) if (p.cells[i] !== EMPTY) expect(layers[i]).toBeGreaterThanOrEqual(0);
  });
});

describe("buildLevel fallback when no tray can be proven winnable (B-24)", () => {
  it("warns, falls back to shuffle 0, and reports a win rate of 0", () => {
    // Alternating colors in one column with a single slot: a crate always blocks the other color.
    const p = { ...pic(["1", "0", "1", "0"]), name: "Zebra" };
    const warn = vi.spyOn(console, "warn").mockImplementation(() => {});
    const built = buildLevel(p, { seed: 1, shuffle: 0.5, edges: "bottom", slots: 1 });
    expect(warn).toHaveBeenCalledWith('Could not verify level "Zebra" as solvable');
    warn.mockRestore();
    expect(solveGreedy(built.level).solved).toBe(false);
    expect(built.winRate).toBe(0);
    expect(built.level.columns).toEqual(generateLevel(p, { seed: 1, shuffle: 0, edges: "bottom", slots: 1 }).columns);
  });
});
