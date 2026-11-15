/**
 * LevelDefinition JSON against the cube-hunt rules: the example files are what the reference source produces,
 * every built-in level converts, and a level read from JSON plays exactly like the original.
 */
import { Game } from "@ref/core/game";
import type { Level, Picture } from "@ref/core/types";
import { builtInLevels } from "@ref/levels/art";
import { buildLevel } from "@ref/levels/generator";
import { solveGreedy } from "@ref/levels/solver";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { formatLevelDefinition } from "../../level-format/src/format";
import { GENERATION_DEFAULTS, type LevelDefinition } from "../../level-format/src/levelDefinition";
import { readLevelDefinition, validateLevelDefinition } from "../../level-format/src/validate";
import { builtInDefinition, examples } from "../scripts/level-examples";

const EXAMPLES = join(__dirname, "../../../LevelFormat/examples");
const ref = { builtInLevels, buildLevel, solveGreedy };
const specs = builtInLevels();

const pictureOf = (d: LevelDefinition): Picture => ({
  name: d.name,
  width: d.picture.width,
  height: d.picture.height,
  cells: Int16Array.from(d.picture.cells),
  palette: d.picture.palette,
});

/** A static LevelDefinition as a cube-hunt Level. */
function toLevel(d: LevelDefinition): Level {
  return {
    picture: pictureOf(d),
    columns: d.tray!.columns.map((col) => col.map((k) => ({ ...k }))),
    slotCount: d.slotCount,
    edges: d.edges,
  };
}

describe("example files", () => {
  it.each(Object.entries(examples(ref)))("%s matches what the reference source produces", (file, level) => {
    expect(readFileSync(join(EXAMPLES, file), "utf8")).toBe(formatLevelDefinition(level));
  });
});

describe("built-in levels as LevelDefinition", () => {
  it.each(specs.map((s, i) => [s.picture.name, i] as const))("%s: valid, and plays exactly like the original", (_name, i) => {
    const def = builtInDefinition(ref, i, `level-${i + 1}`);
    const text = formatLevelDefinition(def);
    const result = readLevelDefinition(text);
    expect(result.errors).toEqual([]);
    expect(result.warnings).toEqual([]);

    const original = buildLevel(specs[i].picture, specs[i]).level;
    const fromJson = toLevel(result.level!);
    expect(fromJson.columns).toEqual(original.columns);
    expect([...fromJson.picture.cells]).toEqual([...original.picture.cells]);
    expect(solveGreedy(fromJson)).toEqual(solveGreedy(original));

    // Same moves, same game: replay a fixed tap order on both and compare every dispatch.
    const a = new Game(original);
    const b = new Game(fromJson);
    for (let guard = 0; a.status === "playing" && guard < 5000; guard++) {
      const col = a.columns.findIndex((c) => c.length > 0 && a.board.reachableCount(c[0].color) > 0);
      if (col >= 0 && a.hasFreeSlot()) expect(b.tap(col)).toBe(a.tap(col));
      expect(b.step()).toEqual(a.step());
      const status: string = a.status; // step() may have changed it; TypeScript cannot see that
      if (status === "stuck") {
        a.addSlot();
        b.addSlot();
      }
    }
    expect(b.status).toBe(a.status);
  });
});

describe("generation-only levels", () => {
  it("heart.generated: the tray built from its settings is a valid static level", () => {
    const def = readLevelDefinition(readFileSync(join(EXAMPLES, "heart.generated.level.json"), "utf8")).level!;
    expect(def.tray).toBeUndefined();
    const g = def.generation!;
    const built = buildLevel(pictureOf(def), {
      seed: g.seed,
      shuffle: g.shuffle,
      targetWinRate: g.targetWinRate ?? GENERATION_DEFAULTS.targetWinRate,
      columns: g.columns ?? GENERATION_DEFAULTS.columns,
      bigChance: g.bigChance ?? GENERATION_DEFAULTS.bigChance,
      slots: def.slotCount,
      edges: def.edges,
    });
    const baked: LevelDefinition = { ...def, tray: { columns: built.level.columns } };
    const result = validateLevelDefinition(JSON.parse(formatLevelDefinition(baked)));
    expect(result.errors).toEqual([]);
    expect(solveGreedy(toLevel(result.level!)).solved).toBe(true);
  });
});

describe("the validator catches a tray that does not match the rules", () => {
  it("one crate short in a real level: TRAY_TOTAL_MISMATCH for that color", () => {
    const def = builtInDefinition(ref, 2, "sunset");
    const crate = def.tray!.columns[0].shift()!;
    const result = validateLevelDefinition(JSON.parse(formatLevelDefinition(def)));
    expect(result.errors.map((e) => `${e.code} ${e.path}`)).toEqual([`TRAY_TOTAL_MISMATCH /picture/palette/${crate.color}`]);
  });
});
