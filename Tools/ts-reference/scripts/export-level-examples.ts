/**
 * Writes the example LevelDefinition files in LevelFormat/examples from the cube-hunt reference source.
 * Run: npm run export:examples. tests/levelFormat.test.ts checks the committed files still match.
 */
import { mkdirSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { builtInLevels } from "../vendor/cube-hunt/src/levels/art";
import { buildLevel } from "../vendor/cube-hunt/src/levels/generator";
import { solveGreedy } from "../vendor/cube-hunt/src/levels/solver";
import { formatLevelDefinition } from "../../level-format/src/format";
import { examples } from "./level-examples";

const out = join(dirname(fileURLToPath(import.meta.url)), "../../../LevelFormat/examples");
mkdirSync(out, { recursive: true });
for (const [file, level] of Object.entries(examples({ builtInLevels, buildLevel, solveGreedy }))) {
  writeFileSync(join(out, file), formatLevelDefinition(level));
  console.log(`wrote LevelFormat/examples/${file}`);
}
