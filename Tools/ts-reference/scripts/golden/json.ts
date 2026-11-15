/**
 * Deterministic JSON text for golden files: two-space indents, object keys in insertion order, and arrays of
 * plain values (numbers, strings, booleans, null) on one line, so files stay compact and diff well.
 * The output is ordinary JSON; any reader sees the same values.
 */
type Json = null | boolean | number | string | Json[] | { [k: string]: Json };

type Plain = null | boolean | number | string;
const isPlain = (v: unknown): v is Plain => v === null || typeof v !== "object";

export function stringify(value: unknown, indent = ""): string {
  const v = value as Json;
  if (isPlain(v)) {
    if (typeof v === "number" && !Number.isFinite(v)) throw new Error(`cannot write ${v} as JSON`);
    return JSON.stringify(v);
  }
  const inner = indent + "  ";
  if (Array.isArray(v)) {
    if (v.length === 0) return "[]";
    if (v.every(isPlain)) return `[${v.map((x) => JSON.stringify(x)).join(", ")}]`;
    return `[\n${v.map((x) => inner + stringify(x, inner)).join(",\n")}\n${indent}]`;
  }
  const o = v as { [k: string]: Json | undefined };
  const keys = Object.keys(o).filter((k) => o[k] !== undefined);
  if (keys.length === 0) return "{}";
  return `{\n${keys.map((k) => `${inner}${JSON.stringify(k)}: ${stringify(o[k], inner)}`).join(",\n")}\n${indent}}`;
}

export const toFile = (value: unknown) => stringify(value) + "\n";
