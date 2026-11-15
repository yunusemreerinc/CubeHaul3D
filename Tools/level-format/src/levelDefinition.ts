/**
 * CubeHaul3D LevelDefinition, schema version 1. See docs/LEVEL_FORMAT.md.
 *
 * Everything a level needs, in a form both the TypeScript tools and the C# game read:
 * a picture (row-major cell colors, EMPTY = -1), the board rules (edges, slotCount),
 * and either a static tray to play as-is, generation settings to build one, or both.
 */

export const SCHEMA_VERSION = 1;
export const EMPTY = -1;

export type Edges = "bottom" | "sides" | "all";
export type DifficultyLabel = "Easy" | "Medium" | "Hard" | "Expert";

export interface PictureDefinition {
  width: number;
  height: number;
  /** Row-major, length width * height; row 0 is the top (farthest from the ramp). EMPTY (-1) or a palette index. */
  cells: number[];
  /** "#rrggbb" colors, indexed by the values in `cells` and by crate colors. */
  palette: string[];
}

export interface CrateDefinition {
  id: number;
  /** Palette index. */
  color: number;
  /** Trucks in the crate: each hauls away one cube. */
  count: number;
}

/** The exact tray to play. columns[c][0] is the front (tappable) crate of column c. */
export interface TrayDefinition {
  columns: CrateDefinition[][];
}

/** How to generate a tray (cube-hunt's BuildOptions). Omitted optional fields use the defaults below. */
export interface GenerationSettings {
  seed: number;
  shuffle: number;
  targetWinRate?: number;
  columns?: number;
  bigChance?: number;
  minBlock?: number;
  maxBlock?: number;
  candidates?: number;
}

export const GENERATION_DEFAULTS = { targetWinRate: 1, columns: 5, bigChance: 0.15, candidates: 16 } as const;

export interface DifficultyMetadata {
  /** Random-player win rate of the static tray, 0..1. */
  winRate: number;
  label: DifficultyLabel;
  /** How many random games winRate was measured over (cube-hunt uses 24). */
  randomGames?: number;
  greedy?: { solved: boolean; ticks: number; taps: number };
}

export interface LevelDefinition {
  $schema?: string;
  schemaVersion: 1;
  levelId: string;
  themeId: string;
  name: string;
  picture: PictureDefinition;
  edges: Edges;
  slotCount: number;
  tray?: TrayDefinition;
  generation?: GenerationSettings;
  difficultyMetadata?: DifficultyMetadata;
  /** Free-form data for tools; the game ignores it. */
  extensions?: Record<string, unknown>;
}

/** cube-hunt's difficulty chip (main.ts): >= 0.9 Easy, >= 0.65 Medium, >= 0.45 Hard, else Expert. */
export function difficultyLabel(winRate: number): DifficultyLabel {
  if (winRate >= 0.9) return "Easy";
  if (winRate >= 0.65) return "Medium";
  if (winRate >= 0.45) return "Hard";
  return "Expert";
}
