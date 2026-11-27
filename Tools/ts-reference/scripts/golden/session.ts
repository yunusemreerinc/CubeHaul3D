/**
 * Session fixtures (Task 15, G-11): whole levels played through main.ts's timing (tests/appModel.ts, a headless mirror
 * of App.update / sendTruck / drive / tapColumn / addSlot) by appModel's deterministic `play` policy, at a fixed frame
 * time. For each run: every tap, claim (Game.tick → truck sent), collect (truck reached its cube → Game.collect) and
 * +1 Slot, with the frame it happened in, and every truck's position on sampled frames. The C# GameSession, driven by
 * the same policy at the same frame time, must give exactly the same log. See docs/SESSION.md.
 */
import type { LevelDefinition } from "../../../level-format/src/levelDefinition";
import { readLevelDefinition } from "../../../level-format/src/validate";
import { AppModel, play, type Event } from "../../tests/appModel";
import { GOLDEN_DIR, header, LEVEL_IDS, levelFile, toLevel } from "./golden";
import { stringify } from "./json";

interface Run {
  level: number;
  speed: 1 | 2 | 3;
  /** Real seconds per frame. */
  fps: number;
  /** Record every truck's position every this many frames (0: never). */
  poseEvery: number;
}

const runs = (): Run[] => [
  ...LEVEL_IDS.map((_, level): Run => ({ level, speed: 1, fps: 60, poseEvery: level === 0 ? 15 : 0 })),
  { level: 0, speed: 2, fps: 60, poseEvery: 15 },
  { level: 0, speed: 3, fps: 60, poseEvery: 15 },
  { level: 0, speed: 1, fps: 30, poseEvery: 10 },
  { level: 0, speed: 1, fps: 120, poseEvery: 30 },
  { level: 0, speed: 3, fps: 144, poseEvery: 36 },
  { level: 3, speed: 3, fps: 60, poseEvery: 0 },
  { level: 6, speed: 2, fps: 60, poseEvery: 0 },
  { level: 9, speed: 2, fps: 60, poseEvery: 0 },
];

/** Event kinds in the rows: [frame delta, kind, a, b]. */
const KIND = { tap: 0, claim: 1, collect: 2, addSlot: 3 } as const;

function row(e: Event, frameDelta: number): number[] {
  switch (e.kind) {
    case "tap":
      return [frameDelta, KIND.tap, e.col, e.slot];
    case "claim":
      return [frameDelta, KIND.claim, e.slot, e.cell];
    case "collect":
      return [frameDelta, KIND.collect, e.cell, -1];
    default:
      return [frameDelta, KIND.addSlot, -1, -1];
  }
}

function runName(r: Run): string {
  return `s-${String(r.level + 1).padStart(2, "0")}-${LEVEL_IDS[r.level]}-${r.speed}x-${r.fps}fps`;
}

function record(r: Run, levelFiles: Record<string, string>) {
  const def = readLevelDefinition(levelFiles[levelFile(r.level)]).level as LevelDefinition;
  const dt = 1 / r.fps;
  const model = new AppModel(toLevel(def), { speed: r.speed, dt });
  const frameOf: number[] = [];
  const poses: { frame: number; trucks: number[]; ramp: number[][] }[] = [];
  let frames = 0;
  let maxOut = 0;
  const result = play(model, 400_000, (m) => {
    frames++;
    while (frameOf.length < m.events.length) frameOf.push(frames);
    maxOut = Math.max(maxOut, m.trucksOut());
    if (r.poseEvery > 0 && frames % r.poseEvery === 0) {
      poses.push({ frame: frames, trucks: m.carPoses().flatMap((c) => [c.cell, c.x, c.y]), ramp: m.rampQueues() });
    }
  });
  let last = 0;
  const events = model.events.map((e, k) => {
    const out = row(e, frameOf[k] - last);
    last = frameOf[k];
    return out;
  });
  const g = model.game;
  return {
    ...header(
      "session",
      "A whole level played through main.ts's timing (tests/appModel.ts) by appModel's `play` policy at a fixed frame time. " +
        "events: [frame delta, kind, a, b] with kind 0 = tap (column, slot), 1 = claim (slot, cell: a truck is sent), " +
        "2 = collect (cell: its truck arrived), 3 = +1 Slot; a tap or +1 Slot in frame f happens before that frame's update. " +
        "poses: on sampled frames, every truck on the road in update order as cell, x, y (route center line, design px), " +
        "and the cells of the trucks still queued in each ramp lane. docs/SESSION.md.",
    ),
    name: runName(r),
    levelFile: levelFile(r.level),
    speed: r.speed,
    fps: r.fps,
    dt,
    policy: "appModel.play",
    result,
    frames,
    finalStatus: g.status,
    simTime: model.simTime,
    trucks: model.events.filter((e) => e.kind === "claim").length,
    maxTrucksOut: maxOut,
    remaining: g.board.remaining,
    events,
    poses,
  };
}

export function sessionFiles(levelFiles: Record<string, string>): Record<string, string> {
  const files: Record<string, string> = {};
  for (const r of runs()) files[`${GOLDEN_DIR}/sessions/${runName(r)}.json`] = stringify(record(r, levelFiles)) + "\n";
  return files;
}
