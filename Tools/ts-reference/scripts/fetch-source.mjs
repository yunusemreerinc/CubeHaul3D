// Checks out the Cube Hunt TypeScript source at the pinned commit into vendor/ (git-ignored).
// The checkout is never modified: the reference tests only import from it.
import { execFileSync } from "node:child_process";
import { existsSync, mkdirSync, readFileSync, rmSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const { repo, commit, dir } = JSON.parse(readFileSync(join(root, "source.json"), "utf8"));
const target = join(root, dir);
const git = (...args) => execFileSync("git", ["-C", target, ...args], { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }).trim();

function current() {
  try {
    return git("rev-parse", "HEAD");
  } catch {
    return null;
  }
}

if (current() !== commit) {
  rmSync(target, { recursive: true, force: true });
  mkdirSync(target, { recursive: true });
  git("init", "-q");
  git("remote", "add", "origin", repo);
  execFileSync("git", ["-C", target, "fetch", "-q", "--depth", "1", "origin", commit], { stdio: "inherit" });
  git("checkout", "-q", "FETCH_HEAD");
}
if (current() !== commit) throw new Error(`expected ${commit} in ${target}, found ${current()}`);
const dirty = git("status", "--porcelain");
if (dirty) throw new Error(`the reference checkout has local changes; it must stay untouched:\n${dirty}`);
if (!existsSync(join(target, "src/core/game.ts"))) throw new Error("unexpected source layout");
console.log(`cube-hunt @ ${commit.slice(0, 7)} ready in ${dir}`);
