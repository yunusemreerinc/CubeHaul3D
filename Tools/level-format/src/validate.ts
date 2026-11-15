/**
 * Reader and validator for LevelDefinition v1. The C# twin is
 * Assets/CubeHaul/Scripts/Data/LevelValidator.cs: both apply the same rules in the same order and report the
 * same issue codes at the same JSON Pointer paths, which the shared fixtures in LevelFormat/fixtures check.
 * The rules are listed in docs/LEVEL_FORMAT.md §5.
 */
import {
  difficultyLabel,
  GENERATION_DEFAULTS,
  SCHEMA_VERSION,
  type LevelDefinition,
} from "./levelDefinition";

export type IssueCode =
  // structure
  | "JSON_SYNTAX"
  | "WRONG_TYPE"
  | "MISSING_FIELD"
  | "UNKNOWN_FIELD"
  | "UNSUPPORTED_SCHEMA_VERSION"
  | "INVALID_VALUE"
  | "OUT_OF_RANGE"
  // picture
  | "CELLS_LENGTH"
  | "CELL_COLOR"
  | "EMPTY_PICTURE"
  // tray
  | "TRAY_EMPTY"
  | "DUPLICATE_CRATE_ID"
  | "CRATE_COLOR"
  | "TRAY_TOTAL_MISMATCH"
  | "COLUMN_COUNT_MISMATCH"
  // level as a whole
  | "NO_TRAY_OR_GENERATION"
  | "DIFFICULTY_WITHOUT_TRAY"
  | "DIFFICULTY_LABEL_MISMATCH"
  // warnings
  | "UNUSED_PALETTE_COLOR"
  | "DUPLICATE_PALETTE_COLOR"
  | "BLOCK_SIZE_OVERRIDE";

export interface Issue {
  code: IssueCode;
  /** JSON Pointer (RFC 6901) to the offending value; "" is the whole document. */
  path: string;
  message: string;
}

export interface ReadResult {
  ok: boolean;
  /** Set only when there are no errors. */
  level?: LevelDefinition;
  errors: Issue[];
  warnings: Issue[];
}

export const LIMITS = {
  side: [1, 64],
  palette: [1, 32],
  slotCount: [1, 6],
  columns: [1, 10],
  crateId: [0, 999_999],
  crateCount: [1, 9_999],
  seed: [0, 2_147_483_647],
  shuffle: [0, 100],
  blockSize: [1, 9_999],
  candidates: [1, 1_000],
  randomGames: [1, 10_000],
  nameLength: 64,
} as const;

const ID = /^[a-z0-9][a-z0-9_-]{0,63}$/;
const HEX = /^#[0-9a-fA-F]{6}$/;
const EDGES = ["bottom", "sides", "all"];
const LABELS = ["Easy", "Medium", "Hard", "Expert"];

const ROOT_KEYS = [
  "$schema",
  "schemaVersion",
  "levelId",
  "themeId",
  "name",
  "picture",
  "edges",
  "slotCount",
  "tray",
  "generation",
  "difficultyMetadata",
  "extensions",
];
const ROOT_REQUIRED = ["levelId", "themeId", "name", "picture", "edges", "slotCount"];

type Json = unknown;
type Obj = Record<string, Json>;

/** Escapes one JSON Pointer token. */
const token = (key: string | number) => String(key).replace(/~/g, "~0").replace(/\//g, "~1");
const at = (path: string, key: string | number) => `${path}/${token(key)}`;
const isObject = (v: Json): v is Obj => typeof v === "object" && v !== null && !Array.isArray(v);
const has = (o: Obj, k: string) => Object.prototype.hasOwnProperty.call(o, k);
const typeName = (v: Json) => (v === null ? "null" : Array.isArray(v) ? "array" : typeof v);

class Checker {
  readonly errors: Issue[] = [];
  readonly warnings: Issue[] = [];

  error(code: IssueCode, path: string, message: string): void {
    this.errors.push({ code, path, message });
  }

  warn(code: IssueCode, path: string, message: string): void {
    this.warnings.push({ code, path, message });
  }

  /** An object with exactly the allowed keys and all required ones. False if `v` is not an object. */
  object(v: Json, path: string, allowed: readonly string[], required: readonly string[]): v is Obj {
    if (!isObject(v)) {
      this.error("WRONG_TYPE", path, `expected an object, got ${typeName(v)}`);
      return false;
    }
    for (const k of Object.keys(v)) if (!allowed.includes(k)) this.error("UNKNOWN_FIELD", at(path, k), `unknown field "${k}"`);
    for (const k of required) if (!has(v, k)) this.error("MISSING_FIELD", at(path, k), `missing required field "${k}"`);
    return true;
  }

  integer(v: Json, path: string, min: number, max: number): number | undefined {
    if (typeof v !== "number" || !Number.isInteger(v)) {
      this.error("WRONG_TYPE", path, `expected an integer, got ${typeName(v) === "number" ? String(v) : typeName(v)}`);
      return undefined;
    }
    if (v < min || v > max) {
      this.error("OUT_OF_RANGE", path, `${v} is outside ${min}..${max}`);
      return undefined;
    }
    return v;
  }

  number(v: Json, path: string, min: number, max: number): number | undefined {
    if (typeof v !== "number") {
      this.error("WRONG_TYPE", path, `expected a number, got ${typeName(v)}`);
      return undefined;
    }
    if (!(v >= min && v <= max)) {
      this.error("OUT_OF_RANGE", path, `${v} is outside ${min}..${max}`);
      return undefined;
    }
    return v;
  }

  string(v: Json, path: string): string | undefined {
    if (typeof v !== "string") {
      this.error("WRONG_TYPE", path, `expected a string, got ${typeName(v)}`);
      return undefined;
    }
    return v;
  }

  boolean(v: Json, path: string): boolean | undefined {
    if (typeof v !== "boolean") {
      this.error("WRONG_TYPE", path, `expected a boolean, got ${typeName(v)}`);
      return undefined;
    }
    return v;
  }

  array(v: Json, path: string): Json[] | undefined {
    if (!Array.isArray(v)) {
      this.error("WRONG_TYPE", path, `expected an array, got ${typeName(v)}`);
      return undefined;
    }
    return v;
  }

  /** An array whose length is within min..max (OUT_OF_RANGE on the array otherwise). */
  arrayOfLength(v: Json, path: string, min: number, max: number): Json[] | undefined {
    const a = this.array(v, path);
    if (!a) return undefined;
    if (a.length < min || a.length > max) {
      this.error("OUT_OF_RANGE", path, `has ${a.length} items, expected ${min}..${max}`);
      return undefined;
    }
    return a;
  }

  /** Number of errors so far, to tell whether a section added any. */
  get count(): number {
    return this.errors.length;
  }
}

interface PictureInfo {
  width: number;
  height: number;
  palette: string[];
  cells: number[];
}

function checkPicture(c: Checker, v: Json): { picture?: PictureInfo; palette?: string[] } {
  const path = "/picture";
  const before = c.count;
  if (!c.object(v, path, ["width", "height", "cells", "palette"], ["width", "height", "cells", "palette"])) return {};
  const width = has(v, "width") ? c.integer(v.width, at(path, "width"), ...LIMITS.side) : undefined;
  const height = has(v, "height") ? c.integer(v.height, at(path, "height"), ...LIMITS.side) : undefined;

  let palette: string[] | undefined;
  if (has(v, "palette")) {
    const raw = c.arrayOfLength(v.palette, at(path, "palette"), ...LIMITS.palette);
    if (raw) {
      const paletteBefore = c.count;
      raw.forEach((p, i) => {
        const s = c.string(p, at(at(path, "palette"), i));
        if (s !== undefined && !HEX.test(s)) c.error("INVALID_VALUE", at(at(path, "palette"), i), `"${s}" is not a #rrggbb color`);
      });
      if (c.count === paletteBefore) palette = raw as string[];
    }
  }

  let cells: number[] | undefined;
  if (has(v, "cells")) {
    const raw = c.array(v.cells, at(path, "cells"));
    if (raw) {
      const cellsBefore = c.count;
      raw.forEach((x, i) => {
        if (typeof x !== "number" || !Number.isInteger(x)) {
          c.error("WRONG_TYPE", at(at(path, "cells"), i), `expected an integer, got ${typeName(x) === "number" ? String(x) : typeName(x)}`);
        }
      });
      if (c.count === cellsBefore) cells = raw as number[];
    }
  }

  if (c.count !== before || width === undefined || height === undefined || !palette || !cells) return { palette };
  return { picture: { width, height, palette, cells }, palette };
}

/** S1: the picture's cells against its size and palette. Returns true if the picture is playable. */
function checkPictureRules(c: Checker, p: PictureInfo): boolean {
  const path = "/picture";
  if (p.cells.length !== p.width * p.height) {
    c.error("CELLS_LENGTH", at(path, "cells"), `has ${p.cells.length} cells, expected width * height = ${p.width * p.height}`);
    return false;
  }
  let bad = false;
  p.cells.forEach((x, i) => {
    if (x < -1 || x >= p.palette.length) {
      c.error("CELL_COLOR", at(at(path, "cells"), i), `${x} is neither EMPTY (-1) nor a palette index 0..${p.palette.length - 1}`);
      bad = true;
    }
  });
  if (bad) return false;
  const used = new Array<number>(p.palette.length).fill(0);
  for (const x of p.cells) if (x >= 0) used[x]++;
  if (used.every((n) => n === 0)) {
    c.error("EMPTY_PICTURE", at(path, "cells"), "the picture has no cubes");
    return false;
  }
  used.forEach((n, k) => {
    if (n === 0) c.warn("UNUSED_PALETTE_COLOR", at(at(path, "palette"), k), `palette color ${k} is not used by any cell`);
  });
  const seen = new Map<string, number>();
  p.palette.forEach((hex, k) => {
    const key = hex.toLowerCase();
    if (seen.has(key)) c.warn("DUPLICATE_PALETTE_COLOR", at(at(path, "palette"), k), `same color as palette entry ${seen.get(key)}`);
    else seen.set(key, k);
  });
  return true;
}

interface CrateInfo {
  id: number;
  color: number;
  count: number;
  path: string;
}

function checkTray(c: Checker, v: Json): CrateInfo[][] | undefined {
  const path = "/tray";
  const before = c.count;
  if (!c.object(v, path, ["columns"], ["columns"])) return undefined;
  if (!has(v, "columns")) return undefined;
  const cols = c.arrayOfLength(v.columns, at(path, "columns"), ...LIMITS.columns);
  if (!cols) return undefined;
  const out: CrateInfo[][] = [];
  cols.forEach((col, ci) => {
    const colPath = at(at(path, "columns"), ci);
    const crates = c.array(col, colPath);
    const list: CrateInfo[] = [];
    out.push(list);
    crates?.forEach((k, ki) => {
      const p = at(colPath, ki);
      if (!c.object(k, p, ["id", "color", "count"], ["id", "color", "count"])) return;
      const id = has(k, "id") ? c.integer(k.id, at(p, "id"), ...LIMITS.crateId) : undefined;
      const color = has(k, "color") ? c.integer(k.color, at(p, "color"), 0, Number.MAX_SAFE_INTEGER) : undefined;
      const count = has(k, "count") ? c.integer(k.count, at(p, "count"), ...LIMITS.crateCount) : undefined;
      if (id !== undefined && color !== undefined && count !== undefined) list.push({ id, color, count, path: p });
    });
  });
  return c.count === before ? out : undefined;
}

/** S2: the tray against itself and the picture. */
function checkTrayRules(c: Checker, columns: CrateInfo[][], palette: string[] | undefined, picture: PictureInfo | undefined): void {
  const crates = columns.flat();
  if (crates.length === 0) {
    c.error("TRAY_EMPTY", "/tray/columns", "the tray has no crates");
    return;
  }
  const ids = new Set<number>();
  for (const k of crates) {
    if (ids.has(k.id)) c.error("DUPLICATE_CRATE_ID", at(k.path, "id"), `crate id ${k.id} is used more than once`);
    ids.add(k.id);
  }
  if (!palette) return;
  let badColor = false;
  for (const k of crates) {
    if (k.color >= palette.length) {
      c.error("CRATE_COLOR", at(k.path, "color"), `${k.color} is not a palette index 0..${palette.length - 1}`);
      badColor = true;
    }
  }
  if (badColor || !picture) return;
  for (let color = 0; color < palette.length; color++) {
    const cubes = picture.cells.filter((x) => x === color).length;
    const trucks = crates.filter((k) => k.color === color).reduce((n, k) => n + k.count, 0);
    if (cubes !== trucks) {
      c.error("TRAY_TOTAL_MISMATCH", `/picture/palette/${color}`, `color ${color} has ${cubes} cubes but ${trucks} trucks in the tray`);
    }
  }
}

function checkGeneration(c: Checker, v: Json): { ok: boolean; columns?: number } {
  const path = "/generation";
  const before = c.count;
  const keys = ["seed", "shuffle", "targetWinRate", "columns", "bigChance", "minBlock", "maxBlock", "candidates"];
  if (!c.object(v, path, keys, ["seed", "shuffle"])) return { ok: false };
  if (has(v, "seed")) c.integer(v.seed, at(path, "seed"), ...LIMITS.seed);
  if (has(v, "shuffle")) c.number(v.shuffle, at(path, "shuffle"), ...LIMITS.shuffle);
  if (has(v, "targetWinRate")) c.number(v.targetWinRate, at(path, "targetWinRate"), 0, 1);
  const columns = has(v, "columns") ? c.integer(v.columns, at(path, "columns"), ...LIMITS.columns) : GENERATION_DEFAULTS.columns;
  if (has(v, "bigChance")) c.number(v.bigChance, at(path, "bigChance"), 0, 1);
  for (const k of ["minBlock", "maxBlock"]) {
    if (!has(v, k)) continue;
    if (c.integer(v[k], at(path, k), ...LIMITS.blockSize) !== undefined) {
      c.warn("BLOCK_SIZE_OVERRIDE", at(path, k), `${k} overrides the derived crate sizes and is not always honoured (TS_BASELINE F-4)`);
    }
  }
  if (has(v, "candidates")) c.integer(v.candidates, at(path, "candidates"), ...LIMITS.candidates);
  return { ok: c.count === before, columns };
}

function checkDifficulty(c: Checker, v: Json): void {
  const path = "/difficultyMetadata";
  if (!c.object(v, path, ["winRate", "label", "randomGames", "greedy"], ["winRate", "label"])) return;
  const winRate = has(v, "winRate") ? c.number(v.winRate, at(path, "winRate"), 0, 1) : undefined;
  let label: string | undefined;
  if (has(v, "label")) {
    label = c.string(v.label, at(path, "label"));
    if (label !== undefined && !LABELS.includes(label)) {
      c.error("INVALID_VALUE", at(path, "label"), `"${label}" is not one of ${LABELS.join(", ")}`);
      label = undefined;
    }
  }
  if (has(v, "randomGames")) c.integer(v.randomGames, at(path, "randomGames"), ...LIMITS.randomGames);
  if (has(v, "greedy")) {
    const g = v.greedy;
    const gp = at(path, "greedy");
    if (c.object(g, gp, ["solved", "ticks", "taps"], ["solved", "ticks", "taps"])) {
      if (has(g, "solved")) c.boolean(g.solved, at(gp, "solved"));
      if (has(g, "ticks")) c.integer(g.ticks, at(gp, "ticks"), 0, Number.MAX_SAFE_INTEGER);
      if (has(g, "taps")) c.integer(g.taps, at(gp, "taps"), 0, Number.MAX_SAFE_INTEGER);
    }
  }
  if (winRate !== undefined && label !== undefined && label !== difficultyLabel(winRate)) {
    c.error("DIFFICULTY_LABEL_MISMATCH", at(path, "label"), `win rate ${winRate} is "${difficultyLabel(winRate)}", not "${label}"`);
  }
}

/** Validates an already parsed JSON value. */
export function validateLevelDefinition(root: Json): ReadResult {
  const c = new Checker();
  const done = (): ReadResult => {
    const ok = c.errors.length === 0;
    return { ok, level: ok ? (root as LevelDefinition) : undefined, errors: c.errors, warnings: c.warnings };
  };

  if (!isObject(root)) {
    c.error("WRONG_TYPE", "", `expected an object, got ${typeName(root)}`);
    return done();
  }
  // The version decides the shape of everything else, so nothing more is checked without a known one.
  if (!has(root, "schemaVersion")) {
    c.error("MISSING_FIELD", "/schemaVersion", 'missing required field "schemaVersion"');
    return done();
  }
  const version = root.schemaVersion;
  if (typeof version !== "number" || !Number.isInteger(version)) {
    c.error("WRONG_TYPE", "/schemaVersion", `expected an integer, got ${typeName(version)}`);
    return done();
  }
  if (version !== SCHEMA_VERSION) {
    c.error("UNSUPPORTED_SCHEMA_VERSION", "/schemaVersion", `schema version ${version} is not supported (this reader knows ${SCHEMA_VERSION})`);
    return done();
  }

  c.object(root, "", ROOT_KEYS, ROOT_REQUIRED);
  if (has(root, "$schema")) c.string(root.$schema, "/$schema");
  for (const k of ["levelId", "themeId"]) {
    if (!has(root, k)) continue;
    const s = c.string(root[k], `/${k}`);
    if (s !== undefined && !ID.test(s)) c.error("INVALID_VALUE", `/${k}`, `"${s}" must match ${ID.source}`);
  }
  if (has(root, "name")) {
    const s = c.string(root.name, "/name");
    if (s !== undefined && (s.trim().length === 0 || s.length > LIMITS.nameLength)) {
      c.error("INVALID_VALUE", "/name", `the name must be 1..${LIMITS.nameLength} characters and not blank`);
    }
  }
  if (has(root, "edges")) {
    const s = c.string(root.edges, "/edges");
    if (s !== undefined && !EDGES.includes(s)) c.error("INVALID_VALUE", "/edges", `"${s}" is not one of ${EDGES.join(", ")}`);
  }
  if (has(root, "slotCount")) c.integer(root.slotCount, "/slotCount", ...LIMITS.slotCount);
  if (has(root, "extensions") && !isObject(root.extensions)) {
    c.error("WRONG_TYPE", "/extensions", `expected an object, got ${typeName(root.extensions)}`);
  }

  // Structure of each section, then the rules between them (S1..S5 in docs/LEVEL_FORMAT.md).
  const { picture, palette } = has(root, "picture") ? checkPicture(c, root.picture) : {};
  const tray = has(root, "tray") ? checkTray(c, root.tray) : undefined;
  const generation = has(root, "generation") ? checkGeneration(c, root.generation) : { ok: false };
  if (has(root, "difficultyMetadata")) checkDifficulty(c, root.difficultyMetadata);

  const playable = picture ? checkPictureRules(c, picture) : false; // S1
  if (tray) checkTrayRules(c, tray, palette, playable ? picture : undefined); // S2
  if (tray && generation.ok && generation.columns !== undefined && generation.columns !== tray.length) {
    // S3
    c.error("COLUMN_COUNT_MISMATCH", "/tray/columns", `the tray has ${tray.length} columns but generation.columns is ${generation.columns}`);
  }
  if (has(root, "difficultyMetadata") && !has(root, "tray")) {
    // S4
    c.error("DIFFICULTY_WITHOUT_TRAY", "/difficultyMetadata", "difficultyMetadata describes a static tray, but there is none");
  }
  if (!has(root, "tray") && !has(root, "generation")) {
    // S5
    c.error("NO_TRAY_OR_GENERATION", "", "a level needs a static tray, generation settings, or both");
  }
  return done();
}

/** Parses and validates LevelDefinition JSON text (UTF-8, an optional BOM is ignored). */
export function readLevelDefinition(text: string): ReadResult {
  let root: Json;
  try {
    root = JSON.parse(text.charCodeAt(0) === 0xfeff ? text.slice(1) : text);
  } catch (e) {
    return { ok: false, errors: [{ code: "JSON_SYNTAX", path: "", message: (e as Error).message }], warnings: [] };
  }
  return validateLevelDefinition(root);
}
