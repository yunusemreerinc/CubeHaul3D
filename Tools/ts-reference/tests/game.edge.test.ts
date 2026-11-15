/**
 * Game edge cases: full slots, columns that cannot be tapped, won/stuck, and play after the +1 Slot booster.
 * Behaviour ids (B-xx) and invariants (I-xx) refer to docs/PORTING_PLAN.md.
 */
import { Game } from "@ref/core/game";
import { describe, expect, it } from "vitest";
import { level, pic, solid, violations } from "./helpers";

/** Everything that tap/tick/addSlot may change, for "nothing changed" checks. */
const snapshot = (g: Game) =>
  JSON.stringify({
    columns: g.columns,
    slots: g.slots,
    status: g.status,
    cells: [...g.board.cells],
    pending: g.board.pending,
    remaining: g.board.remaining,
  });

describe("full slots (B-10, I-12)", () => {
  it("a tap with every slot taken returns -1 and changes nothing", () => {
    const g = new Game(level(solid(3, 1), [[[0, 1]], [[0, 1]], [[0, 1]]], 2));
    expect(g.tap(0)).toBe(0);
    expect(g.tap(1)).toBe(1);
    expect(g.status).toBe("playing"); // both crates can eat, so not stuck
    expect(g.hasFreeSlot()).toBe(false);
    expect(g.canTap(2)).toBe(false);
    const before = snapshot(g);
    expect(g.tap(2)).toBe(-1);
    expect(snapshot(g)).toBe(before);
    expect(g.columns[2]).toHaveLength(1);
  });

  it("a crate's slot frees on the tick that sends its last truck, and the next tap takes the lowest free slot", () => {
    const g = new Game(level(solid(4, 1), [[[0, 1]], [[0, 2]], [[0, 1]]], 2));
    g.tap(0);
    g.tap(1);
    const sent = g.tick();
    expect(sent.map((d) => d.slot)).toEqual([0, 1]);
    expect(g.slots[0]).toBeNull(); // its only truck left
    expect(g.slots[1]?.count).toBe(1);
    expect(g.tap(2)).toBe(0);
  });

  it("only the front crate leaves a column; the crates behind keep their order", () => {
    const g = new Game(level(solid(6, 1), [[[0, 1], [0, 2], [0, 3]]], 3));
    expect(g.front(0)?.id).toBe(0);
    g.tap(0);
    expect(g.slots[0]?.id).toBe(0);
    expect(g.columns[0].map((b) => b.id)).toEqual([1, 2]);
    g.tap(0);
    expect(g.slots.map((b) => b?.id ?? null)).toEqual([0, 1, null]);
    expect(g.columns[0].map((b) => b.id)).toEqual([2]);
  });
});

describe("columns that cannot be tapped (B-10, B-13)", () => {
  const twoColumns = () => new Game(level(solid(2, 1), [[[0, 2]], []], 2));

  it("an empty column returns -1", () => {
    const g = twoColumns();
    expect(g.canTap(1)).toBe(false);
    expect(g.tap(1)).toBe(-1);
  });

  it.each([-1, 2, 99, 0.5, Number.NaN])("a column index that does not exist (%s) returns -1 and changes nothing", (col) => {
    const g = twoColumns();
    const before = snapshot(g);
    expect(g.tap(col)).toBe(-1);
    expect(snapshot(g)).toBe(before);
  });

  it("a column is empty once its last crate is tapped", () => {
    const g = twoColumns();
    expect(g.tap(0)).toBe(0);
    expect(g.front(0)).toBeUndefined();
    expect(g.tap(0)).toBe(-1);
  });

  it("no tap is accepted once the game is over, even with a crate and a free slot left", () => {
    // Only possible with a tray that has more trucks than cubes, but it shows that tap checks the status first.
    const g = new Game(level(solid(1, 1), [[[0, 1], [0, 5]]], 3));
    g.tap(0);
    g.step();
    expect(g.status).toBe("won");
    expect(g.hasFreeSlot()).toBe(true);
    expect(g.columns[0]).toHaveLength(1);
    expect(g.canTap(0)).toBe(false);
    expect(g.tap(0)).toBe(-1);
  });
});

describe("won (B-13, I-14)", () => {
  it("is declared when the last cube is collected, not when its truck is sent", () => {
    const g = new Game(level(solid(1, 1), [[[0, 1]]], 1));
    g.tap(0);
    expect(g.tick().map((d) => d.cell)).toEqual([0]);
    expect(g.board.remaining).toBe(1);
    expect(g.status).toBe("playing");
    g.collect(0);
    expect(g.status).toBe("won");
  });

  it("leaves the tray and the slots empty, since there is one truck per cube (I-01)", () => {
    const p = pic(["1100", "0011", "0101"]);
    const g = new Game(level(p, [[[0, 3]], [[1, 3], [0, 3]], [[1, 3]]], 3));
    for (let guard = 0; g.status !== "won" && guard < 100; guard++) {
      if (g.status === "stuck") g.addSlot();
      while (g.hasFreeSlot() && g.tap(g.columns.findIndex((c) => c.length > 0)) >= 0);
      g.step();
      expect(violations(g)).toEqual([]);
    }
    expect(g.status).toBe("won");
    expect(g.trayCount()).toBe(0);
    expect(g.slots.every((s) => s === null)).toBe(true);
  });

  it("is final: tick sends nothing and addSlot does not change it", () => {
    const g = new Game(level(solid(1, 1), [[[0, 1]]], 1));
    g.tap(0);
    g.step();
    expect(g.tick()).toEqual([]);
    g.addSlot();
    expect(g.status).toBe("won");
    expect(g.slots).toHaveLength(2);
  });

  it("FINDING: a level with no cubes starts as 'playing' and only becomes 'won' on the first tick", () => {
    // The constructor does not call updateStatus. No built-in level is empty, but an uploaded,
    // fully transparent image would be. Recorded as current behaviour; see docs/TS_BASELINE.md.
    const empty = pic(["..", ".."]);
    empty.palette = ["#000"];
    const g = new Game(level(empty, [], 5));
    expect(g.board.remaining).toBe(0);
    expect(g.status).toBe("playing");
    expect(g.tick()).toEqual([]);
    expect(g.status).toBe("won");
  });
});

describe("stuck (B-13, I-14, I-15)", () => {
  // One column, open at the bottom: color 1 on top of color 0.
  const stacked = pic(["1", "0"]);

  it("is not declared while a slot is free, even if nothing in the slots can eat", () => {
    const g = new Game(level(stacked, [[[1, 1]], [[0, 1]]], 2));
    g.tap(0);
    expect(g.slotCanEat(0)).toBe(false);
    expect(g.status).toBe("playing");
  });

  it("is declared as soon as the last free slot is filled with a crate that cannot eat", () => {
    const g = new Game(level(stacked, [[[1, 1], [0, 1]]], 1));
    g.tap(0);
    expect(g.status).toBe("stuck");
    expect(g.tick()).toEqual([]);
    expect(g.tap(0)).toBe(-1);
  });

  it("is not declared while a truck is still out, then is once it has collected and nothing opens", () => {
    // Color 0 at the bottom, color 1 above it, color 2 on top; slots: 0 and 2.
    const p = pic(["2", "1", "0"]);
    const g = new Game(level(p, [[[0, 1]], [[2, 1]], [[1, 1]]], 2));
    g.tap(0);
    g.tap(1);
    g.tick(); // a truck leaves for the color-0 cube; the slot frees
    expect(g.status).toBe("playing");
    g.tap(2); // color 1 into the free slot: will be reachable once the truck collects
    expect(g.board.pending).toBe(1);
    expect(g.status).toBe("playing"); // I-15
    g.collect(2);
    expect(g.slotCanEat(0)).toBe(true);
    expect(g.status).toBe("playing");
  });

  it("can be reached by a collect: the pickup opens only a color that no slot holds", () => {
    // Color 0, then color 1, then color 0 again, open at the bottom. One slot, holding the color-0 crate of 2.
    const g = new Game(level(pic(["0", "1", "0"]), [[[0, 2], [1, 1]]], 1));
    g.tap(0);
    expect(g.tick().map((d) => d.cell)).toEqual([2]);
    expect(g.status).toBe("playing"); // a truck is out
    g.collect(2);
    // Now only the color-1 cube is reachable, the slot still holds color 0 and no truck is out.
    expect(g.board.reachableCount(1)).toBe(1);
    expect(g.slots[0]?.count).toBe(1);
    expect(g.status).toBe("stuck");
  });
});

describe("play after the +1 Slot booster (B-15, B-30)", () => {
  it("stuck -> addSlot -> playing, and the level can still be won", () => {
    const g = new Game(level(pic(["1", "0"]), [[[1, 1], [0, 1]]], 1));
    g.tap(0);
    expect(g.status).toBe("stuck");
    g.addSlot();
    expect(g.status).toBe("playing");
    expect(g.slots).toHaveLength(2);
    expect(g.tap(0)).toBe(1); // color 0 goes into the new slot
    expect(g.step().map((d) => d.cell)).toEqual([1]); // color 0 eats; color 1 opens up
    expect(g.step().map((d) => d.cell)).toEqual([0]); // and the crate that was stuck eats it
    expect(g.status).toBe("won");
  });

  it("adding a slot while playing only adds a free slot at the end", () => {
    const g = new Game(level(solid(3, 1), [[[0, 1]], [[0, 2]]], 1));
    g.tap(0);
    g.addSlot();
    expect(g.status).toBe("playing");
    expect(g.slots.map((s) => s?.id ?? null)).toEqual([0, null]);
    expect(g.tap(1)).toBe(1);
  });

  it("the core has no limit on slots: the once-per-level, max-6 rule lives in main.ts (B-30)", () => {
    const g = new Game(level(solid(1, 1), [[[0, 1]]], 5));
    for (let k = 0; k < 4; k++) g.addSlot();
    expect(g.slots).toHaveLength(9);
  });

  it("a stuck game always has a crate left in the tray, so +1 Slot always gives a move (liveness)", () => {
    const g = new Game(level(pic(["1", "0"]), [[[1, 1], [0, 1]]], 1));
    g.tap(0);
    expect(g.status).toBe("stuck");
    expect(g.trayCount()).toBeGreaterThan(0);
  });
});

describe("the level passed in is never changed (B-16)", () => {
  it("playing a Game leaves the Level untouched, so a restart starts fresh", () => {
    const lv = level(solid(3, 1), [[[0, 3]]], 1);
    const before = JSON.stringify({ columns: lv.columns, cells: [...lv.picture.cells] });
    const g = new Game(lv);
    g.tap(0);
    while (g.status === "playing") g.step();
    expect(JSON.stringify({ columns: lv.columns, cells: [...lv.picture.cells] })).toBe(before);
    const again = new Game(lv);
    expect(again.columns[0][0].count).toBe(3);
    expect(again.board.remaining).toBe(3);
  });
});
