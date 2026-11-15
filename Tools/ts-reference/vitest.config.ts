import { fileURLToPath } from "node:url";
import { defineConfig } from "vitest/config";

export default defineConfig({
  resolve: {
    alias: { "@ref": fileURLToPath(new URL("./vendor/cube-hunt/src", import.meta.url)) },
  },
  test: {
    // Our tests, plus the source's own tests run unchanged from the pinned checkout.
    include: ["tests/**/*.test.ts", "vendor/cube-hunt/tests/**/*.test.ts"],
    testTimeout: 120_000,
  },
});
