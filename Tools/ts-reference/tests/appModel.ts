/**
 * A headless model of the timing in cube-hunt's src/main.ts (App.update, sendTruck, drive, tapColumn,
 * addSlot), for testing how the speed button interacts with the rules. main.ts touches the DOM at load
 * time, so it cannot be imported in Node; this file mirrors only the parts that decide *when* the rules
 * are called (Game.tap / tick / collect / addSlot). Drawing, particles and UI animations are left out.
 *
 * Kept line for line with main.ts @ 559dfd9:
 * - layout: BOARD, FIELD_INSET, ROAD_GAP, TUNNEL, RAMP_LANES, cellSize(), ring, carScale()
 * - update(): slot timers (sim time), tick(ready) with the crate-flight gate, ramp lanes, cars, flyers (real time)
 * - sendTruck(): cheapest of the three ramp lanes via RoadMap.routeFrom
 * - drive(): waypoints, collect on arrival, LOAD_TIME, drive home
 */
import { Game, type Dispatch } from "@ref/core/game";
import type { Level } from "@ref/core/types";
import { RoadMap, type Pt, type Ring } from "@ref/view/roads";

export const BOARD = { x: 16, y: 64, w: 388, h: 388 };
export const FIELD_INSET = 38;
export const ROAD_GAP = 22;
export const TUNNEL = { x: 420 / 2, y: 496, w: 112, h: 36 };
export const RAMP_LANES = [-34, 0, 34];
export const CONVOY_GAP = 30;
export const CAR_SPEED = 200;
export const POP_INTERVAL = CONVOY_GAP / CAR_SPEED;
export const LOAD_TIME = 0.42;
export const FLY_SPEED = 3.8;
export const MAX_SLOTS = 6;

interface Car {
  x: number;
  y: number;
  sx: number;
  sy: number;
  path: Pt[];
  step: number;
  route: Pt[];
  cell: number;
  state: "out" | "load" | "home" | "gone";
  wait: number;
}

export type Event =
  | { t: number; kind: "tap"; col: number; slot: number }
  | { t: number; kind: "claim"; slot: number; cell: number }
  | { t: number; kind: "collect"; cell: number }
  | { t: number; kind: "addSlot" };

export interface ModelOptions {
  speed: 1 | 2 | 3;
  /** Real seconds per frame (main.ts: the rAF delta, capped at 0.05). */
  dt?: number;
  /** Mirror main.ts (crate flight in real time), or also scale the flight by the speed. */
  flightInSimTime?: boolean;
}

export class AppModel {
  readonly game: Game;
  readonly roads: RoadMap;
  readonly ring: Ring;
  readonly speed: number;
  readonly cellSize: number;
  readonly events: Event[] = [];
  /** Real time since the level started. */
  time = 0;
  /** Simulation time (real time × speed). */
  simTime = 0;
  slotBoostUsed = false;
  private slotTimer = new Array<number>(MAX_SLOTS).fill(0);
  private flyers: { slot: number; t: number }[] = [];
  private ramp: Car[][] = RAMP_LANES.map(() => []);
  private rampTimer = RAMP_LANES.map(() => 0);
  private cars: Car[] = [];
  private readonly dt: number;
  private readonly flightInSimTime: boolean;

  constructor(level: Level, opts: ModelOptions) {
    this.speed = opts.speed;
    this.dt = opts.dt ?? 1 / 60;
    this.flightInSimTime = opts.flightInSimTime ?? false;
    this.game = new Game(level);
    const p = level.picture;
    const cs = Math.min((BOARD.w - 2 * FIELD_INSET) / p.width, (BOARD.h - 2 * FIELD_INSET) / p.height);
    this.cellSize = cs;
    const fx = BOARD.x + (BOARD.w - cs * p.width) / 2;
    const fy = BOARD.y + (BOARD.h - cs * p.height) / 2;
    this.ring = { x0: fx - ROAD_GAP, y0: fy - ROAD_GAP, x1: fx + cs * p.width + ROAD_GAP, y1: fy + cs * p.height + ROAD_GAP };
    this.roads = new RoadMap(this.game.board, fx, fy, cs, this.ring, { x: TUNNEL.x, y: this.ring.y1 });
  }

  carScale(): number {
    return Math.min(1, Math.max(0.5, this.cellSize / 13));
  }

  /** Trucks on the road or waiting in the ramp. */
  trucksOut(): number {
    return this.cars.length + this.ramp.reduce((n, q) => n + q.length, 0);
  }

  /** Trucks on the road, in update order: the cell each one hauls and where it is (its route's center line). */
  carPoses(): { cell: number; x: number; y: number; state: Car["state"] }[] {
    return this.cars.map((c) => ({ cell: c.cell, x: c.x, y: c.y, state: c.state }));
  }

  /** Trucks waiting in each ramp lane's queue, oldest first, by the cell each one hauls. */
  rampQueues(): number[][] {
    return this.ramp.map((q) => q.map((c) => c.cell));
  }

  isFlying(slot: number): boolean {
    return this.flyers.some((f) => f.slot === slot);
  }

  /** main.ts tapColumn: rejected (tray shakes) when no slot is free. */
  tap(col: number): number {
    if (!this.game.front(col) || this.game.status !== "playing" || !this.game.hasFreeSlot()) return -1;
    const slot = this.game.tap(col);
    if (slot >= 0) {
      this.flyers.push({ slot, t: 0 });
      this.events.push({ t: this.simTime, kind: "tap", col, slot });
    }
    return slot;
  }

  /** main.ts addSlot: once per level, at most MAX_SLOTS. */
  addSlot(): boolean {
    if (this.slotBoostUsed || this.game.slots.length >= MAX_SLOTS) return false;
    this.slotBoostUsed = true;
    this.game.addSlot();
    this.events.push({ t: this.simTime, kind: "addSlot" });
    return true;
  }

  /** One animation frame of main.ts update(), rule-relevant parts only. */
  frame(): void {
    const dt = this.dt;
    const sdt = dt * this.speed;
    this.time += dt;
    this.simTime += sdt;
    for (let k = 0; k < MAX_SLOTS; k++) this.slotTimer[k] -= sdt;
    if (this.game.status === "playing") {
      const ready = (s: number) => this.slotTimer[s] <= 0 && !this.isFlying(s);
      for (const d of this.game.tick(ready)) {
        this.events.push({ t: this.simTime, kind: "claim", slot: d.slot, cell: d.cell });
        this.sendTruck(d);
        this.slotTimer[d.slot] = POP_INTERVAL;
      }
    }
    const spacing = (CONVOY_GAP * this.carScale()) / CAR_SPEED;
    this.ramp.forEach((queue, k) => {
      this.rampTimer[k] -= sdt;
      if (queue.length > 0 && this.rampTimer[k] <= 0) {
        this.cars.push(queue.shift()!);
        this.rampTimer[k] = spacing;
      }
    });
    for (const c of this.cars) {
      if (c.state === "load") {
        c.wait -= sdt;
        if (c.wait <= 0) {
          c.state = "home";
          c.path = [...c.route.slice(0, -1).reverse(), { x: c.sx, y: c.sy }];
          c.step = 0;
        }
        continue;
      }
      this.drive(c, CAR_SPEED * sdt);
    }
    this.cars = this.cars.filter((c) => c.state !== "gone");
    const flight = this.flightInSimTime ? sdt : dt;
    for (const f of this.flyers) f.t += flight * FLY_SPEED;
    this.flyers = this.flyers.filter((f) => f.t < 1);
  }

  private sendTruck(d: Dispatch): void {
    let lane = 0;
    let route: Pt[] = [];
    let best = Infinity;
    RAMP_LANES.forEach((dx, k) => {
      const plan = this.roads.routeFrom(d.cell, { x: TUNNEL.x + dx, y: this.ring.y1 });
      if (plan.cost < best) {
        best = plan.cost;
        lane = k;
        route = plan.path;
      }
    });
    const sx = TUNNEL.x + RAMP_LANES[lane];
    const sy = TUNNEL.y + TUNNEL.h * 0.2;
    this.ramp[lane].push({ x: sx, y: sy, sx, sy, path: route, step: 0, route, cell: d.cell, state: "out", wait: 0 });
  }

  private drive(c: Car, dist: number): void {
    while (dist > 0 && c.step < c.path.length) {
      const t = c.path[c.step];
      const d = Math.hypot(t.x - c.x, t.y - c.y);
      if (d > dist) {
        c.x += ((t.x - c.x) / d) * dist;
        c.y += ((t.y - c.y) / d) * dist;
        return;
      }
      c.x = t.x;
      c.y = t.y;
      dist -= d;
      c.step++;
    }
    if (c.step < c.path.length) return;
    if (c.state === "out") {
      this.game.collect(c.cell);
      this.events.push({ t: this.simTime, kind: "collect", cell: c.cell });
      c.state = "load";
      c.wait = LOAD_TIME;
    } else {
      c.state = "gone";
    }
  }
}

/**
 * A simple, deterministic player: each frame it taps at most one crate, the first column whose front color
 * can eat now or will be uncovered by a truck already out; failing that, when nothing is moving, the first
 * non-empty column. When stuck it uses +1 Slot if it still can. Returns when the level is won or lost.
 */
export function play(model: AppModel, maxFrames = 400_000, onFrame?: (m: AppModel) => void): "won" | "stuck" | "timeout" {
  const g = model.game;
  for (let f = 0; f < maxFrames; f++) {
    if (g.status === "won" && model.trucksOut() === 0) return "won";
    if (g.status === "stuck" && !model.addSlot()) return "stuck";
    if (g.status === "playing" && g.hasFreeSlot()) {
      let col = g.columns.findIndex((c) => c.length > 0 && (g.board.reachableCount(c[0].color) > 0 || g.board.opensUp(c[0].color)));
      if (col < 0 && model.trucksOut() === 0 && !g.slots.some((_, s) => g.slotCanEat(s))) col = g.columns.findIndex((c) => c.length > 0);
      if (col >= 0) model.tap(col);
    }
    model.frame();
    onFrame?.(model);
  }
  return "timeout";
}
