/** Greedy solver and random-player win rate (B-24). */
import { builtInLevels } from "@ref/levels/art";
import { buildLevel } from "@ref/levels/generator";
import { randomWinRate, solveGreedy } from "@ref/levels/solver";
import { describe, expect, it } from "vitest";
import { level, pic, solid } from "./helpers";

// One column, open at the bottom: color 1 on top of color 0.
const stacked = pic(["1", "0"]);

describe("solveGreedy", () => {
  it("wins a one-crate level: one tap, one tick per cube (one truck per slot per tick)", () => {
    expect(solveGreedy(level(solid(3, 1), [[[0, 3]]], 1))).toEqual({ solved: true, ticks: 3, taps: 1 });
  });

  it("taps a crate that can eat before one that cannot", () => {
    // Column 0 holds the blocked color; greedy skips it, eats color 0, then taps column 0.
    expect(solveGreedy(level(stacked, [[[1, 1]], [[0, 1]]], 1))).toEqual({ solved: true, ticks: 2, taps: 2 });
  });

  it("when nothing can eat, it is forced to tap a blocked crate, and reports the loss", () => {
    expect(solveGreedy(level(stacked, [[[1, 1], [0, 1]]], 1))).toEqual({ solved: false, ticks: 1, taps: 1 });
  });

  it("gives up after maxTicks", () => {
    const lv = level(solid(1, 5), [[[0, 5]]], 1);
    expect(solveGreedy(lv)).toEqual({ solved: true, ticks: 5, taps: 1 });
    expect(solveGreedy(lv, 2)).toEqual({ solved: false, ticks: 2, taps: 1 });
  });

  it("does not change the level it is given", () => {
    const lv = level(pic(["0110", "1001"]), [[[0, 2], [1, 2]], [[1, 2], [0, 2]]], 2);
    const before = JSON.stringify(lv.columns);
    solveGreedy(lv);
    expect(JSON.stringify(lv.columns)).toBe(before);
  });
});

describe("randomWinRate", () => {
  it("is 1 when no order of taps can lose, and 0 when every order loses", () => {
    expect(randomWinRate(level(solid(2, 1), [[[0, 1]], [[0, 1]]], 2))).toBe(1);
    expect(randomWinRate(level(stacked, [[[1, 1], [0, 1]]], 1))).toBe(0);
  });

  it("is a share of `games` played with seeds seed, seed + 101, seed + 202, ...", () => {
    const lv = buildLevel(builtInLevels()[2].picture, builtInLevels()[2]).level;
    const one = (seed: number) => randomWinRate(lv, 1, seed);
    const seed = 7;
    const parts = [0, 1, 2, 3, 4, 5, 6].map((k) => one(seed + k * 101));
    expect(randomWinRate(lv, 7, seed)).toBeCloseTo(parts.reduce((a, b) => a + b, 0) / 7, 12);
    expect(parts.every((p) => p === 0 || p === 1)).toBe(true);
  });

  it("is the same every time for the same level and seed", () => {
    const lv = buildLevel(builtInLevels()[4].picture, builtInLevels()[4]).level;
    expect(randomWinRate(lv, 24, 5)).toBe(randomWinRate(lv, 24, 5));
  });
});
