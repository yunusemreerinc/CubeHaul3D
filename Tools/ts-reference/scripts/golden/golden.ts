/**
 * Builds every golden file from the cube-hunt reference source: the 10 built-in levels as LevelDefinition JSON,
 * generator fixtures (RNG, peel layers, generateLevel, the full buildLevel trace) and step-by-step game
 * snapshots. Pure: the same source always gives byte-identical files (checked by tests/golden.test.ts).
 * See docs/GOLDEN_FIXTURES.md.
 */
import { Game } from "../../vendor/cube-hunt/src/core/game";
import type { Level, Picture } from "../../vendor/cube-hunt/src/core/types";
import { builtInLevels } from "../../vendor/cube-hunt/src/levels/art";
import { buildLevel, generateLevel, peelLayers } from "../../vendor/cube-hunt/src/levels/generator";
import { mulberry32 } from "../../vendor/cube-hunt/src/levels/rng";
import { randomWinRate, solveGreedy } from "../../vendor/cube-hunt/src/levels/solver";
import { formatLevelDefinition } from "../../../level-format/src/format";
import type { LevelDefinition } from "../../../level-format/src/levelDefinition";
import { readLevelDefinition } from "../../../level-format/src/validate";
import { builtInDefinition } from "../level-examples";
import { columnsSha256, sha256 } from "./hash";
import { stringify, toFile } from "./json";
import { Recorder } from "./snapshot";

export const SOURCE = {
  repo: "https://github.com/yunusemreerinc/cube-hunt",
  commit: "559dfd90977b1e15367bd7de25aec9fbae4b52f6",
};
export const LEVELS_DIR = "Assets/CubeHaul/Content/Levels";
export const GOLDEN_DIR = "Assets/Tests/Fixtures/Golden";

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
const header = (kind: string, description: string) => ({ format: `cubehaul.golden.${kind}`, version: 1, source: SOURCE, description });

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
  for (const s of SCENARIOS) files[`${GOLDEN_DIR}/scenarios/${s.name}.json`] = toFile(runScenario(s, files));
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
