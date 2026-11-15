/**
 * Records a game as a list of operations, each followed by a full snapshot of the rules' state.
 * The C# equivalence tests replay the operations on the C# Game and compare every snapshot field.
 */
import type { Game } from "../../vendor/cube-hunt/src/core/game";
import { fnv1a } from "./hash";

export type Op =
  | { op: "start" }
  | { op: "tap"; col: number; result: number }
  | { op: "tick"; ready: boolean[] | null; result: [number, number, number][] }
  | { op: "step"; result: [number, number, number][] }
  | { op: "collect"; cell: number }
  | { op: "addSlot" };

export interface Snapshot {
  status: string;
  remaining: number;
  pending: number;
  trayCount: number;
  /** Crate ids per column, front first (their counts never change while in the tray). */
  columns: number[][];
  /** [id, color, count] per slot, or null for a free slot. */
  slots: ([number, number, number] | null)[];
  /** Cells claimed by a truck but not collected yet, ascending. */
  claimed: number[];
  /** Cells that became empty since the previous snapshot, ascending. */
  emptied: number[];
  reachableCount: number[];
  /** Board.pickTarget(color) for every palette color: the next cube a truck of that color would take, or -1. */
  pickTarget: number[];
  /** Reachable cells per color, ascending; only in "detail" scenarios (small boards). */
  reachable?: number[][];
  /** fnv1a over the ascending reachable cells of every color, each color's list followed by -1. */
  reachableHash: string;
  slotCanEat: boolean[];
  slotBlocked: boolean[];
  isStuck: boolean;
  /** fnv1a over: every cell's color (-1 = empty), then isOpen (0/1) per cell, then isClaimed (0/1) per cell. */
  boardHash: string;
}

export type Step = Op & { state: Snapshot };

const dispatches = (sent: { slot: number; cell: number; color: number }[]): [number, number, number][] =>
  sent.map((d) => [d.slot, d.cell, d.color]);

export function snapshot(g: Game, previousCells: ArrayLike<number> | null, detail: boolean): Snapshot {
  const b = g.board;
  const n = b.cells.length;
  const colors = g.level.picture.palette.length;
  const reachable = Array.from({ length: colors }, (_, c) => b.reachableCells(c).sort((x, y) => x - y));
  const claimed: number[] = [];
  const emptied: number[] = [];
  for (let i = 0; i < n; i++) {
    if (b.isClaimed(i)) claimed.push(i);
    if (previousCells && previousCells[i] !== -1 && b.cells[i] === -1) emptied.push(i);
  }
  const board: number[] = [];
  for (let i = 0; i < n; i++) board.push(b.cells[i]);
  for (let i = 0; i < n; i++) board.push(b.isOpen(i) ? 1 : 0);
  for (let i = 0; i < n; i++) board.push(b.isClaimed(i) ? 1 : 0);
  return {
    status: g.status,
    remaining: b.remaining,
    pending: b.pending,
    trayCount: g.trayCount(),
    columns: g.columns.map((col) => col.map((k) => k.id)),
    slots: g.slots.map((s) => (s ? [s.id, s.color, s.count] : null)),
    claimed,
    emptied,
    reachableCount: reachable.map((r) => r.length),
    pickTarget: reachable.map((_, c) => b.pickTarget(c)),
    reachable: detail ? reachable : undefined,
    reachableHash: fnv1a(reachable.flatMap((r) => [...r, -1])),
    slotCanEat: g.slots.map((_, s) => g.slotCanEat(s)),
    slotBlocked: g.slots.map((_, s) => g.slotBlocked(s)),
    isStuck: g.isStuck(),
    boardHash: fnv1a(board),
  };
}

/** Plays a Game through the recorded operations, snapshotting after each one. */
export class Recorder {
  readonly steps: Step[] = [];
  private cells: Int16Array;

  constructor(
    readonly game: Game,
    private readonly detail: boolean,
  ) {
    this.cells = Int16Array.from(game.board.cells);
    this.steps.push({ op: "start", state: snapshot(game, null, detail) });
  }

  private record(op: Op): void {
    this.steps.push({ ...op, state: snapshot(this.game, this.cells, this.detail) });
    this.cells = Int16Array.from(this.game.board.cells);
  }

  tap(col: number): number {
    const result = this.game.tap(col);
    this.record({ op: "tap", col, result });
    return result;
  }

  tick(ready: boolean[] | null = null): [number, number, number][] {
    const result = dispatches(ready ? this.game.tick((s) => ready[s] === true) : this.game.tick());
    this.record({ op: "tick", ready, result });
    return result;
  }

  step(): [number, number, number][] {
    const result = dispatches(this.game.step());
    this.record({ op: "step", result });
    return result;
  }

  collect(cell: number): void {
    this.game.collect(cell);
    this.record({ op: "collect", cell });
  }

  addSlot(): void {
    this.game.addSlot();
    this.record({ op: "addSlot" });
  }
}
