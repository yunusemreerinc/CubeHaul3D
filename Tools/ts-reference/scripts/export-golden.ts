/**
 * Exports the built-in levels and the golden fixtures from the cube-hunt reference source.
 *
 *   npm run export:golden          write every file (idempotent: unchanged files are left alone)
 *   npm run check:golden           write nothing; exit 1 if any file is missing, different or stale
 */
import { existsSync, mkdirSync, readdirSync, readFileSync, statSync, writeFileSync, unlinkSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";
import { buildGoldenFiles, GOLDEN_DIR, LEVELS_DIR } from "./golden/golden";

const repo = join(dirname(fileURLToPath(import.meta.url)), "../../..");
const check = process.argv.includes("--check");

function listFiles(dir: string): string[] {
  const abs = join(repo, dir);
  if (!existsSync(abs)) return [];
  return readdirSync(abs).flatMap((name) => {
    const p = join(abs, name);
    if (statSync(p).isDirectory()) return listFiles(relative(repo, p));
    return name.endsWith(".json") ? [relative(repo, p).split("\\").join("/")] : [];
  });
}

const files = buildGoldenFiles();
const problems: string[] = [];
let written = 0;
for (const [path, content] of Object.entries(files)) {
  const abs = join(repo, path);
  const current = existsSync(abs) ? readFileSync(abs, "utf8") : null;
  if (current === content) continue;
  if (check) {
    problems.push(`${current === null ? "missing" : "differs"}: ${path}`);
    continue;
  }
  mkdirSync(dirname(abs), { recursive: true });
  writeFileSync(abs, content);
  written++;
}
// Files the exporter no longer produces would silently go stale.
for (const path of [...listFiles(GOLDEN_DIR), ...listFiles(LEVELS_DIR)]) {
  if (path in files) continue;
  if (check) problems.push(`stale (not generated any more): ${path}`);
  else {
    unlinkSync(join(repo, path));
    console.log(`removed stale ${path}`);
  }
}

if (check) {
  if (problems.length) {
    console.error(problems.join("\n"));
    console.error(`\n${problems.length} golden file(s) out of date. Run: npm run export:golden`);
    process.exit(1);
  }
  console.log(`all ${Object.keys(files).length} golden files are up to date`);
} else {
  console.log(`${Object.keys(files).length} golden files, ${written} written, ${Object.keys(files).length - written} unchanged`);
}
