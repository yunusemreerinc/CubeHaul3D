/**
 * Seeded random play on every built-in level, checking the invariants of docs/PORTING_PLAN.md §4 after
 * every single call: random taps, ticks with random slots held back, cubes collected in random order
 * (trucks arriving out of order), and +1 Slot when stuck.
 */
import { Game } from "@ref/core/game";
import { builtInLevels } from "@ref/levels/art";
import { buildLevel } from "@ref/levels/generator";
import { mulberry32 } from "@ref/levels/rng";
import { describe, expect, it } from "vitest";
import { freshDepths, internals, violations } from "./helpers";

const OPS = 2500;

describe("invariants under random play", () => {
  const specs = builtInLevels();
  it.each(specs.map((s, i) => [s.picture.name, i] as const))("%s", (_name, i) => {
    const lv = buildLevel(specs[i].picture, specs[i]).level;
    for (const gameSeed of [1, 2]) {
      const rng = mulberry32(1000 * (i + 1) + gameSeed);
      const g = new Game(lv);
      const out: number[] = [];
      for (let op = 0; op < OPS && g.status !== "won"; op++) {
        const r = rng();
        if (g.status === "stuck") {
          if (g.slots.length >= 6) break;
          g.addSlot();
        } else if (r < 0.25 && g.hasFreeSlot()) {
          const cols = g.columns.flatMap((c, k) => (c.length ? [k] : []));
          if (cols.length) {
            const free = g.slots.indexOf(null);
            expect(g.tap(cols[Math.floor(rng() * cols.length)])).toBe(free); // I-12
          }
        } else if (r < 0.6) {
          const mask = g.slots.map(() => rng() < 0.7);
          const sent = g.tick((s) => mask[s]);
          const slots = sent.map((d) => d.slot);
          expect(slots).toEqual([...slots].sort((a, b) => a - b)); // I-13
          expect(new Set(slots).size).toBe(slots.length);
          expect(new Set(sent.map((d) => d.cell)).size).toBe(sent.length);
          for (const d of sent) {
            expect(mask[d.slot]).toBe(true);
            out.push(d.cell);
          }
        } else if (out.length) {
          g.collect(out.splice(Math.floor(rng() * out.length), 1)[0]);
        } else {
          g.tick();
        }
        expect(violations(g)).toEqual([]);
        if (op % 100 === 0) {
          // I-09: incremental depths equal a fresh computation.
          const fresh = freshDepths(g.board);
          const mine = internals(g.board);
          for (let c = 0; c < g.board.cells.length; c++) {
            if (!g.board.isOpen(c)) continue;
            expect(mine.depth[c]).toBe(fresh.depth[c]);
            expect(mine.road[c]).toBe(fresh.road[c]);
          }
        }
      }
      while (out.length) {
        g.collect(out.shift()!);
        expect(violations(g)).toEqual([]);
      }
    }
  });
});
