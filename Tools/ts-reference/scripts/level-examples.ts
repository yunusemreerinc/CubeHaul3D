/** The example LevelDefinitions, built from the reference source. Shared by the export script and its test. */
import type { Level, Picture } from "../vendor/cube-hunt/src/core/types";
import type { LevelSpec } from "../vendor/cube-hunt/src/levels/art";
import type { BuildOptions, BuiltLevel } from "../vendor/cube-hunt/src/levels/generator";
import type { SolveResult } from "../vendor/cube-hunt/src/levels/solver";
import { difficultyLabel, type LevelDefinition } from "../../level-format/src/levelDefinition";

export interface Reference {
  builtInLevels: () => LevelSpec[];
  buildLevel: (picture: Picture, opts: BuildOptions) => BuiltLevel;
  solveGreedy: (level: Level) => SolveResult;
}

const SCHEMA = "../level-definition.v1.schema.json";

/** A built-in level as a LevelDefinition: its picture, the tray buildLevel picks, the settings that made it. */
export function builtInDefinition(ref: Reference, index: number, levelId: string): LevelDefinition {
  const spec = ref.builtInLevels()[index];
  const built = ref.buildLevel(spec.picture, spec);
  const greedy = ref.solveGreedy(built.level);
  const generation: LevelDefinition["generation"] = { seed: spec.seed, shuffle: spec.shuffle };
  if (spec.targetWinRate !== undefined) generation.targetWinRate = spec.targetWinRate;
  generation.columns = spec.columns ?? 5;
  generation.bigChance = spec.bigChance ?? 0.15;
  if (spec.minBlock !== undefined) generation.minBlock = spec.minBlock;
  if (spec.maxBlock !== undefined) generation.maxBlock = spec.maxBlock;
  generation.candidates = 16;
  return {
    $schema: SCHEMA,
    schemaVersion: 1,
    levelId,
    themeId: "default",
    name: spec.picture.name,
    picture: { width: spec.picture.width, height: spec.picture.height, palette: [...spec.picture.palette], cells: [...spec.picture.cells] },
    edges: built.level.edges,
    slotCount: built.level.slotCount,
    tray: { columns: built.level.columns.map((col) => col.map((b) => ({ id: b.id, color: b.color, count: b.count }))) },
    generation,
    difficultyMetadata: {
      winRate: built.winRate,
      label: difficultyLabel(built.winRate),
      randomGames: 24,
      greedy: { solved: greedy.solved, ticks: greedy.ticks, taps: greedy.taps },
    },
  };
}

export function examples(ref: Reference): Record<string, LevelDefinition> {
  return {
    // A complete built-in level: static tray, the generation settings that produced it, and its difficulty.
    "strawberry.level.json": builtInDefinition(ref, 0, "strawberry"),
    // The smallest playable level: a static tray only (the 3x3 "ring" from cube-hunt's tests).
    "ring.static.level.json": {
      $schema: SCHEMA,
      schemaVersion: 1,
      levelId: "ring",
      themeId: "default",
      name: "Ring",
      picture: { width: 3, height: 3, palette: ["#ff0000", "#0000ff"], cells: [0, 0, 0, 0, 1, 0, 0, 0, 0] },
      edges: "all",
      slotCount: 5,
      tray: { columns: [[{ id: 0, color: 0, count: 8 }], [{ id: 1, color: 1, count: 1 }]] },
    },
    // Generation settings only: the tray is built from the seed (at bake time or at runtime).
    "heart.generated.level.json": {
      $schema: SCHEMA,
      schemaVersion: 1,
      levelId: "heart",
      themeId: "default",
      name: "Little Heart",
      picture: {
        width: 7,
        height: 6,
        palette: ["#e8424f", "#ffffff"],
        // prettier-ignore
        cells: [
          -1,  0,  0, -1,  0,  0, -1,
           0,  0,  1,  0,  0,  0,  0,
           0,  0,  0,  0,  0,  0,  0,
          -1,  0,  0,  0,  0,  0, -1,
          -1, -1,  0,  0,  0, -1, -1,
          -1, -1, -1,  0, -1, -1, -1,
        ],
      },
      edges: "bottom",
      slotCount: 5,
      generation: { seed: 7, shuffle: 0.5, targetWinRate: 0.8 },
    },
  };
}
