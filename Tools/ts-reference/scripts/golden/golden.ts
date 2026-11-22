/**
 * Builds every golden file from the cube-hunt reference source: the 10 built-in levels as LevelDefinition JSON,
 * generator fixtures (RNG, peel layers, generateLevel, the full buildLevel trace) and step-by-step game
 * snapshots. Pure: the same source always gives byte-identical files (checked by tests/golden.test.ts).
 * See docs/GOLDEN_FIXTURES.md.
 */
import { Game } from "../../vendor/cube-hunt/src/core/game";
import type { Level, Picture } from "../../vendor/cube-hunt/src/core/types";
import { builtInLevels } from "../../vendor/cube-hunt/src/levels/art";
import { buildLevel, generateLevel, peelLayers, type BuildOptions } from "../../vendor/cube-hunt/src/levels/generator";
import { mulberry32 } from "../../vendor/cube-hunt/src/levels/rng";
import { randomWinRate, solveGreedy } from "../../vendor/cube-hunt/src/levels/solver";
import { formatLevelDefinition } from "../../../level-format/src/format";
import type { LevelDefinition } from "../../../level-format/src/levelDefinition";
import { readLevelDefinition } from "../../../level-format/src/validate";
import { builtInDefinition } from "../level-examples";
import { columnsSha256, sha256 } from "./hash";
import { stringify, toFile } from "./json";
import { parityFiles } from "./parity";
import { Recorder } from "./snapshot";

export const SOURCE = {
  repo: "https://github.com/yunusemreerinc/cube-hunt",
  commit: "559dfd90977b1e15367bd7de25aec9fbae4b52f6",
};
export const LEVELS_DIR = "Assets/_CubeHaul/Levels/Content";
export const GOLDEN_DIR = "Assets/_CubeHaul/Tests/Fixtures/Golden";

/** levelId and file name of each built-in level, in game order. */
export const LEVEL_IDS = [
  "strawberry",
  "mushroom",
  "sunset",
  "ringed-planet",
  "cat",
  "cactus",
  "rocket",
  "rubber-duck",
  "ice-cream",
  "watermelon",
];
export const levelFile = (i: number) => `${LEVELS_DIR}/level-${String(i + 1).padStart(2, "0")}-${LEVEL_IDS[i]}.level.json`;

const ref = { builtInLevels, buildLevel, solveGreedy };
export const header = (kind: string, description: string) => ({ format: `cubehaul.golden.${kind}`, version: 1, source: SOURCE, description });

/** A static LevelDefinition as a cube-hunt Level. */
export function toLevel(d: LevelDefinition): Level {
  const picture: Picture = {
    name: d.name,
    width: d.picture.width,
    height: d.picture.height,
    cells: Int16Array.from(d.picture.cells),
    palette: [...d.picture.palette],
  };
  return { picture, columns: d.tray!.columns.map((col) => col.map((k) => ({ ...k }))), slotCount: d.slotCount, edges: d.edges };
}

// ---------------------------------------------------------------- generator fixtures

const RNG_SEEDS = [0, 1, 11, 1234, 2147483647, 4294967295];

function rngFixture() {
  return {
    ...header("rng", "mulberry32(seed): the first 256 values of each seed, as uint32 (value * 2^32). Seeds are taken modulo 2^32."),
    count: 256,
    seeds: RNG_SEEDS.map((seed) => {
      const r = mulberry32(seed);
      return { seed, values: Array.from({ length: 256 }, () => r() * 2 ** 32) };
    }),
  };
}

function peelFixture() {
  return {
    ...header("peel-layers", "peelLayers(picture, edges) of each built-in level with its own edges; -1 for empty cells, row-major."),
    levels: builtInLevels().map((spec, i) => ({
      levelId: LEVEL_IDS[i],
      edges: spec.edges ?? "all",
      width: spec.picture.width,
      layers: [...peelLayers(spec.picture, spec.edges ?? "all")],
    })),
  };
}

function generateFixture() {
  const runs = [
    { seed: 1, shuffle: 0 },
    { seed: 1, shuffle: 1.5 },
    { seed: 77, shuffle: 0 },
    { seed: 77, shuffle: 1.5 },
  ];
  return {
    ...header(
      "generate",
      "generateLevel(picture, { ...spec, seed, shuffle }) for each built-in level: one tray per run, no solver. columnsSha256 = sha256(JSON.stringify(columns)) with keys id, color, count and no spaces.",
    ),
    levels: builtInLevels().map((spec, i) => ({
      levelId: LEVEL_IDS[i],
      runs: runs.map(({ seed, shuffle }) => {
        const lv = generateLevel(spec.picture, { ...spec, seed, shuffle });
        return {
          seed,
          shuffle,
          crates: lv.columns.flat().length,
          columnLengths: lv.columns.map((c) => c.length),
          fronts: lv.columns.map((c) => (c[0] ? [c[0].id, c[0].color, c[0].count] : null)),
          columnsSha256: columnsSha256(lv.columns),
        };
      }),
    })),
  };
}

/** buildLevel, step by step: every candidate it tries, with the solver and random-player results. */
function buildFixture() {
  return {
    ...header(
      "build",
      "buildLevel(spec.picture, spec) traced: candidates k = 0.. with seed spec.seed + 7919k until the win rate is within 0.04 of the target. games[g] is randomWinRate(level, 1, spec.seed + 101g).",
    ),
    levels: builtInLevels().map((spec, i) => {
      const target = spec.targetWinRate ?? 1;
      const candidates: unknown[] = [];
      let best: { k: number; winRate: number } | null = null;
      for (let k = 0; k < 16; k++) {
        const seed = spec.seed + k * 7919;
        const level = generateLevel(spec.picture, { ...spec, seed });
        const greedy = solveGreedy(level);
        const entry: Record<string, unknown> = {
          k,
          seed,
          crates: level.columns.flat().length,
          columnsSha256: columnsSha256(level.columns),
          greedy: [greedy.solved, greedy.ticks, greedy.taps],
        };
        candidates.push(entry);
        if (!greedy.solved) continue;
        const games = Array.from({ length: 24 }, (_, g) => randomWinRate(level, 1, spec.seed + g * 101));
        const winRate = randomWinRate(level, 24, spec.seed);
        entry.games = games;
        entry.winRate = winRate;
        if (!best || Math.abs(winRate - target) < Math.abs(best.winRate - target)) best = { k, winRate };
        if (Math.abs(winRate - target) < 0.04) break;
      }
      const built = buildLevel(spec.picture, spec);
      if (!best) throw new Error(`${spec.picture.name}: no candidate solved; the fallback path is not traced`);
      const chosen = candidates[best.k] as { columnsSha256: string };
      if (chosen.columnsSha256 !== columnsSha256(built.level.columns) || best.winRate !== built.winRate) {
        throw new Error(`${spec.picture.name}: the trace does not match buildLevel`);
      }
      return {
        levelId: LEVEL_IDS[i],
        file: levelFile(i),
        options: {
          seed: spec.seed,
          shuffle: spec.shuffle,
          targetWinRate: target,
          edges: spec.edges ?? "all",
          columns: spec.columns ?? 5,
          slots: spec.slots ?? 5,
          bigChance: spec.bigChance ?? 0.15,
        },
        candidates,
        chosen: best.k,
        winRate: built.winRate,
      };
    }),
  };
}

// ---------------------------------------------------------------- generator cases

interface GenCase {
  name: string;
  description: string;
  /** A level file whose picture to use, or an inline picture. */
  picture: string | { width: number; height: number; palette: string[]; cells: number[] };
  edges: LevelDefinition["edges"];
  slotCount: number;
  /** LevelDefinition generation settings; candidates is buildLevel's third argument. */
  generation: NonNullable<LevelDefinition["generation"]>;
}

const rowsPicture = (rows: string[]) => {
  const cells = rows.flatMap((r) => [...r].map((ch) => (ch === "." ? -1 : Number(ch))));
  const colors = Math.max(...cells) + 1;
  return { width: rows[0].length, height: rows.length, palette: Array.from({ length: colors }, (_, c) => `#0000${String(c).padStart(2, "0")}`), cells };
};

/** A w x h picture of diagonal color bands, every cell filled: w * h cubes. */
const bandsPicture = (w: number, h: number, colors: number) =>
  rowsPicture(Array.from({ length: h }, (_, y) => Array.from({ length: w }, (_, x) => String(Math.floor((x + y) / 4) % colors)).join("")));

const HEART = { ...rowsPicture([".00.00.", "0010000", "0000000", ".00000.", "..000..", "...0..."]), palette: ["#e8424f", "#ffffff"] };

const GEN_CASES: GenCase[] = [
  {
    name: "heart-example",
    description: "LevelFormat/examples/heart.generated.level.json: generation settings only, baked at build time or at runtime",
    picture: HEART,
    edges: "bottom",
    slotCount: 5,
    generation: { seed: 7, shuffle: 0.5, targetWinRate: 0.8 },
  },
  {
    name: "strawberry-f4-block-bounds",
    description: "F-4: minBlock 40 and maxBlock 12 given by hand; crate sizes leave the bounds, kept as in TypeScript",
    picture: levelFile(0),
    edges: "all",
    slotCount: 5,
    generation: { seed: 3, shuffle: 0.5, targetWinRate: 0.9, minBlock: 40, maxBlock: 12, candidates: 4 },
  },
  {
    name: "cat-one-column-no-big",
    description: "One tray column, bigChance 0: every crate small",
    picture: levelFile(4),
    edges: "bottom",
    slotCount: 5,
    generation: { seed: 9, shuffle: 1, targetWinRate: 0.5, columns: 1, bigChance: 0, candidates: 3 },
  },
  {
    name: "rocket-ten-columns-all-big",
    description: "Ten columns (a ragged last row), bigChance 1: big crates whenever there is room",
    picture: levelFile(6),
    edges: "sides",
    slotCount: 6,
    generation: { seed: 4, shuffle: 0.7, targetWinRate: 0.6, columns: 10, bigChance: 1, candidates: 3 },
  },
  {
    name: "sunset-max-seed",
    description: "seed 2147483647: seed + 7919k passes 2^31 (int overflow in C#) and mulberry32 takes it modulo 2^32",
    picture: levelFile(2),
    edges: "sides",
    slotCount: 5,
    generation: { seed: 2147483647, shuffle: 0.4, targetWinRate: 0.3, candidates: 5 },
  },
  {
    name: "mushroom-seed-0-max-shuffle",
    description: "seed 0, shuffle 100 (the schema maximum): the order is almost all noise",
    picture: levelFile(1),
    edges: "bottom",
    slotCount: 5,
    generation: { seed: 0, shuffle: 100, targetWinRate: 0.7, candidates: 3 },
  },
  {
    name: "bands-fallback",
    description: "No candidate wins with greedy play; the shuffle is backed off and fallback k = 3 is the first the solver wins",
    picture: rowsPicture(["000000", "111111", "222222", "333333", "000000", "111111"]),
    edges: "bottom",
    slotCount: 2,
    generation: { seed: 5, shuffle: 3, columns: 1, minBlock: 2, maxBlock: 4, candidates: 2 },
  },
  {
    name: "stripes-unverified",
    description: "Neither a candidate nor a fallback wins: buildLevel warns and returns the unshuffled tray, not verified",
    picture: rowsPicture(["0123", "1230", "2301", "3012", "0123", "1230"]),
    edges: "bottom",
    slotCount: 1,
    generation: { seed: 1, shuffle: 1, columns: 1, minBlock: 2, maxBlock: 4, candidates: 2 },
  },
  {
    name: "round-half-1275-cubes",
    description: "1275 cubes: total / 150 = 8.5, so minBlock is Math.round(8.5) = 9 (round half up), not 8 (C# Math.Round's default, half to even)",
    picture: bandsPicture(51, 25, 3),
    edges: "bottom",
    slotCount: 5,
    generation: { seed: 2, shuffle: 0.3, candidates: 1 },
  },
  {
    name: "round-half-1040-cubes",
    description: "1040 cubes: total / 32 = 32.5 (bigMin 33, not 32) and total / 16 = 65 (maxBlock 65)",
    picture: bandsPicture(52, 20, 4),
    edges: "all",
    slotCount: 5,
    generation: { seed: 6, shuffle: 0.3, candidates: 1 },
  },
  {
    name: "round-half-1000-cubes",
    description: "1000 cubes: total / 16 = 62.5, so maxBlock is 63, not 62",
    picture: bandsPicture(40, 25, 2),
    edges: "sides",
    slotCount: 5,
    generation: { seed: 8, shuffle: 0.3, candidates: 1 },
  },
  {
    name: "single-cube",
    description: "The smallest picture: one cube, one crate",
    picture: rowsPicture(["0"]),
    edges: "bottom",
    slotCount: 5,
    generation: { seed: 1, shuffle: 0 },
  },
];

type Columns = { id: number; color: number; count: number }[][];
const crates = (columns: Columns) => columns.map((col) => col.map((k) => [k.id, k.color, k.count]));

/** buildLevel(picture, opts, candidates) with every level it tries, in order; checked against buildLevel itself. */
export function traceBuild(picture: Picture, opts: BuildOptions, candidates: number) {
  const target = opts.targetWinRate ?? 1;
  const tried: Record<string, unknown>[] = [];
  const attempt = (phase: string, k: number, seed: number, shuffle: number) => {
    const level = generateLevel(picture, { ...opts, seed, shuffle });
    const greedy = solveGreedy(level);
    const entry: Record<string, unknown> = { phase, k, seed, shuffle, columnsSha256: columnsSha256(level.columns), greedy: [greedy.solved, greedy.ticks, greedy.taps] };
    tried.push(entry);
    return { level, solved: greedy.solved, entry };
  };
  const games = (level: Level) => Array.from({ length: 24 }, (_, g) => randomWinRate(level, 1, opts.seed + g * 101));
  let best: { level: Level; winRate: number; index: number } | null = null;
  for (let k = 0; k < candidates; k++) {
    const a = attempt("candidate", k, opts.seed + k * 7919, opts.shuffle);
    if (!a.solved) continue;
    const winRate = randomWinRate(a.level, 24, opts.seed);
    a.entry.games = games(a.level);
    a.entry.winRate = winRate;
    if (!best || Math.abs(winRate - target) < Math.abs(best.winRate - target)) best = { level: a.level, winRate, index: tried.length - 1 };
    if (Math.abs(winRate - target) < 0.04) break;
  }
  let path = "candidate";
  if (!best) {
    path = "unverified";
    for (let k = 1; k <= 8; k++) {
      const a = attempt("fallback", k, opts.seed + k, opts.shuffle * (1 - k / 8));
      if (!a.solved) continue;
      best = { level: a.level, winRate: randomWinRate(a.level, 24, opts.seed), index: tried.length - 1 };
      path = "fallback";
      break;
    }
  }
  if (!best) {
    const a = attempt("unverified", 0, opts.seed, 0);
    best = { level: a.level, winRate: randomWinRate(a.level, 24, opts.seed), index: tried.length - 1 };
  }
  const warn = console.warn;
  console.warn = () => {};
  const built = buildLevel(picture, opts, candidates);
  console.warn = warn;
  if (columnsSha256(built.level.columns) !== columnsSha256(best.level.columns) || built.winRate !== best.winRate) {
    throw new Error(`${picture.name}: the trace does not match buildLevel`);
  }
  const greedy = solveGreedy(built.level);
  return {
    tried,
    result: {
      path,
      tried: best.index,
      winRate: built.winRate,
      greedy: [greedy.solved, greedy.ticks, greedy.taps],
      columns: crates(built.level.columns),
    },
  };
}

function genCasesFixture(levelFiles: Record<string, string>) {
  return {
    ...header(
      "generator-cases",
      "generateLevel / solveGreedy / randomWinRate / buildLevel on cases the built-in levels do not reach: hand-set options, " +
        "large seeds, the fallback and unverified paths. tried = every tray buildLevel generates, in order (phase candidate: " +
        "seed + 7919k; fallback: seed + k, shuffle * (1 - k/8); unverified: shuffle 0); result = what buildLevel returns, " +
        "result.tried = its index in tried; greedy = [solved, ticks, taps]; games[g] = randomWinRate(level, 1, seed + 101g).",
    ),
    cases: GEN_CASES.map((c) => {
      const pic =
        typeof c.picture === "string"
          ? (JSON.parse(levelFiles[c.picture]) as LevelDefinition).picture
          : c.picture;
      const picture: Picture = { name: c.name, width: pic.width, height: pic.height, cells: Int16Array.from(pic.cells), palette: [...pic.palette] };
      const g = c.generation;
      const opts: BuildOptions = { ...g, edges: c.edges, slots: c.slotCount };
      delete (opts as { candidates?: number }).candidates;
      const generated = generateLevel(picture, opts);
      return {
        name: c.name,
        description: c.description,
        ...(typeof c.picture === "string" ? { pictureFrom: c.picture } : { picture: c.picture }),
        edges: c.edges,
        slotCount: c.slotCount,
        generation: g,
        generate: { columnsSha256: columnsSha256(generated.columns), columns: crates(generated.columns) },
        build: traceBuild(picture, opts, g.candidates ?? 16),
      };
    }),
  };
}

// ---------------------------------------------------------------- scenarios

interface Scenario {
  name: string;
  description: string;
  /** Inline static level, or the path of a level file (repo-relative). */
  level: LevelDefinition | string;
  /** Snapshots also list the reachable cells of each color (small boards only). */
  detail: boolean;
  play: (r: Recorder) => void;
}

const SMALL = (levelId: string, rows: string[], palette: string[], edges: LevelDefinition["edges"], columns: [number, number][][], slotCount: number): LevelDefinition => {
  const width = rows[0].length;
  const cells = rows.flatMap((r) => [...r].map((ch) => (ch === "." ? -1 : Number(ch))));
  let id = 0;
  return {
    schemaVersion: 1,
    levelId,
    themeId: "default",
    name: levelId,
    picture: { width, height: rows.length, palette, cells },
    edges,
    slotCount,
    tray: { columns: columns.map((col) => col.map(([color, count]) => ({ id: id++, color, count }))) },
  };
};

/** Taps the first column whose front crate can eat now or will be uncovered; else, if nothing moves, the first non-empty. */
function simplePolicy(r: Recorder, maxRounds = 20_000): void {
  const g = r.game;
  for (let round = 0; g.status !== "won" && round < maxRounds; round++) {
    if (g.status === "stuck") {
      if (g.slots.length >= 6) return;
      r.addSlot();
      continue;
    }
    while (g.hasFreeSlot()) {
      const col = g.columns.findIndex((c) => c.length > 0 && g.board.reachableCount(c[0].color) > 0);
      if (col < 0) break;
      r.tap(col);
    }
    const sent = r.step();
    if (sent.length === 0 && g.status === "playing" && g.hasFreeSlot()) {
      const col = g.columns.findIndex((c) => c.length > 0);
      if (col >= 0) r.tap(col);
    }
  }
}

/** Seeded random play with trucks collecting out of order: random taps, ticks with random slots held back, collects. */
function randomDelayed(seed: number, ops: number) {
  return (r: Recorder) => {
    const g = r.game;
    const rng = mulberry32(seed);
    const out: number[] = [];
    for (let k = 0; k < ops && g.status !== "won"; k++) {
      const x = rng();
      if (g.status === "stuck") {
        if (g.slots.length >= 6) break;
        r.addSlot();
      } else if (x < 0.25 && g.hasFreeSlot()) {
        const cols = g.columns.flatMap((c, i) => (c.length ? [i] : []));
        if (cols.length) r.tap(cols[Math.floor(rng() * cols.length)]);
      } else if (x < 0.6) {
        const ready = g.slots.map(() => rng() < 0.7);
        for (const [, cell] of r.tick(ready)) out.push(cell);
      } else if (out.length) {
        r.collect(out.splice(Math.floor(rng() * out.length), 1)[0]);
      } else {
        for (const [, cell] of r.tick()) out.push(cell);
      }
    }
    while (out.length) r.collect(out.shift()!);
  };
}

export const SCENARIOS: Scenario[] = [
  {
    name: "ring-step",
    description: "3x3 ring open on all sides: tap both crates, then step until won.",
    level: SMALL("ring", ["000", "010", "000"], ["#ff0000", "#0000ff"], "all", [[[0, 8]], [[1, 1]]], 5),
    detail: true,
    play: (r) => {
      r.tap(0);
      r.tap(1);
      while (r.game.status === "playing") r.step();
    },
  },
  {
    name: "claim-blocks-until-collect",
    description: "One column open at the bottom: a claimed cube keeps the next one closed until it is collected; not stuck while a truck is out.",
    level: SMALL("column", ["0", "0", "0"], ["#000000"], "bottom", [[[0, 3]]], 1),
    detail: true,
    play: (r) => {
      r.tap(0);
      r.tick();
      r.tick();
      r.tick();
      r.collect(2);
      r.tick();
      r.collect(1);
      r.tick();
      r.collect(0);
      r.tick();
    },
  },
  {
    name: "stuck-addslot-won",
    description: "A blocked crate fills the only slot (stuck); tap and tick do nothing; +1 Slot, then the level is won.",
    level: SMALL("stack", ["1", "0"], ["#ff0000", "#00ff00"], "bottom", [[[1, 1], [0, 1]]], 1),
    detail: true,
    play: (r) => {
      r.tap(0);
      r.tick();
      r.tap(0);
      r.addSlot();
      r.tap(0);
      r.step();
      r.step();
      r.tick();
    },
  },
  {
    name: "sealed-gap-opensup",
    description: "Blue under a sealed gap under red under green: slotBlocked follows opensUp while a truck is out.",
    level: SMALL("sealed-gap", ["2", "1", ".", "0"], ["#0000ff", "#ff0000", "#00ff00"], "bottom", [[[1, 1]], [[0, 1]], [[2, 1]]], 3),
    detail: true,
    play: (r) => {
      r.tap(0);
      r.tap(2);
      r.tap(1);
      r.tick();
      r.collect(3);
      r.tick();
      r.collect(1);
      r.tick();
      r.collect(0);
    },
  },
  {
    name: "ready-mask-middle-out",
    description: "5x2 open at the bottom, one crate of 10: slots held back, a row taken middle-out, pickups opening the row above.",
    level: SMALL("rows", ["00000", "00000"], ["#000000"], "bottom", [[[0, 10]]], 1),
    detail: true,
    play: (r) => {
      r.tap(0);
      r.tick([false]);
      for (let k = 0; k < 6; k++) r.tick([true]);
      r.collect(7);
      r.tick([true]);
      r.tick([true]);
      for (const c of [6, 8, 5, 9, 2]) r.collect(c);
      while (r.game.status === "playing") {
        const sent = r.tick();
        for (const [, cell] of sent) r.collect(cell);
        if (sent.length === 0) break;
      }
    },
  },
  {
    name: "all-edges-top-row",
    description: "Open on all sides: the top row is entered from outside (corners by the side roads first).",
    level: SMALL("top-row", ["000", "111", "111"], ["#ff0000", "#0000ff"], "all", [[[0, 3]], [[1, 6]]], 2),
    detail: true,
    play: simplePolicy,
  },
  {
    name: "sides-edges-wall",
    description: "Open at the bottom and sides behind a bottom row of another color.",
    level: SMALL("wall", ["0000", "0000", "1111"], ["#ff0000", "#0000ff"], "sides", [[[0, 4], [0, 4]], [[1, 4]]], 1),
    detail: true,
    play: simplePolicy,
  },
  {
    name: "strawberry-step",
    description: "Built-in level 1 played with step() (every truck collects at once) by a simple player.",
    level: levelFile(0),
    detail: false,
    play: simplePolicy,
  },
  {
    name: "mushroom-random-delayed",
    description: "Built-in level 2, seeded random play with trucks collecting out of order and slots held back.",
    level: levelFile(1),
    detail: false,
    play: randomDelayed(2024, 500),
  },
  {
    name: "cat-random-delayed",
    description: "Built-in level 5, seeded random play with trucks collecting out of order and slots held back.",
    level: levelFile(4),
    detail: false,
    play: randomDelayed(77, 500),
  },
];

/** Plays a scenario on a fresh Game; `levels` maps level file paths to their text. */
export function runScenario(s: Scenario, levels: Record<string, string>) {
  const def = typeof s.level === "string" ? readLevelDefinition(levels[s.level]).level! : s.level;
  const recorder = new Recorder(new Game(toLevel(def)), s.detail);
  s.play(recorder);
  return {
    ...header("scenario", s.description),
    name: s.name,
    detail: s.detail,
    ...(typeof s.level === "string" ? { levelFile: s.level } : { level: s.level }),
    steps: recorder.steps,
  };
}

// ---------------------------------------------------------------- all files

/** Every golden file: repo-relative path -> exact content. */
export function buildGoldenFiles(): Record<string, string> {
  const files: Record<string, string> = {};
  builtInLevels().forEach((_, i) => {
    const def = builtInDefinition(ref, i, LEVEL_IDS[i]);
    def.$schema = "../../../../LevelFormat/level-definition.v1.schema.json";
    files[levelFile(i)] = formatLevelDefinition(def);
  });
  files[`${GOLDEN_DIR}/rng.json`] = toFile(rngFixture());
  files[`${GOLDEN_DIR}/peel-layers.json`] = toFile(peelFixture());
  files[`${GOLDEN_DIR}/generate.json`] = toFile(generateFixture());
  files[`${GOLDEN_DIR}/build.json`] = toFile(buildFixture());
  files[`${GOLDEN_DIR}/generator-cases.json`] = toFile(genCasesFixture(files));
  for (const s of SCENARIOS) files[`${GOLDEN_DIR}/scenarios/${s.name}.json`] = toFile(runScenario(s, files));
  Object.assign(files, parityFiles(files));
  const manifest = {
    ...header("manifest", "Every golden file with its sha256 and size in bytes (UTF-8, LF line endings). Regenerate with `npm run export:golden` in Tools/ts-reference."),
    files: Object.keys(files)
      .sort()
      .map((path) => ({ path, bytes: Buffer.byteLength(files[path], "utf8"), sha256: sha256(files[path]) })),
  };
  files[`${GOLDEN_DIR}/manifest.json`] = toFile(manifest);
  return files;
}

export { stringify };
