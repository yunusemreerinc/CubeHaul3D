// Unity .meta files for everything under Assets/.
//
//   node Tools/unity/metas.mjs           create missing .meta files, delete orphaned ones
//   node Tools/unity/metas.mjs --check   change nothing; exit 1 if any are missing or orphaned
//
// New GUIDs are derived from the asset path (md5 of "CubeHaul3D:" + path), so running this on any machine gives the
// same GUIDs. Existing .meta files are never rewritten: Unity owns them once they exist, and moving an asset with its
// .meta keeps its GUID. Hidden files (".gitkeep") and "~"-suffixed folders are ignored, as Unity ignores them.
import { createHash } from "node:crypto";
import { existsSync, readdirSync, readFileSync, statSync, unlinkSync, writeFileSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";

const repo = join(dirname(fileURLToPath(import.meta.url)), "../..");
const check = process.argv.includes("--check");
const ignored = (name) => name.startsWith(".") || name.endsWith("~") || name === "cvs" || name.endsWith(".tmp");

const importer = (path, isDir) => {
  if (isDir) return "folderAsset: yes\nDefaultImporter";
  if (path.endsWith(".cs")) return "MonoImporter";
  if (path.endsWith(".asmdef")) return "AssemblyDefinitionImporter";
  if (/\.(json|txt|md|xml|csv|bytes|html|yaml)$/.test(path)) return "TextScriptImporter";
  return "DefaultImporter";
};

function metaText(path, isDir) {
  const guid = createHash("md5").update(`CubeHaul3D:${path}`).digest("hex");
  const kind = importer(path, isDir);
  const name = kind.split("\n").at(-1);
  const body =
    name === "MonoImporter"
      ? "  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
      : "  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n";
  return `fileFormatVersion: 2\nguid: ${guid}\n${kind}:\n${body}`;
}

const missing = [];
const orphans = [];
function walk(dir) {
  for (const name of readdirSync(dir).sort()) {
    if (ignored(name)) continue;
    const abs = join(dir, name);
    const rel = relative(repo, abs).split("\\").join("/");
    if (name.endsWith(".meta")) {
      if (!existsSync(abs.slice(0, -5))) orphans.push(rel);
      continue;
    }
    const isDir = statSync(abs).isDirectory();
    if (!existsSync(`${abs}.meta`)) missing.push({ rel, isDir });
    if (isDir) walk(abs);
  }
}
walk(join(repo, "Assets"));

if (check) {
  for (const m of missing) console.error(`missing: ${m.rel}.meta`);
  for (const o of orphans) console.error(`orphaned: ${o}`);
  if (missing.length || orphans.length) process.exit(1);
  console.log("every asset under Assets/ has its .meta, and no .meta is orphaned");
} else {
  for (const m of missing) writeFileSync(join(repo, `${m.rel}.meta`), metaText(m.rel, m.isDir));
  for (const o of orphans) unlinkSync(join(repo, o));
  console.log(`${missing.length} .meta created, ${orphans.length} orphaned .meta removed`);
}
