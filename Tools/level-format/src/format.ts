/**
 * Writes a LevelDefinition as canonical, diff-friendly JSON: two-space indents, keys in schema order,
 * the picture one row of cells per line, and one crate per line. The output is plain JSON, so any
 * reader (JSON.parse, the C# reader) sees the same values; the layout only helps people and diffs.
 */
import type { LevelDefinition } from "./levelDefinition";

const ROOT_ORDER = ["$schema", "schemaVersion", "levelId", "themeId", "name", "picture", "edges", "slotCount", "tray", "generation", "difficultyMetadata", "extensions"];
const GENERATION_ORDER = ["seed", "shuffle", "targetWinRate", "columns", "bigChance", "minBlock", "maxBlock", "candidates"];
const DIFFICULTY_ORDER = ["winRate", "label", "randomGames", "greedy"];

const inline = (v: unknown) => JSON.stringify(v).replace(/,/g, ", ").replace(/:/g, ": ");
const ordered = (o: Record<string, unknown>, order: string[]) =>
  [...order.filter((k) => k in o), ...Object.keys(o).filter((k) => !order.includes(k))];

export function formatLevelDefinition(level: LevelDefinition): string {
  const root = level as unknown as Record<string, unknown>;
  const lines: string[] = [];
  const keys = ordered(root, ROOT_ORDER).filter((k) => root[k] !== undefined);
  keys.forEach((key, i) => {
    const comma = i < keys.length - 1 ? "," : "";
    const v = root[key];
    if (key === "picture") {
      const p = level.picture;
      const rows: string[] = [];
      for (let y = 0; y < p.height; y++) rows.push(`      ${p.cells.slice(y * p.width, (y + 1) * p.width).map((c) => String(c).padStart(2)).join(",")}`);
      lines.push(`  "picture": {`);
      lines.push(`    "width": ${p.width},`);
      lines.push(`    "height": ${p.height},`);
      lines.push(`    "palette": ${inline(p.palette)},`);
      lines.push(`    "cells": [`);
      lines.push(rows.join(",\n"));
      lines.push(`    ]`);
      lines.push(`  }${comma}`);
    } else if (key === "tray" && level.tray) {
      const cols = level.tray.columns;
      lines.push(`  "tray": {`);
      lines.push(`    "columns": [`);
      cols.forEach((col, c) => {
        const cc = c < cols.length - 1 ? "," : "";
        if (col.length === 0) {
          lines.push(`      []${cc}`);
          return;
        }
        lines.push(`      [`);
        col.forEach((k, j) => lines.push(`        { "id": ${k.id}, "color": ${k.color}, "count": ${k.count} }${j < col.length - 1 ? "," : ""}`));
        lines.push(`      ]${cc}`);
      });
      lines.push(`    ]`);
      lines.push(`  }${comma}`);
    } else if ((key === "generation" || key === "difficultyMetadata") && typeof v === "object" && v) {
      const o = v as Record<string, unknown>;
      const ks = ordered(o, key === "generation" ? GENERATION_ORDER : DIFFICULTY_ORDER).filter((k) => o[k] !== undefined);
      lines.push(`  "${key}": {`);
      ks.forEach((k, j) => lines.push(`    ${JSON.stringify(k)}: ${inline(o[k])}${j < ks.length - 1 ? "," : ""}`));
      lines.push(`  }${comma}`);
    } else {
      lines.push(`  ${JSON.stringify(key)}: ${key === "extensions" ? JSON.stringify(v, null, 2).replace(/\n/g, "\n  ") : JSON.stringify(v)}${comma}`);
    }
  });
  return `{\n${lines.join("\n")}\n}\n`;
}
