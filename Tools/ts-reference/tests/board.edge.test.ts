/**
 * Board edge cases: claimed (pending) cubes, nothing opening behind a cube before it is collected,
 * the differences between the bottom / sides / all edges, and the incremental distance bookkeeping.
 */
import { Board } from "@ref/core/board";
import { Game } from "@ref/core/game";
import { peelLayers } from "@ref/levels/generator";
import { describe, expect, it } from "vitest";
import { at, freshDepths, internals, level, pic, solid } from "./helpers";

const sorted = (xs: number[]) => [...xs].sort((a, b) => a - b);

describe("claimed (pending) cubes (B-06, B-07, I-03, I-10)", () => {
  it("claim takes a cube off the reachable list but leaves it on the board", () => {
    const b = new Board(solid(3, 1), "bottom");
    b.claim(1);
    expect(b.isClaimed(1)).toBe(true);
    expect(b.pending).toBe(1);
    expect(b.remaining).toBe(3);
    expect(b.remainingOf(0)).toBe(3);
    expect(b.reachableCount(0)).toBe(2);
    expect(b.cells[1]).toBe(0);
  });

  it("a claimed cube is never picked again", () => {
    const b = new Board(solid(3, 1), "bottom");
    expect(b.pickTarget(0)).toBe(1);
    b.claim(1);
    expect(b.pickTarget(0)).toBe(0); // 0 and 2 are equally far; the lower index wins
    b.claim(0);
    expect(b.pickTarget(0)).toBe(2);
    b.claim(2);
    expect(b.pickTarget(0)).toBe(-1);
  });

  it("claiming twice, claiming an unreachable cube or an empty cell throws", () => {
    const b = new Board(solid(1, 2), "bottom");
    b.claim(1);
    expect(() => b.claim(1)).toThrow(/not reachable/);
    expect(() => b.claim(0)).toThrow(/not reachable/);
    b.eat(1);
    expect(() => b.claim(1)).toThrow(/not reachable/);
  });

  it("eating a claimed cube clears the claim; eating it again throws", () => {
    const b = new Board(solid(1, 2), "bottom");
    b.claim(1);
    b.eat(1);
    expect(b.pending).toBe(0);
    expect(b.isClaimed(1)).toBe(false);
    expect(b.remaining).toBe(1);
    expect(() => b.eat(1)).toThrow(/already empty/);
  });

  it("FINDING: Game.collect also accepts a reachable cube no truck was sent for", () => {
    // Board.eat takes "claimed, or reachable and taken directly" (step() relies on that), and
    // Game.collect passes straight through. The view only ever collects dispatched cells, so this
    // is a loose API, not a rule. Recorded so the C# port keeps (or deliberately tightens) it.
    const g = new Game(level(solid(2, 1), [[[0, 2]]], 1));
    g.collect(0);
    expect(g.board.remaining).toBe(1);
    expect(g.board.pending).toBe(0);
    expect(g.columns[0][0].count).toBe(2); // the tray did not pay for it: the totals no longer match
  });
});

describe("nothing opens behind a cube until it is collected (B-06, B-08, B-12)", () => {
  it("the cube behind a claimed cube stays unreachable", () => {
    const b = new Board(solid(1, 3), "bottom");
    b.claim(2);
    expect(b.reachableCount(0)).toBe(0);
    expect(b.isOpen(2)).toBe(false);
    b.eat(2);
    expect(b.isOpen(2)).toBe(true);
    expect(b.reachableCells(0)).toEqual([1]);
  });

  it("a sealed gap behind a claimed cube opens only on collect", () => {
    // top: color 0, middle: empty and sealed, bottom: color 0
    const b = new Board(pic(["0", ".", "0"]), "bottom");
    expect(b.isOpen(1)).toBe(false);
    b.claim(2);
    expect(b.isOpen(1)).toBe(false);
    expect(b.reachableCount(0)).toBe(0);
    expect(b.opensUp(0)).toBe(true); // the truck that is out will uncover it
    b.eat(2);
    expect(b.isOpen(1)).toBe(true);
    expect(b.reachableCells(0)).toEqual([0]);
  });

  it("opensUp is false while no truck is out, and only sees one cube deep", () => {
    const b = new Board(pic(["2", "1", "0"]), "bottom");
    expect(b.opensUp(1)).toBe(false);
    b.claim(2);
    expect(b.opensUp(1)).toBe(true);
    expect(b.opensUp(2)).toBe(false); // color 1 is still in the way
    expect(b.opensUp(0)).toBe(false); // the only color-0 cube is the claimed one itself
  });

  it("in the game: a second tick sends nothing until the first truck collects", () => {
    const g = new Game(level(solid(1, 3), [[[0, 3]]], 1));
    g.tap(0);
    expect(g.tick().map((d) => d.cell)).toEqual([2]);
    for (let k = 0; k < 5; k++) expect(g.tick()).toEqual([]);
    expect(g.slots[0]?.count).toBe(2);
    expect(g.status).toBe("playing");
    expect(g.slotBlocked(0)).toBe(false); // opensUp: the truck out will uncover its color
    g.collect(2);
    expect(g.tick().map((d) => d.cell)).toEqual([1]);
  });
});

describe("bottom / sides / all edges (B-02, B-05)", () => {
  const corners = { bl: at(3, 0, 2), tl: at(3, 0, 0), tr: at(3, 2, 0), br: at(3, 2, 2), tm: at(3, 1, 0) };

  it.each([
    ["bottom", { bl: ["bottom"], tl: [], tr: [], br: ["bottom"], tm: [] }],
    ["sides", { bl: ["bottom", "left"], tl: ["left"], tr: ["right"], br: ["bottom", "right"], tm: [] }],
    ["all", { bl: ["bottom", "left"], tl: ["left", "top"], tr: ["right", "top"], br: ["bottom", "right"], tm: ["top"] }],
  ] as const)("open sides of the corners with %s", (edges, expected) => {
    const b = new Board(solid(3, 3), edges);
    for (const [name, i] of Object.entries(corners)) expect(b.openSides(i)).toEqual(expected[name as keyof typeof corners]);
  });

  it.each([
    ["bottom", [6, 7, 8]],
    ["sides", [0, 2, 3, 5, 6, 7, 8]],
    ["all", [0, 1, 2, 3, 5, 6, 7, 8]],
  ] as const)("reachable cubes of a solid 3x3 with %s", (edges, expected) => {
    expect(sorted(new Board(solid(3, 3), edges).reachableCells(0))).toEqual(expected);
  });

  it.each([
    ["bottom", [2, 2, 2, 1, 1, 1, 0, 0, 0]],
    ["sides", [0, 1, 0, 0, 1, 0, 0, 0, 0]],
    ["all", [0, 0, 0, 0, 1, 0, 0, 0, 0]],
  ] as const)("peel layers of a solid 3x3 with %s", (edges, expected) => {
    expect([...peelLayers(solid(3, 3), edges)]).toEqual(expected);
  });

  it("a wall makes the same crate stuck at the bottom but playable from the sides or all round", () => {
    // Color 1 along the bottom row, color 0 above; the only crate is color 0.
    const p = pic(["000", "000", "111"]);
    const counts: Record<string, number> = {};
    const status: Record<string, string> = {};
    for (const edges of ["bottom", "sides", "all"] as const) {
      const g = new Game(level(p, [[[0, 6]], [[1, 3]]], 1, edges));
      counts[edges] = g.board.reachableCount(0);
      g.tap(0);
      status[edges] = g.status;
    }
    expect(counts).toEqual({ bottom: 0, sides: 4, all: 5 });
    expect(status).toEqual({ bottom: "stuck", sides: "playing", all: "playing" });
  });

  it("all: a top-row corner is driven to by the side road, which is shorter than the top road", () => {
    // Only the top row is color 0. Road to the left side of (0,0): w/2 + (h - 0.5) = 4;
    // to its top: w/2 + h + 0.5 = 5. The middle top cube only has the top road: 1.5 + 3 + 1.5 = 6.
    const b = new Board(pic(["000", "111", "111"]), "all");
    expect(sorted(b.reachableCells(0))).toEqual([0, 1, 2]);
    const order: number[] = [];
    for (let t = b.pickTarget(0); t >= 0; t = b.pickTarget(0)) {
      order.push(t);
      b.claim(t);
    }
    expect(order).toEqual([0, 2, 1]);
  });

  it("sides: the same top row has no way in to its middle cube", () => {
    const b = new Board(pic(["000", "111", "111"]), "sides");
    expect(sorted(b.reachableCells(0))).toEqual([0, 2]);
  });

  it("the bottom edge is open in every mode, and is where the ramp-side rows start", () => {
    for (const edges of ["bottom", "sides", "all"] as const) {
      const b = new Board(solid(5, 3), edges);
      expect(b.pickTarget(0)).toBe(at(5, 2, 2)); // bottom row, middle
    }
  });

  it("clearing order of a solid 5x3: bottom and sides alike, all clears the top row from the corners in", () => {
    // Rows nearest the ramp go first and each row goes middle-out. With "all", every top-row cube is on
    // an open edge (0 cells in), so it is entered from outside: the corners by the side roads (road 5),
    // the rest by the top road (7, 7, 8), instead of through the cleared row below (I-07, I-08).
    const orders: Record<string, number[]> = {};
    for (const edges of ["bottom", "sides", "all"] as const) {
      const b = new Board(solid(5, 3), edges);
      const order: number[] = [];
      for (let t = b.pickTarget(0); t >= 0; t = b.pickTarget(0)) {
        order.push(t);
        b.eat(t);
      }
      orders[edges] = order;
    }
    const lower = [12, 11, 13, 10, 14, 7, 6, 8, 5, 9];
    expect(orders).toEqual({
      bottom: [...lower, 2, 1, 3, 0, 4],
      sides: [...lower, 2, 1, 3, 0, 4],
      all: [...lower, 0, 4, 1, 3, 2],
    });
  });

  it("I-08: the way in is chosen by fewest cells in, but cubes are compared by the total drive", () => {
    // sides, 5x3. Row y=1: (0,1) side cube, (1,1) cleared, (2,1) inner cube, (4,1) side cube.
    //   00000
    //   0.000
    //   ..111      (0,2) and (1,2) are cleared bottom cells
    // (0,1): straight in from the left side is (0 in, road 4) = 4; through the cleared cell below it would be
    //        (1 in, road 2) = 3, but fewer cells in wins, so its drive counts as 4.
    // (2,1): through (1,1) is (2 in, road 1) = 3.
    // So (2,1) goes first. Comparing by the shortest total would tie (0,1) with it at 3 and the lower
    // index (0,1) would go first. Porting must keep the two rules apart.
    const b = new Board(pic(["00000", "0.000", "..111"]), "sides");
    const order: number[] = [];
    for (let k = 0; k < 3; k++) {
      const t = b.pickTarget(0);
      order.push(t);
      b.claim(t);
    }
    expect(order).toEqual([at(5, 2, 1), at(5, 0, 1), at(5, 4, 1)]);
  });
});

describe("incremental distances match a fresh computation (I-09)", () => {
  it("after every pickup on a picture with sealed gaps, open on all sides", () => {
    const p = pic(["0000000", "0.1.1.0", "0011100", "01.0.10", "0000000"]);
    const b = new Board(p, "all");
    for (let guard = 0; b.remaining > 0 && guard < 100; guard++) {
      const t = [0, 1].map((c) => b.pickTarget(c)).find((i) => i >= 0)!;
      b.eat(t);
      const fresh = freshDepths(b);
      const mine = internals(b);
      for (let i = 0; i < b.cells.length; i++) {
        if (!b.isOpen(i)) continue;
        expect([i, mine.depth[i], mine.road[i]]).toEqual([i, fresh.depth[i], fresh.road[i]]);
      }
    }
    expect(b.remaining).toBe(0);
  });
});
