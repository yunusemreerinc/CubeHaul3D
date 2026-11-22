/**
 * Parity scenarios (Task 10): every built-in level played by several seeded input policies (greedy, random,
 * delayed and out-of-order pickups, invalid input, deliberate jams), on its own tray or on trays generated and
 * built from other seeds, with other slot counts and edges. Each file holds only the calls made and, after every
 * call, a compact snapshot of what the rules report; the C# parity test replays the calls and names the first
 * step where any value differs. See docs/PARITY.md.
 */
import { Game } from "../../vendor/cube-hunt/src/core/game";
import type { Level } from "../../vendor/cube-hunt/src/core/types";
import { buildLevel, generateLevel, type BuildOptions } from "../../vendor/cube-hunt/src/levels/generator";
import { mulberry32 } from "../../vendor/cube-hunt/src/levels/rng";
import type { LevelDefinition } from "../../../level-format/src/levelDefinition";
import { readLevelDefinition } from "../../../level-format/src/validate";
import { GOLDEN_DIR, header, levelFile, toLevel } from "./golden";
import { columnsSha256, fnv1a } from "./hash";
import { stringify } from "./json";

type Edges = LevelDefinition["edges"];
type TraySource =
  | { source: "file" }
  | { source: "generate"; seed: number; shuffle: number }
  | { source: "build"; seed: number; shuffle: number; targetWinRate?: number; candidates?: number };

interface ParitySpec {
  name: string;
  description: string;
  level: number;
  edges?: Edges;
  slotCount?: number;
  tray: TraySource;
  policy: string;
  seed: number;
  play: (r: ParityRecorder, rng: () => number) => void;
}

type Dispatch = [number, number, number];

/** One call and what the rules report after it. Keys are kept short and stable; docs/PARITY.md lists them. */
interface Row {
  i: number;
  op: string;
  col?: number;
  ready?: boolean[] | null;
  cell?: number;
  result?: number;
  sent?: Dispatch[];
  status: string;
  remaining: number;
  pending: number;
  tray: number;
  columns: number[];
  fronts: number[];
  slots: (Dispatch | null)[];
  emptied: number[];
  canEat: boolean[];
  blocked: boolean[];
  stuck: boolean;
  reachable: number[];
  pick: number[];
  reachHash: string;
  boardHash: string;
}

export class ParityRecorder {
  readonly rows: Row[] = [];
  /** Trucks out (claimed, not collected), in the order they were sent. */
  readonly out: number[] = [];
  private cells: Int16Array;

  constructor(readonly game: Game) {
    this.cells = Int16Array.from(game.board.cells);
    this.record({ op: "start" });
  }

  get g() {
    return this.game;
  }

  private record(op: Partial<Row> & { op: string }): void {
    const g = this.game;
    const b = g.board;
    const n = b.cells.length;
    const colors = g.level.picture.palette.length;
    const reach = Array.from({ length: colors }, (_, c) => b.reachableCells(c).sort((x, y) => x - y));
    const emptied: number[] = [];
    const board: number[] = [];
    for (let i = 0; i < n; i++) if (this.cells[i] !== -1 && b.cells[i] === -1) emptied.push(i);
    for (let i = 0; i < n; i++) board.push(b.cells[i]);
    for (let i = 0; i < n; i++) board.push(b.isOpen(i) ? 1 : 0);
    for (let i = 0; i < n; i++) board.push(b.isClaimed(i) ? 1 : 0);
    this.rows.push({
      i: this.rows.length,
      ...op,
      status: g.status,
      remaining: b.remaining,
      pending: b.pending,
      tray: g.trayCount(),
      columns: g.columns.map((c) => c.length),
      fronts: g.columns.map((c) => (c.length ? c[0].id : -1)),
      slots: g.slots.map((s) => (s ? [s.id, s.color, s.count] : null)),
      emptied,
      canEat: g.slots.map((_, s) => g.slotCanEat(s)),
      blocked: g.slots.map((_, s) => g.slotBlocked(s)),
      stuck: g.isStuck(),
      reachable: reach.map((r) => r.length),
      pick: reach.map((_, c) => b.pickTarget(c)),
      reachHash: fnv1a(reach.flatMap((r) => [...r, -1])),
      boardHash: fnv1a(board),
    });
    this.cells = Int16Array.from(b.cells);
  }

  tap(col: number): number {
    const result = this.game.tap(col);
    this.record({ op: "tap", col, result });
    return result;
  }

  tick(ready: boolean[] | null = null): Dispatch[] {
    const sent: Dispatch[] = (ready ? this.game.tick((s) => ready[s] === true) : this.game.tick()).map((d) => [d.slot, d.cell, d.color]);
    for (const d of sent) this.out.push(d[1]);
    this.record({ op: "tick", ready, sent });
    return sent;
  }

  step(): Dispatch[] {
    const sent: Dispatch[] = this.game.step().map((d) => [d.slot, d.cell, d.color]);
    this.record({ op: "step", sent });
    return sent;
  }

  /** Collects the truck at this index of `out`. */
  collectAt(k: number): void {
    const cell = this.out.splice(k, 1)[0];
    this.game.collect(cell);
    this.record({ op: "collect", cell });
  }

  addSlot(): void {
    this.game.addSlot();
    this.record({ op: "addSlot" });
  }

  collectAll(): void {
    while (this.out.length) this.collectAt(0);
  }

  /** After the end: a won game ignores everything; a stuck one too, until +1 Slot. */
  epilogue(): void {
    this.tick();
    this.step();
    this.tap(0);
    this.tap(-1);
  }
}

// ---------------------------------------------------------------- input policies

const nonEmpty = (g: Game) => g.columns.flatMap((c, i) => (c.length ? [i] : []));
const pick = <T>(rng: () => number, items: T[]) => items[Math.floor(rng() * items.length)];

/** Fills free slots with the column whose front crate has the most reachable cubes per crate of its color already in a slot. */
function tapBest(r: ParityRecorder): boolean {
  const g = r.g;
  let best = -1;
  let score = -Infinity;
  g.columns.forEach((c, i) => {
    if (!c.length) return;
    const reach = g.board.reachableCount(c[0].color);
    if (reach === 0) return;
    const s = reach / (1 + g.slots.filter((k) => k?.color === c[0].color).length);
    if (s > score) {
      score = s;
      best = i;
    }
  });
  if (best < 0) return false;
  r.tap(best);
  return true;
}

/** Greedy taps, then step (every truck collects at once); +1 Slot when stuck, up to maxSlots. */
const greedyStep = (maxOps: number, maxSlots = 6) => (r: ParityRecorder) => {
  const g = r.g;
  while (g.status !== "won" && r.rows.length < maxOps) {
    if (g.status === "stuck") {
      if (g.slots.length >= maxSlots) break;
      r.addSlot();
      continue;
    }
    while (g.hasFreeSlot() && tapBest(r));
    const sent = r.step();
    if (sent.length === 0 && g.status === "playing") {
      const cols = nonEmpty(g);
      if (!cols.length || r.tap(cols[0]) < 0) break;
    }
  }
};

/** cube-hunt's careless player: random taps while a slot is free, then step; +1 Slot when stuck. */
const randomStep = (maxOps: number, maxSlots = 6) => (r: ParityRecorder, rng: () => number) => {
  const g = r.g;
  while (g.status !== "won" && r.rows.length < maxOps) {
    if (g.status === "stuck") {
      if (g.slots.length >= maxSlots) break;
      r.addSlot();
      continue;
    }
    let taps = 0;
    while (g.status === "playing" && g.hasFreeSlot() && nonEmpty(g).length) {
      r.tap(pick(rng, nonEmpty(g)));
      taps++;
    }
    if (g.status !== "playing") continue;
    if (r.step().length === 0 && taps === 0) break;
  }
};

/** Trucks travel: random taps, ticks with slots held back, pickups in random order. */
const delayed = (maxOps: number, pReady: number, maxSlots = 6) => (r: ParityRecorder, rng: () => number) => {
  const g = r.g;
  while (g.status !== "won" && r.rows.length < maxOps) {
    const x = rng();
    if (g.status === "stuck") {
      if (g.slots.length >= maxSlots) break;
      r.addSlot();
    } else if (x < 0.25 && g.hasFreeSlot() && nonEmpty(g).length) r.tap(pick(rng, nonEmpty(g)));
    else if (x < 0.6) r.tick(g.slots.map(() => rng() < pReady));
    else if (r.out.length) r.collectAt(Math.floor(rng() * r.out.length));
    else r.tick();
  }
  r.collectAll();
};

/** Trucks arrive in the order they left, `lag` ticks later; greedy taps. */
const fifo = (maxOps: number, lag: number, maxSlots = 6) => (r: ParityRecorder) => {
  const g = r.g;
  const sentAt: number[] = [];
  let t = 0;
  while (g.status !== "won" && r.rows.length < maxOps) {
    if (g.status === "stuck") {
      if (g.slots.length >= maxSlots) break;
      r.addSlot();
      continue;
    }
    while (g.hasFreeSlot() && tapBest(r));
    const sent = r.tick();
    for (let k = 0; k < sent.length; k++) sentAt.push(t);
    while (sentAt.length && t - sentAt[0] >= lag) {
      sentAt.shift();
      r.collectAt(0);
    }
    if (sent.length === 0 && r.out.length === 0 && g.status === "playing") {
      const cols = nonEmpty(g);
      if (!cols.length || r.tap(cols[0]) < 0) break;
    }
    t++;
  }
  r.collectAll();
};

/** Greedy taps, tick, then the trucks of that tick collect last-sent first. */
const reverseCollect = (maxOps: number, maxSlots = 6) => (r: ParityRecorder) => {
  const g = r.g;
  while (g.status !== "won" && r.rows.length < maxOps) {
    if (g.status === "stuck") {
      if (g.slots.length >= maxSlots) break;
      r.addSlot();
      continue;
    }
    while (g.hasFreeSlot() && tapBest(r));
    const sent = r.tick();
    while (r.out.length) r.collectAt(r.out.length - 1);
    if (sent.length === 0 && g.status === "playing") {
      const cols = nonEmpty(g);
      if (!cols.length || r.tap(cols[0]) < 0) break;
    }
  }
};

/** Input the game must ignore, mixed into random play: bad and empty columns, taps on full slots, held ticks. */
const abuse = (maxOps: number, maxSlots = 7) => (r: ParityRecorder, rng: () => number) => {
  const g = r.g;
  while (g.status !== "won" && r.rows.length < maxOps) {
    const x = rng();
    if (g.status === "stuck") {
      r.tap(pick(rng, [0, -1, g.columns.length]));
      r.tick();
      if (g.slots.length >= maxSlots) break;
      r.addSlot();
    } else if (x < 0.08) r.tap(pick(rng, [-1, -7, g.columns.length, g.columns.length + 3]));
    else if (x < 0.14) {
      const empty = g.columns.findIndex((c) => c.length === 0);
      r.tap(empty >= 0 ? empty : pick(rng, nonEmpty(g).length ? nonEmpty(g) : [0]));
    } else if (x < 0.36 && nonEmpty(g).length) r.tap(pick(rng, nonEmpty(g)));
    else if (x < 0.42) r.tick(g.slots.map(() => false));
    else if (x < 0.45 && g.slots.length < maxSlots && g.status === "playing" && rng() < 0.3) r.addSlot();
    else if (x < 0.75) r.tick(g.slots.map(() => rng() < 0.8));
    else if (r.out.length) r.collectAt(Math.floor(rng() * r.out.length));
    else r.step();
  }
  r.collectAll();
};

/** Taps crates that cannot eat whenever it can, to jam the slots; +1 Slot each time, up to maxSlots. */
const jam = (maxOps: number, maxSlots: number) => (r: ParityRecorder, rng: () => number) => {
  const g = r.g;
  while (g.status !== "won" && r.rows.length < maxOps) {
    if (g.status === "stuck") {
      if (g.slots.length >= maxSlots) break;
      r.addSlot();
      continue;
    }
    while (g.status === "playing" && g.hasFreeSlot() && nonEmpty(g).length) {
      const blocked = nonEmpty(g).filter((c) => g.board.reachableCount(g.columns[c][0].color) === 0);
      r.tap(pick(rng, blocked.length ? blocked : nonEmpty(g)));
    }
    if (g.status !== "playing") continue;
    const sent = r.tick();
    if (r.out.length && rng() < 0.6) r.collectAt(Math.floor(rng() * r.out.length));
    if (sent.length === 0 && r.out.length === 0 && g.status === "playing" && !g.hasFreeSlot()) r.step();
    if (sent.length === 0 && r.out.length === 0 && g.status === "playing" && !nonEmpty(g).length) break;
  }
  r.collectAll();
};

const then = (...parts: ((r: ParityRecorder, rng: () => number) => void)[]) => (r: ParityRecorder, rng: () => number) => {
  for (const p of parts) p(r, rng);
};
const epilogue = (r: ParityRecorder) => r.epilogue();

// ---------------------------------------------------------------- the scenarios

const SPECS: ParitySpec[] = [
  { name: "p01-strawberry-greedy-step", description: "Strawberry, its own tray, greedy taps and step to the win; then input after the win.", level: 0, tray: { source: "file" }, policy: "greedy-step", seed: 0, play: then(greedyStep(400), epilogue) },
  { name: "p02-strawberry-generated-delayed", description: "Strawberry on a tray generated from seed 3, shuffle 1.5, bottom edge, 3 slots; random taps, held slots, pickups out of order.", level: 0, edges: "bottom", slotCount: 3, tray: { source: "generate", seed: 3, shuffle: 1.5 }, policy: "delayed 0.7", seed: 11, play: delayed(350, 0.7) },
  { name: "p03-strawberry-one-slot-random", description: "Strawberry with a single slot: careless random taps, stuck often, +1 Slot up to 6.", level: 0, slotCount: 1, tray: { source: "file" }, policy: "random-step", seed: 13, play: randomStep(300) },
  { name: "p04-mushroom-fifo", description: "Mushroom, its own tray, trucks arriving in order three ticks after they leave.", level: 1, tray: { source: "file" }, policy: "fifo lag 3", seed: 0, play: fifo(400, 3) },
  { name: "p05-mushroom-abuse", description: "Mushroom with invalid and ignored input mixed into random play.", level: 1, tray: { source: "file" }, policy: "abuse", seed: 8, play: abuse(350) },
  { name: "p06-mushroom-all-edges-fifo0", description: "Mushroom's tray with all edges open; every truck collects right after its tick.", level: 1, edges: "all", tray: { source: "file" }, policy: "fifo lag 0", seed: 0, play: fifo(400, 0) },
  { name: "p07-sunset-random-step", description: "Sunset, its own tray, careless random play with step.", level: 2, tray: { source: "file" }, policy: "random-step", seed: 21, play: then(randomStep(350), epilogue) },
  { name: "p08-sunset-built-sides-delayed", description: "Sunset on a tray built from seed 99 (target 0.5, 4 candidates), sides edges; held slots and out-of-order pickups.", level: 2, edges: "sides", tray: { source: "build", seed: 99, shuffle: 0.6, targetWinRate: 0.5, candidates: 4 }, policy: "delayed 0.6", seed: 4, play: delayed(350, 0.6) },
  { name: "p09-sunset-six-slots-jam", description: "Sunset with 6 slots, jammed on purpose with blocked crates, +1 Slot up to 8.", level: 2, slotCount: 6, tray: { source: "file" }, policy: "jam", seed: 77, play: jam(350, 8) },
  { name: "p10-planet-greedy-step", description: "Ringed Planet (46x46), its own tray, greedy taps and step.", level: 3, tray: { source: "file" }, policy: "greedy-step", seed: 0, play: greedyStep(700) },
  { name: "p11-planet-generated-seven-slots-fifo", description: "Ringed Planet on a tray generated from seed 1234, shuffle 0, 7 slots; trucks arrive in order a tick later.", level: 3, slotCount: 7, tray: { source: "generate", seed: 1234, shuffle: 0 }, policy: "fifo lag 1", seed: 0, play: fifo(400, 1) },
  { name: "p12-cat-jam", description: "Cat, its own tray, jammed on purpose, +1 Slot up to 7.", level: 4, tray: { source: "file" }, policy: "jam", seed: 3, play: jam(350, 7) },
  { name: "p13-cat-generated-max-seed-random", description: "Cat on a tray generated from seed 2147483647, shuffle 0.5, all edges; careless random play.", level: 4, edges: "all", tray: { source: "generate", seed: 2147483647, shuffle: 0.5 }, policy: "random-step", seed: 9, play: randomStep(350) },
  { name: "p14-cat-reverse-collect", description: "Cat, its own tray, greedy taps; each tick's trucks collect last-sent first.", level: 4, tray: { source: "file" }, policy: "reverse-collect", seed: 0, play: reverseCollect(400) },
  { name: "p15-cactus-delayed", description: "Cactus, its own tray, held slots and out-of-order pickups.", level: 5, tray: { source: "file" }, policy: "delayed 0.7", seed: 31, play: delayed(350, 0.7) },
  { name: "p16-cactus-two-slots-greedy", description: "Cactus with 2 slots: greedy step play, +1 Slot when stuck.", level: 5, slotCount: 2, tray: { source: "file" }, policy: "greedy-step", seed: 0, play: then(greedyStep(400), epilogue) },
  { name: "p17-rocket-random-step", description: "Rocket, its own tray, careless random play with step.", level: 6, tray: { source: "file" }, policy: "random-step", seed: 1, play: then(randomStep(350), epilogue) },
  { name: "p18-rocket-generated-shuffle100-fifo", description: "Rocket on a tray generated from seed 0, shuffle 100; trucks arrive in order five ticks later.", level: 6, tray: { source: "generate", seed: 0, shuffle: 100 }, policy: "fifo lag 5", seed: 0, play: fifo(400, 5) },
  { name: "p19-duck-abuse", description: "Rubber Duck with invalid and ignored input mixed into random play.", level: 7, tray: { source: "file" }, policy: "abuse", seed: 2, play: abuse(350) },
  { name: "p20-duck-built-greedy", description: "Rubber Duck on a tray built from seed 7 (target 0.3, 3 candidates); greedy step play.", level: 7, tray: { source: "build", seed: 7, shuffle: 1.2, targetWinRate: 0.3, candidates: 3 }, policy: "greedy-step", seed: 0, play: then(greedyStep(700), epilogue) },
  { name: "p21-icecream-sides-delayed", description: "Ice Cream's tray with sides edges; held slots and out-of-order pickups.", level: 8, edges: "sides", tray: { source: "file" }, policy: "delayed 0.7", seed: 500, play: delayed(350, 0.7) },
  { name: "p22-icecream-mostly-held", description: "Ice Cream, its own tray, slots mostly held back (20% ready).", level: 8, tray: { source: "file" }, policy: "delayed 0.2", seed: 6, play: delayed(350, 0.2) },
  { name: "p23-watermelon-greedy-step", description: "Watermelon, its own tray, greedy taps and step.", level: 9, tray: { source: "file" }, policy: "greedy-step", seed: 0, play: then(greedyStep(700), epilogue) },
  { name: "p24-watermelon-one-slot-jam", description: "Watermelon on a tray generated from seed 42, shuffle 2, one slot: jammed, +1 Slot up to 6.", level: 9, slotCount: 1, tray: { source: "generate", seed: 42, shuffle: 2 }, policy: "jam", seed: 42, play: jam(300, 6) },
];

function levelFor(spec: ParitySpec, levelFiles: Record<string, string>): Level {
  const def = readLevelDefinition(levelFiles[levelFile(spec.level)]).level!;
  const edges = spec.edges ?? def.edges;
  const slotCount = spec.slotCount ?? def.slotCount;
  const base = toLevel(def);
  const picture = base.picture;
  if (spec.tray.source === "file") return { ...base, edges, slotCount };
  const g = def.generation!;
  const opts: BuildOptions = { ...g, edges, slots: slotCount, seed: spec.tray.seed, shuffle: spec.tray.shuffle };
  delete (opts as { candidates?: number }).candidates;
  if (spec.tray.source === "generate") return generateLevel(picture, opts);
  if (spec.tray.targetWinRate !== undefined) opts.targetWinRate = spec.tray.targetWinRate;
  const warn = console.warn;
  console.warn = () => {};
  const built = buildLevel(picture, opts, spec.tray.candidates ?? 16).level;
  console.warn = warn;
  return built;
}

/** Fields that describe the call itself; always written. */
const OP_FIELDS = new Set(["i", "op", "col", "ready", "cell", "result", "sent"]);

function parityFile(spec: ParitySpec, levelFiles: Record<string, string>) {
  const level = levelFor(spec, levelFiles);
  const recorder = new ParityRecorder(new Game(level));
  spec.play(recorder, mulberry32(spec.seed));
  const rows = recorder.rows;
  const head = {
    ...header(
      "parity",
      "One parity scenario: the calls made (op, col, ready, cell) with their results (result, sent) and, after each call, what the rules report. docs/PARITY.md.",
    ),
    name: spec.name,
    scenario: spec.description,
    levelFile: levelFile(spec.level),
    edges: level.edges,
    slotCount: level.slotCount,
    tray: spec.tray,
    traySha256: columnsSha256(level.columns),
    policy: spec.policy,
    seed: spec.seed,
    outcome: recorder.game.status,
    stepCount: rows.length,
  };
  // One row per line, and from the second row on only the snapshot fields that changed since the row before
  // (an absent field = same value as in the previous row). Keeps 24 files of several hundred steps small.
  const lines = rows.map((row, k) => {
    if (k === 0) return JSON.stringify(row);
    const prev = rows[k - 1] as unknown as Record<string, unknown>;
    const out: Record<string, unknown> = {};
    for (const [key, value] of Object.entries(row)) {
      if (OP_FIELDS.has(key) || JSON.stringify(value) !== JSON.stringify(prev[key])) out[key] = value;
    }
    return JSON.stringify(out);
  });
  const text = stringify(head);
  return `${text.slice(0, -2)},\n  "steps": [\n${lines.map((l) => "    " + l).join(",\n")}\n  ]\n}\n`;
}

/**
 * Replays a parity file's calls on a fresh Game (the tray made again from its recipe), checking every invariant
 * after each call, and returns the rows it records, expanded. A file is repeatable if these equal its own rows.
 */
export function replayParity(doc: { levelFile: string; edges: Edges; slotCount: number; tray: TraySource; steps: Record<string, unknown>[] },
  levelFiles: Record<string, string>, check: (g: Game) => void): { rows: Row[]; traySha256: string } {
  const index = Number(/level-(\d+)-/.exec(doc.levelFile)![1]) - 1;
  const spec: ParitySpec = { name: "", description: "", level: index, edges: doc.edges, slotCount: doc.slotCount, tray: doc.tray, policy: "", seed: 0, play: () => {} };
  const level = levelFor(spec, levelFiles);
  const r = new ParityRecorder(new Game(level));
  check(r.g);
  for (const step of doc.steps.slice(1)) {
    switch (step.op) {
      case "tap": r.tap(step.col as number); break;
      case "tick": r.tick(step.ready as boolean[] | null); break;
      case "step": r.step(); break;
      case "collect": {
        const k = r.out.indexOf(step.cell as number);
        if (k >= 0) r.collectAt(k);
        else throw new Error(`collect of cell ${step.cell}, which no truck was sent for`);
        break;
      }
      case "addSlot": r.addSlot(); break;
      default: throw new Error(`unknown op ${step.op}`);
    }
    check(r.g);
  }
  return { rows: r.rows, traySha256: columnsSha256(level.columns) };
}

/** Expands a parity file's rows: every row with all its fields. */
export function expandRows(steps: Record<string, unknown>[]): Record<string, unknown>[] {
  const out: Record<string, unknown>[] = [];
  for (const [k, row] of steps.entries()) {
    const full: Record<string, unknown> = {};
    if (k > 0) for (const [key, value] of Object.entries(out[k - 1])) if (!OP_FIELDS.has(key)) full[key] = value;
    Object.assign(full, row);
    out.push(full);
  }
  return out;
}

/** Every parity file: repo-relative path -> exact content. */
export function parityFiles(levelFiles: Record<string, string>): Record<string, string> {
  const files: Record<string, string> = {};
  for (const spec of SPECS) files[`${GOLDEN_DIR}/parity/${spec.name}.json`] = parityFile(spec, levelFiles);
  return files;
}
