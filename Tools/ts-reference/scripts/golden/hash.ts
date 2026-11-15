import { createHash } from "node:crypto";

/**
 * FNV-1a, 32 bit, over a sequence of 32-bit integers, each fed as 4 little-endian bytes.
 * Simple enough to reimplement exactly in C#: h = 2166136261; for each byte b: h = (h ^ b) * 16777619 (mod 2^32).
 * Written as 8 lowercase hex digits.
 */
export function fnv1a(values: Iterable<number>): string {
  let h = 0x811c9dc5;
  for (const v of values) {
    const x = v | 0;
    for (let k = 0; k < 4; k++) {
      h ^= (x >>> (8 * k)) & 0xff;
      h = Math.imul(h, 0x01000193) >>> 0;
    }
  }
  return h.toString(16).padStart(8, "0");
}

export const sha256 = (text: string) => createHash("sha256").update(text, "utf8").digest("hex");

/** sha256 of JSON.stringify(columns): `[[{"id":0,"color":1,"count":9},...],...]`, no spaces, keys in that order. */
export function columnsSha256(columns: { id: number; color: number; count: number }[][]): string {
  return sha256(JSON.stringify(columns.map((col) => col.map((k) => ({ id: k.id, color: k.color, count: k.count })))));
}
