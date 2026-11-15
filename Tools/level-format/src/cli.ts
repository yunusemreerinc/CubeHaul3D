#!/usr/bin/env -S npx tsx
/** Validates LevelDefinition files: `npm run validate -- path/to/level.json ...`. Exit code 1 on any error. */
import { readFileSync } from "node:fs";
import { readLevelDefinition } from "./validate";

let failed = false;
const files = process.argv.slice(2);
if (files.length === 0) {
  console.error("usage: validate-level <file.json>...");
  process.exit(2);
}
for (const file of files) {
  const result = readLevelDefinition(readFileSync(file, "utf8"));
  console.log(`${result.ok ? "OK  " : "FAIL"} ${file}`);
  for (const e of result.errors) console.log(`  error   ${e.code} ${e.path || "(root)"}: ${e.message}`);
  for (const w of result.warnings) console.log(`  warning ${w.code} ${w.path || "(root)"}: ${w.message}`);
  if (!result.ok) failed = true;
}
process.exit(failed ? 1 : 0);
