/**
 * Golden fixtures (docs/GOLDEN_FIXTURES.md): the committed files are exactly what the exporter produces, the
 * exporter is deterministic, and every file agrees with the reference rules it was made from.
 */
import { Game } from "@ref/core/game";
import { builtInLevels } from "@ref/levels/art";
import { buildLevel } from "@ref/levels/generator";
import { mulberry32 } from "@ref/levels/rng";
import { existsSync, readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { beforeAll, describe, expect, it } from "vitest";
import { readLevelDefinition, validateLevelDefinition } from "../../level-format/src/validate";
import { buildGoldenFiles, GOLDEN_DIR, LEVEL_IDS, levelFile, LEVELS_DIR, toLevel } from "../scripts/golden/golden";
import { columnsSha256, fnv1a, sha256 } from "../scripts/golden/hash";
import { snapshot, type Step } from "../scripts/golden/snapshot";
import { violations } from "./helpers";

const REPO = join(__dirname, "../../..");
const disk = (path: string) => readFileSync(join(REPO, path), "utf8");
const json = (path: string) => JSON.parse(disk(path));
const specs = builtInLevels();

let generated: Record<string, string>;
beforeAll(() => {
  generated = buildGoldenFiles();
}, 120_000);

describe("regeneration", () => {
  it("the committed files are byte for byte what the exporter produces", () => {
    for (const [path, content] of Object.entries(generated)) {
      expect(existsSync(join(REPO, path)), path).toBe(true);
      expect(disk(path), path).toBe(content);
    }
  });

  it("there are no stale files the exporter no longer produces", () => {
    const list = (dir: string): string[] =>
      readdirSync(join(REPO, dir), { withFileTypes: true }).flatMap((e) =>
        e.isDirectory() ? list(`${dir}/${e.name}`) : e.name.endsWith(".json") ? [`${dir}/${e.name}`] : [],
      );
    expect([...list(GOLDEN_DIR), ...list(LEVELS_DIR)].sort()).toEqual(Object.keys(generated).sort());
  });

  it("the exporter is deterministic: a second run gives identical files", () => {
    expect(buildGoldenFiles()).toEqual(generated);
  }, 120_000);

  it("the manifest lists every other file with its size and sha256", () => {
    const manifest = json(`${GOLDEN_DIR}/manifest.json`);
    const others = Object.keys(generated).filter((p) => !p.endsWith("manifest.json")).sort();
    expect(manifest.files.map((f: { path: string }) => f.path)).toEqual(others);
    for (const f of manifest.files) {
      expect(f.sha256).toBe(sha256(disk(f.path)));
      expect(f.bytes).toBe(Buffer.byteLength(disk(f.path), "utf8"));
    }
  });
});

describe("level files", () => {
  it.each(LEVEL_IDS.map((id, i) => [id, i] as const))("%s: valid, and the picture, palette and tray of the built-in level", (id, i) => {
    const result = readLevelDefinition(disk(levelFile(i)));
    expect(result.errors).toEqual([]);
    expect(result.warnings).toEqual([]);
    const def = result.level!;
    const spec = specs[i];
    const built = buildLevel(spec.picture, spec);
    expect(def.levelId).toBe(id);
    expect(def.name).toBe(spec.picture.name);
    expect([def.picture.width, def.picture.height]).toEqual([spec.picture.width, spec.picture.height]);
    expect(def.picture.cells).toEqual([...spec.picture.cells]);
    expect(def.picture.palette).toEqual(spec.picture.palette);
    expect(def.tray!.columns).toEqual(built.level.columns);
    expect(def.edges).toBe(built.level.edges);
    expect(def.slotCount).toBe(built.level.slotCount);
    expect(def.generation!.seed).toBe(spec.seed);
    expect(def.difficultyMetadata!.winRate).toBe(built.winRate);
  });
});

describe("generator fixtures", () => {
  it("rng.json holds mulberry32's output as uint32", () => {
    const rng = json(`${GOLDEN_DIR}/rng.json`);
    for (const { seed, values } of rng.seeds) {
      const r = mulberry32(seed);
      expect(values).toEqual(values.map(() => r() * 2 ** 32));
    }
    const minusOne = mulberry32(-1);
    expect(rng.seeds.find((s: { seed: number }) => s.seed === 4294967295).values.slice(0, 8)).toEqual(
      Array.from({ length: 8 }, () => minusOne() * 2 ** 32),
    );
  });

  it("build.json: the chosen candidate is the level file's tray, and its games average to the win rate", () => {
    const build = json(`${GOLDEN_DIR}/build.json`);
    build.levels.forEach((lv: { file: string; candidates: { k: number; seed: number; columnsSha256: string; games?: number[]; winRate?: number }[]; chosen: number; winRate: number; options: { seed: number } }, i: number) => {
      const def = readLevelDefinition(disk(lv.file)).level!;
      expect(lv.file).toBe(levelFile(i));
      const chosen = lv.candidates[lv.chosen];
      expect(chosen.columnsSha256).toBe(columnsSha256(def.tray!.columns));
      expect(chosen.winRate).toBe(lv.winRate);
      expect(lv.winRate).toBe(def.difficultyMetadata!.winRate);
      for (const c of lv.candidates) {
        expect(c.seed).toBe(lv.options.seed + c.k * 7919);
        if (c.games) expect(c.games.reduce((a, b) => a + b, 0) / c.games.length).toBe(c.winRate);
      }
    });
  });
});

describe("scenarios replay exactly from their recorded operations", () => {
  const dir = `${GOLDEN_DIR}/scenarios`;
  const names = readdirSync(join(REPO, dir)).filter((f) => f.endsWith(".json")).sort();

  it("there are scenarios for step, tick + collect, stuck + addSlot and every edge mode", () => {
    const all = names.map((n) => json(`${dir}/${n}`));
    const ops = new Set(all.flatMap((s) => s.steps.map((x: Step) => x.op)));
    expect([...ops].sort()).toEqual(["addSlot", "collect", "start", "step", "tap", "tick"]);
    const edges = new Set(all.map((s) => (s.level ?? readLevelDefinition(disk(s.levelFile)).level!).edges));
    expect([...edges].sort()).toEqual(["all", "bottom", "sides"]);
    const ends = new Set(all.map((s) => s.steps.at(-1).state.status));
    expect([...ends].sort()).toEqual(["playing", "stuck", "won"]);
    expect(names.length).toBeGreaterThanOrEqual(8);
  });

  it.each(names)("%s", (name) => {
    const s = json(`${dir}/${name}`);
    const def = s.level ? validateLevelDefinition(s.level).level! : readLevelDefinition(disk(s.levelFile)).level!;
    expect(def).toBeDefined();
    const g = new Game(toLevel(def));
    let cells: Int16Array | null = null;
    for (const step of s.steps as Step[]) {
      switch (step.op) {
        case "tap":
          expect(g.tap(step.col)).toBe(step.result);
          break;
        case "tick": {
          const ready = step.ready;
          const sent = ready ? g.tick((k) => ready[k] === true) : g.tick();
          expect(sent.map((d) => [d.slot, d.cell, d.color])).toEqual(step.result);
          break;
        }
        case "step":
          expect(g.step().map((d) => [d.slot, d.cell, d.color])).toEqual(step.result);
          break;
        case "collect":
          g.collect(step.cell);
          break;
        case "addSlot":
          g.addSlot();
          break;
      }
      expect(snapshot(g, cells, s.detail)).toEqual(step.state);
      expect(violations(g)).toEqual([]);
      cells = Int16Array.from(g.board.cells);
    }
  });

  it("boardHash of a cleared board is fnv1a(-1 x n, 1 x n, 0 x n)", () => {
    const s = json(`${dir}/ring-step.json`);
    const last = s.steps.at(-1).state;
    expect(last.status).toBe("won");
    expect(last.boardHash).toBe(fnv1a([...Array(9).fill(-1), ...Array(9).fill(1), ...Array(9).fill(0)]));
  });
});
