import { Board } from "@ref/core/board";
import { Game } from "@ref/core/game";
import { EMPTY, type Block, type Level, type OpenEdges, type Picture } from "@ref/core/types";

export const E = EMPTY;

/**
 * Builds a picture from rows of characters: '.' is empty, a digit is that palette index.
 * Row 0 is the top of the board, the last row is the ramp side.
 */
export function pic(rows: string[], name = "test"): Picture {
  const height = rows.length;
  const width = rows[0].length;
  const cells = new Int16Array(width * height);
  let colors = 0;
  rows.forEach((row, y) => {
    if (row.length !== width) throw new Error(`row ${y} has length ${row.length}, expected ${width}`);
    [...row].forEach((ch, x) => {
      const c = ch === "." ? EMPTY : Number(ch);
      if (Number.isNaN(c)) throw new Error(`bad cell '${ch}'`);
      cells[y * width + x] = c;
      colors = Math.max(colors, c + 1);
    });
  });
  return { name, width, height, cells, palette: Array.from({ length: colors }, (_, i) => `#00000${i}`) };
}

/** A solid one-color picture. */
export function solid(width: number, height: number): Picture {
  return { name: "solid", width, height, cells: new Int16Array(width * height), palette: ["#000"] };
}

/** Crates written as [color, count] pairs per column; ids are assigned in reading order. */
export function level(picture: Picture, columns: [number, number][][], slotCount = 5, edges: OpenEdges = "bottom"): Level {
  let id = 0;
  const cols: Block[][] = columns.map((col) => col.map(([color, count]) => ({ id: id++, color, count })));
  return { picture, columns: cols, slotCount, edges };
}

/** Cell index for (x, y) on a board `width` wide. */
export const at = (width: number, x: number, y: number) => y * width + x;

/** Read-only view of Board's private state, for invariant checks only. */
interface BoardInternals {
  open: Uint8Array;
  claimed: Uint8Array;
  reachable: Set<number>[];
  depth: Int32Array;
  road: Float32Array;
}
export const internals = (b: Board) => b as unknown as BoardInternals;

/**
 * Checks every invariant from docs/PORTING_PLAN.md §4 that can be read off a Game.
 * Returns the names of the violated ones (empty = all hold).
 */
export function violations(g: Game): string[] {
  const out: string[] = [];
  const b = g.board;
  const s = internals(b);
  const n = b.cells.length;
  const palette = g.level.picture.palette.length;

  let remaining = 0;
  let pending = 0;
  for (let i = 0; i < n; i++) {
    if (b.cells[i] !== EMPTY) remaining++;
    if (s.claimed[i]) {
      pending++;
      if (b.cells[i] === EMPTY) out.push("I-03 claimed cell is empty");
    }
    if (s.open[i] && b.cells[i] !== EMPTY) out.push("I-04 open cell holds a cube");
  }
  if (remaining !== b.remaining) out.push("I-02 remaining");
  if (pending !== b.pending) out.push("I-02 pending");

  // I-04: open = empty cells connected to an open edge through empty cells.
  const seen = new Uint8Array(n);
  const stack: number[] = [];
  for (let i = 0; i < n; i++) {
    if (b.cells[i] === EMPTY && b.openSides(i).length > 0) {
      seen[i] = 1;
      stack.push(i);
    }
  }
  while (stack.length) {
    const j = stack.pop()!;
    for (const m of b.neighbors(j)) {
      if (!seen[m] && b.cells[m] === EMPTY) {
        seen[m] = 1;
        stack.push(m);
      }
    }
  }
  for (let i = 0; i < n; i++) if (seen[i] !== s.open[i]) out.push(`I-04 open[${i}]`);

  for (let c = 0; c < palette; c++) {
    // I-01: trucks still to go + trucks on their way = cubes of that color on the board.
    let trucks = 0;
    for (const col of g.columns) for (const k of col) if (k.color === c) trucks += k.count;
    for (const k of g.slots) if (k && k.color === c) trucks += k.count;
    let onBoard = 0;
    let claimed = 0;
    for (let i = 0; i < n; i++) {
      if (b.cells[i] === c) {
        onBoard++;
        if (s.claimed[i]) claimed++;
      }
    }
    if (trucks + claimed !== onBoard) out.push(`I-01 color ${c}: ${trucks} + ${claimed} != ${onBoard}`);
    if (b.remainingOf(c) !== onBoard) out.push(`I-02 remainingOf(${c})`);
    // I-06: reachable is exactly the unclaimed, exposed cubes of that color.
    const expected = new Set<number>();
    for (let i = 0; i < n; i++) {
      if (b.cells[i] !== c || s.claimed[i]) continue;
      if (b.openSides(i).length > 0 || b.neighbors(i).some((m) => s.open[m] === 1)) expected.add(i);
    }
    const got = s.reachable[c];
    if (got.size !== expected.size || [...got].some((i) => !expected.has(i))) out.push(`I-06 reachable(${c})`);
    // I-07: pickTarget is -1 exactly when nothing is reachable, else one of the reachable cubes.
    const t = b.pickTarget(c);
    if ((t < 0) !== (got.size === 0) || (t >= 0 && !got.has(t))) out.push(`I-07 pickTarget(${c})`);
  }

  for (const k of g.slots) if (k && k.count < 1) out.push("I-11 slot with count < 1");
  if ((g.status === "won") !== (b.remaining === 0)) out.push("I-14 won <=> remaining = 0");
  if (g.status === "stuck" && (g.hasFreeSlot() || b.pending > 0 || g.slots.some((_, k) => g.slotCanEat(k)))) {
    out.push("I-14 stuck definition");
  }
  // Liveness: while playing with no truck out, a slot can eat or a crate can be tapped.
  if (g.status === "playing" && b.pending === 0) {
    const canEat = g.slots.some((_, k) => g.slotCanEat(k));
    const canTap = g.hasFreeSlot() && g.columns.some((c) => c.length > 0);
    if (!canEat && !canTap) out.push("liveness: playing but no move and no truck out");
  }
  return out;
}

/** Recomputes depth/road for open cells from scratch (BFS from the open edges), for I-09. */
export function freshDepths(b: Board): { depth: Int32Array; road: Float32Array } {
  const fresh = new Board(
    { name: "fresh", width: b.width, height: b.height, cells: Int16Array.from(b.cells), palette: paletteFor(b) },
    b.edges,
  );
  const f = internals(fresh);
  return { depth: f.depth, road: f.road };
}

function paletteFor(b: Board): string[] {
  let max = -1;
  for (const c of b.cells) max = Math.max(max, c);
  return Array.from({ length: Math.max(1, max + 1) }, () => "#000");
}
