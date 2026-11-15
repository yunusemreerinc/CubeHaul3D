/**
 * The TypeScript reader/validator against the shared, language-neutral fixtures in LevelFormat/.
 * The C# tests (Tools/dotnet/CubeHaul.Data.Tests) run the same fixtures and must give the same results.
 */
import Ajv2020 from "ajv/dist/2020";
import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { formatLevelDefinition } from "../src/format";
import { difficultyLabel } from "../src/levelDefinition";
import { readLevelDefinition, type Issue } from "../src/validate";

const DIR = join(__dirname, "../../../LevelFormat");
const read = (...p: string[]) => readFileSync(join(DIR, ...p), "utf8");
const files = (dir: string) => readdirSync(join(DIR, dir)).filter((f) => f.endsWith(".json") && f !== "expected.json").sort();
const key = (i: Pick<Issue, "code" | "path">) => `${i.code} ${i.path}`;
const keys = (xs: Pick<Issue, "code" | "path">[]) => xs.map(key).sort();

type Expected<T> = Record<string, T>;
const invalidExpected: Expected<{ errors: Issue[]; schemaRejects: boolean | null }> = JSON.parse(read("fixtures/invalid/expected.json"));
const validExpected: Expected<{ warnings: Issue[] }> = JSON.parse(read("fixtures/valid/expected.json"));

const ajv = new Ajv2020({ allErrors: true, strict: true, strictRequired: false });
const schemaCheck = ajv.compile(JSON.parse(read("level-definition.v1.schema.json")));
const parse = (text: string) => JSON.parse(text.charCodeAt(0) === 0xfeff ? text.slice(1) : text);

describe("fixture lists", () => {
  it("every fixture file has an expected result, and every expected result a file", () => {
    expect(files("fixtures/invalid")).toEqual(Object.keys(invalidExpected).sort());
    expect(files("fixtures/valid")).toEqual(Object.keys(validExpected).sort());
  });
});

describe("invalid fixtures: rejected with exactly the expected errors", () => {
  it.each(files("fixtures/invalid"))("%s", (file) => {
    const result = readLevelDefinition(read("fixtures/invalid", file));
    expect(result.ok).toBe(false);
    expect(result.level).toBeUndefined();
    expect(keys(result.errors)).toEqual(keys(invalidExpected[file].errors));
    for (const e of result.errors) expect(e.message.length).toBeGreaterThan(0);
  });
});

describe("valid fixtures and examples: accepted, with exactly the expected warnings", () => {
  it.each(files("fixtures/valid"))("fixtures/valid/%s", (file) => {
    const result = readLevelDefinition(read("fixtures/valid", file));
    expect(result.errors).toEqual([]);
    expect(result.ok).toBe(true);
    expect(keys(result.warnings)).toEqual(keys(validExpected[file].warnings));
  });

  it.each(files("examples"))("examples/%s", (file) => {
    const result = readLevelDefinition(read("examples", file));
    expect(result.errors).toEqual([]);
    expect(result.warnings).toEqual([]);
  });
});

describe("the JSON Schema agrees with the validator on structure", () => {
  it.each([...files("fixtures/valid").map((f) => `fixtures/valid/${f}`), ...files("examples").map((f) => `examples/${f}`)])(
    "accepts %s",
    (path) => {
      const ok = schemaCheck(parse(read(path)));
      expect(schemaCheck.errors ?? []).toEqual([]);
      expect(ok).toBe(true);
    },
  );

  it.each(files("fixtures/invalid").filter((f) => invalidExpected[f].schemaRejects !== null))("%s", (file) => {
    // Structural mistakes are caught by the schema too; rules between fields (lengths, totals, labels) only by the validator.
    expect(!schemaCheck(parse(read("fixtures/invalid", file)))).toBe(invalidExpected[file].schemaRejects);
  });
});

describe("canonical writer", () => {
  it.each(files("examples"))("examples/%s is in canonical form and round-trips", (file) => {
    const text = read("examples", file);
    const level = readLevelDefinition(text).level!;
    expect(formatLevelDefinition(level)).toBe(text);
    expect(JSON.parse(formatLevelDefinition(level))).toEqual(JSON.parse(text));
  });
});

describe("difficulty labels (B-25)", () => {
  it.each([
    [1, "Easy"],
    [0.9, "Easy"],
    [0.8999, "Medium"],
    [0.65, "Medium"],
    [0.6499, "Hard"],
    [0.45, "Hard"],
    [0.4499, "Expert"],
    [0, "Expert"],
  ] as const)("%s -> %s", (rate, label) => {
    expect(difficultyLabel(rate)).toBe(label);
  });
});
