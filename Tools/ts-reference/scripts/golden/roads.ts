/**
 * Road fixtures (Task 13): truck routes from cube-hunt's RoadMap (src/view/roads.ts) with main.ts's geometry (ring,
 * tunnel, ramp lanes), on the ten levels at several board states and on small hand-made boards. For each target cell:
 * the cost of every ramp lane, the lane sendTruck picks (strictly cheapest, first on ties) and its full route.
 * The C# RoadPathPlanner must give the same numbers. See docs/ROADS.md.
 */
import { Board } from "../../vendor/cube-hunt/src/core/board";
import type { OpenEdges, Picture } from "../../vendor/cube-hunt/src/core/types";
import { RoadMap } from "../../vendor/cube-hunt/src/view/roads";
import type { LevelDefinition } from "../../../level-format/src/levelDefinition";
import { readLevelDefinition } from "../../../level-format/src/validate";
import { BOARD, FIELD_INSET, RAMP_LANES, ROAD_GAP, TUNNEL } from "../../tests/appModel";
import { GOLDEN_DIR, header, levelFile } from "./golden";
import { stringify } from "./json";

interface RoadCase {
  name: string;
  description: string;
  picture: Picture;
  edges: OpenEdges;
  levelFile?: string;
  inline?: { width: number; height: number; cells: number[] };
  /** Peel rounds before recording: each round eats every color's pickTarget. */
  rounds: number;
  /** Extra cells to eat (in order) after the rounds, then claim pickTarget of every color if `claim`. */
  eat?: number[];
  claim?: boolean;
  /** Record at most this many targets (evenly spread over the reachable cells), or all. */
  sample?: number;
}

/** main.ts startLevel geometry: cell size, field origin, ring and entry, for a picture. */
export function geometry(width: number, height: number) {
  const cs = Math.min((BOARD.w - 2 * FIELD_INSET) / width, (BOARD.h - 2 * FIELD_INSET) / height);
  const fx = BOARD.x + (BOARD.w - cs * width) / 2;
  const fy = BOARD.y + (BOARD.h - cs * height) / 2;
  const ring = { x0: fx - ROAD_GAP, y0: fy - ROAD_GAP, x1: fx + cs * width + ROAD_GAP, y1: fy + cs * height + ROAD_GAP };
  return { cs, fx, fy, ring };
}

const pic = (rows: string[]): Picture => {
  const cells = Int16Array.from(rows.flatMap((r) => [...r].map((c) => (c === "." ? -1 : Number(c)))));
  let m = 0;
  for (const c of cells) m = Math.max(m, c);
  return { name: "case", width: rows[0].length, height: rows.length, cells, palette: Array.from({ length: m + 1 }, () => "#000000") };
};

function cases(levelFiles: Record<string, string>): RoadCase[] {
  const out: RoadCase[] = [];
  for (let i = 0; i < 10; i++) {
    const def = readLevelDefinition(levelFiles[levelFile(i)]).level as LevelDefinition;
    const picture: Picture = { name: def.levelId, width: def.picture.width, height: def.picture.height, cells: Int16Array.from(def.picture.cells), palette: [...def.picture.palette] };
    for (const rounds of [0, 6, 18]) {
      out.push({ name: `${def.levelId}-r${rounds}`, description: `${def.name}, ${def.edges} edges, after ${rounds} peel rounds`, picture, edges: def.edges, levelFile: levelFile(i), rounds, sample: 24 });
    }
    out.push({ name: `${def.levelId}-r12-claimed`, description: `${def.name} after 12 rounds, every color's next cube claimed (claimed cubes are not driven through)`, picture, edges: def.edges, levelFile: levelFile(i), rounds: 12, claim: true, sample: 24 });
  }
  // Other edge modes on two levels.
  for (const [i, edges] of [[0, "bottom"], [0, "sides"], [4, "all"], [4, "sides"]] as const) {
    const def = readLevelDefinition(levelFiles[levelFile(i)]).level as LevelDefinition;
    const picture: Picture = { name: def.levelId, width: def.picture.width, height: def.picture.height, cells: Int16Array.from(def.picture.cells), palette: [...def.picture.palette] };
    out.push({ name: `${def.levelId}-${edges}-r8`, description: `${def.name} with ${edges} edges, after 8 peel rounds`, picture, edges, levelFile: levelFile(i), rounds: 8, sample: 24 });
  }
  const small = (name: string, description: string, rows: string[], edges: OpenEdges, eat: number[] = [], rounds = 0): RoadCase => {
    const picture = pic(rows);
    return { name, description, picture, edges, inline: { width: picture.width, height: picture.height, cells: [...picture.cells] }, rounds, eat };
  };
  out.push(small("solid5-bottom", "5x5 solid, bottom edge only", ["00000", "00000", "00000", "00000", "00000"], "bottom"));
  out.push(small("solid5-sides", "5x5 solid, bottom and sides", ["00000", "00000", "00000", "00000", "00000"], "sides"));
  out.push(small("solid6-all", "6x6 solid, all edges (corners reached round the road)", ["000000", "000000", "000000", "000000", "000000", "000000"], "all"));
  out.push(small("corridor5-bottom", "5x5, a corridor cleared up the middle column (roads.test.ts)", ["00000", "00000", "00000", "00000", "00000"], "bottom", [22, 17, 12, 11]));
  out.push(small("sealed-gap-bottom", "A sealed transparent gap opens only once trucks reach it", ["00000", "0...0", "0...0", "00000", "00000"], "bottom", [22, 17]));
  out.push(small("u-turn-sides", "7x5, sides open: a U-shaped clearing entered from the right", ["0000000", "0000000", "0000000", "0000000", "0000000"], "sides", [34, 27, 20, 13, 12, 11, 10]));
  out.push(small("ties-all", "7x7 all edges, symmetric clearing: equal-cost ways in (first wins)", ["0000000", "0000000", "0000000", "0000000", "0000000", "0000000", "0000000"], "all", [], 2));
  out.push(small("wide-row-bottom", "15x2, bottom edge: lanes left and right of the tunnel matter", ["000000000000000", "000000000000000"], "bottom"));
  // Closed edges that would be the short way in: a corridor up the left side and along the top of a wide, low board,
  // so driving round the road to the top or the right side would be far shorter than the corridor. Only open edges
  // may be used; for the top-right cube, a closed top or right edge is the tempting shortcut.
  const wide3 = ["000000000", "000000000", "000000000"];
  const upLeftThenTop = [18, 9, 0, 1, 2, 3, 4, 5, 6, 7];
  out.push(small("top-corridor-bottom", "9x3, bottom edge only: a corridor up the left and along the top; the closed top and right would be shorter", wide3, "bottom", upLeftThenTop));
  out.push(small("top-corridor-sides", "9x3, bottom and sides: the same corridor; the closed top would be shorter for the top row", wide3, "sides", upLeftThenTop));
  const wide5 = ["000000000", "000000000", "000000000", "000000000", "000000000"];
  const upMiddleThenLeft = [40, 31, 22, 13, 4, 3, 2, 1, 0, 9, 18, 27];
  out.push(small("closed-sides-bottom", "9x5, bottom edge only: a corridor up the middle, left along the top, down the left side; the closed left would be shorter", wide5, "bottom", upMiddleThenLeft));
  out.push(small("tall-column-all", "1x9, all edges: a single column reached from top or bottom", ["0", "0", "0", "0", "0", "0", "0", "0", "0"], "all", [], 1));
  return out;
}

function record(c: RoadCase) {
  const board = new Board(c.picture, c.edges);
  const colors = c.picture.palette.length;
  const eaten: number[] = [];
  for (let r = 0; r < c.rounds; r++) {
    for (let k = 0; k < colors; k++) {
      const t = board.pickTarget(k);
      if (t < 0) continue;
      board.eat(t);
      eaten.push(t);
    }
  }
  for (const t of c.eat ?? []) {
    board.eat(t);
    eaten.push(t);
  }
  const claimed: number[] = [];
  if (c.claim) {
    for (let k = 0; k < colors; k++) {
      const t = board.pickTarget(k);
      if (t < 0) continue;
      board.claim(t);
      claimed.push(t);
    }
  }
  const { cs, fx, fy, ring } = geometry(c.picture.width, c.picture.height);
  const roads = new RoadMap(board, fx, fy, cs, ring, { x: TUNNEL.x, y: ring.y1 });
  const reachable = Array.from({ length: colors }, (_, k) => board.reachableCells(k)).flat().sort((a, b) => a - b);
  let targets = reachable;
  if (c.sample && reachable.length > c.sample) {
    targets = Array.from({ length: c.sample }, (_, k) => reachable[Math.floor((k * reachable.length) / c.sample!)]);
  }
  return {
    name: c.name,
    description: c.description,
    ...(c.levelFile ? { levelFile: c.levelFile } : { picture: c.inline }),
    edges: c.edges,
    eaten,
    claimed,
    geometry: { cellSize: cs, fieldX: fx, fieldY: fy, ring: [ring.x0, ring.y0, ring.x1, ring.y1] },
    reachableCount: reachable.length,
    targets: targets.map((cell) => {
      const plans = RAMP_LANES.map((dx) => roads.routeFrom(cell, { x: TUNNEL.x + dx, y: ring.y1 }));
      let lane = 0;
      plans.forEach((p, k) => {
        if (p.cost < plans[lane].cost) lane = k;
      });
      return {
        cell,
        costs: plans.map((p) => p.cost),
        lane,
        route: plans[lane].path.flatMap((p) => [p.x, p.y]),
      };
    }),
  };
}

export function roadsFile(levelFiles: Record<string, string>): Record<string, string> {
  const doc = {
    ...header(
      "roads",
      "Truck routes of cube-hunt's RoadMap with main.ts geometry. For each target: costs = routeFrom cost from each ramp lane " +
        "(TUNNEL.x + RAMP_LANES[k], ring.y1); lane = the one sendTruck picks; route = that lane's path as x0, y0, x1, y1, ... " +
        "(the lane entry first, the parking point last). Board state: eaten cells in order, then claimed cells. docs/ROADS.md.",
    ),
    tunnel: { x: TUNNEL.x, y: TUNNEL.y, w: TUNNEL.w, h: TUNNEL.h, startY: TUNNEL.y + TUNNEL.h * 0.2 },
    rampLanes: RAMP_LANES,
    roadGap: ROAD_GAP,
    cases: cases(levelFiles).map(record),
  };
  return { [`${GOLDEN_DIR}/roads.json`]: stringify(doc) + "\n" };
}
