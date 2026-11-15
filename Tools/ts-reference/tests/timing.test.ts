/**
 * Speed-independent logic. The rules (Game, Board) have no clock: they only see the order of
 * tap / tick / collect / addSlot calls. The speed button lives in main.ts and decides *when* those calls
 * happen; tests here use AppModel, a headless mirror of main.ts's timing (see appModel.ts).
 */
import { Game } from "@ref/core/game";
import type { Level } from "@ref/core/types";
import { builtInLevels } from "@ref/levels/art";
import { buildLevel } from "@ref/levels/generator";
import { describe, expect, it } from "vitest";
import { AppModel, play, type Event } from "./appModel";
import { level, solid, violations } from "./helpers";

const specs = builtInLevels();
const built = new Map<number, Level>();
const levelAt = (i: number) => {
  if (!built.has(i)) built.set(i, buildLevel(specs[i].picture, specs[i]).level);
  return built.get(i)!;
};
const state = (g: Game) => JSON.stringify({ c: g.columns, s: g.slots, st: g.status, b: [...g.board.cells], p: g.board.pending });

/** Replays a recorded event log on a fresh Game, with no notion of time, and checks every result. */
function replay(lv: Level, events: Event[]): Game {
  const g = new Game(lv);
  for (let k = 0; k < events.length; ) {
    const e = events[k];
    if (e.kind === "claim") {
      const group: { slot: number; cell: number }[] = [];
      while (k < events.length && events[k].kind === "claim" && events[k].t === e.t) {
        const c = events[k] as Extract<Event, { kind: "claim" }>;
        group.push({ slot: c.slot, cell: c.cell });
        k++;
      }
      const slots = new Set(group.map((d) => d.slot));
      expect(g.tick((s) => slots.has(s)).map((d) => ({ slot: d.slot, cell: d.cell }))).toEqual(group);
      continue;
    }
    if (e.kind === "tap") expect(g.tap(e.col)).toBe(e.slot);
    else if (e.kind === "collect") g.collect(e.cell);
    else g.addSlot();
    k++;
  }
  return g;
}

describe("the rules have no clock", () => {
  it("ticks with every slot held back change nothing, however many there are", () => {
    const g = new Game(level(solid(3, 2), [[[0, 6]]], 1));
    g.tap(0);
    const before = state(g);
    for (let k = 0; k < 1000; k++) expect(g.tick(() => false)).toEqual([]);
    expect(state(g)).toBe(before);
  });

  it("idle ticks between calls do not change what the next call does", () => {
    const run = (idle: number) => {
      const g = new Game(level(solid(4, 2), [[[0, 8]]], 1));
      g.tap(0);
      const claims: number[] = [];
      while (g.status === "playing") {
        for (let k = 0; k < idle; k++) g.tick(() => false);
        const sent = g.tick();
        for (let k = 0; k < idle; k++) g.tick(() => false);
        for (const d of sent) {
          claims.push(d.cell);
          g.collect(d.cell);
        }
      }
      return claims;
    };
    expect(run(7)).toEqual(run(0));
  });

  it.each([1, 2, 3] as const)("a game played at %ix replays exactly from its call order alone", (speed) => {
    for (const i of [0, 2, 6]) {
      const lv = levelAt(i);
      const m = new AppModel(lv, { speed });
      play(m);
      const g = replay(lv, m.events);
      expect(state(g)).toBe(state(m.game));
    }
  });
});

describe("the app timing keeps the rules intact at every speed", () => {
  it.each([1, 2, 3] as const)("%ix: every truck sent collects its cube exactly once, and the invariants hold", (speed) => {
    for (const i of [0, 1, 9]) {
      let frames = 0;
      const m = new AppModel(levelAt(i), { speed });
      const result = play(m, 400_000, (model) => {
        if (++frames % 30 === 0) expect(violations(model.game)).toEqual([]);
      });
      expect(result).toBe("won");
      const claims = m.events.filter((e) => e.kind === "claim").map((e) => (e as { cell: number }).cell);
      const collects = m.events.filter((e) => e.kind === "collect").map((e) => (e as { cell: number }).cell);
      expect([...collects].sort((a, b) => a - b)).toEqual([...claims].sort((a, b) => a - b));
      expect(new Set(claims).size).toBe(claims.length);
      expect(claims.length).toBe(specs[i].picture.cells.filter((c) => c >= 0).length);
    }
  });

  it("a crate's trucks wait for it to land, POP_INTERVAL / speed apart (B-32, B-33)", () => {
    // One crate of 3 on a row of 3: the first truck leaves once the flight (1/3.8 s, real time) ends;
    // the next ones follow 0.15 s of sim time apart, i.e. 0.15 / speed real seconds.
    for (const speed of [1, 2, 3] as const) {
      const m = new AppModel(level(solid(3, 1), [[[0, 3]]], 1), { speed, dt: 1 / 600 });
      m.tap(0);
      while (m.events.filter((e) => e.kind === "claim").length < 3) m.frame();
      const realTimes = m.events.filter((e) => e.kind === "claim").map((e) => e.t / speed);
      expect(realTimes[0]).toBeCloseTo(1 / 3.8, 2);
      expect(realTimes[1] - realTimes[0]).toBeCloseTo(0.15 / speed, 2);
      expect(realTimes[2] - realTimes[1]).toBeCloseTo(0.15 / speed, 2);
    }
  });
});

describe("FINDING: the speed button changes the order of the rule calls (see docs/TS_BASELINE.md)", () => {
  // main.ts multiplies a frame's delta by the speed (one 1/60 s frame = 1/20 s of sim time at 3x) while the
  // crate flight and the player's taps stay in real time. So 2x and 3x are not just a faster replay of 1x:
  // trucks are sent in a different order. The rules stay correct (above), and with this model's player the
  // outcome of every built-in level is the same at all speeds, but the exact game is not.
  const runs = specs.map((spec, i) => {
    const out = ([1, 2, 3] as const).map((speed) => {
      const m = new AppModel(levelAt(i), { speed });
      const result = play(m);
      const claims = m.events.filter((e) => e.kind === "claim").map((e) => (e as { cell: number }).cell);
      return { result, booster: m.slotBoostUsed, claims };
    });
    return [spec.picture.name, out] as const;
  });

  it.each(runs)("%s: same outcome at 1x, 2x and 3x, but trucks sent in a different order", (_name, out) => {
    expect(out[1].result).toBe(out[0].result);
    expect(out[2].result).toBe(out[0].result);
    expect(out[1].booster).toBe(out[0].booster);
    expect(out[2].booster).toBe(out[0].booster);
    expect(out[1].claims).not.toEqual(out[0].claims);
    expect(out[2].claims).not.toEqual(out[0].claims);
  });

  it("Rocket: the model player gets stuck at every speed, after a different number of cubes", () => {
    const rocket = runs[6][1];
    expect(rocket.map((r) => r.result)).toEqual(["stuck", "stuck", "stuck"]);
    expect(rocket.map((r) => r.claims.length)).toEqual([1183, 1155, 1183]);
  });

  it("at 1x the real-time crate flight makes no difference; scaling it with the speed does not remove the effect", () => {
    for (const i of [0, 4]) {
      const claims = (speed: 1 | 3, flightInSimTime: boolean) => {
        const m = new AppModel(levelAt(i), { speed, flightInSimTime });
        play(m);
        return m.events.filter((e) => e.kind === "claim").map((e) => (e as { cell: number }).cell);
      };
      expect(claims(1, true)).toEqual(claims(1, false));
      expect(claims(3, true)).not.toEqual(claims(1, true));
    }
  });
});
