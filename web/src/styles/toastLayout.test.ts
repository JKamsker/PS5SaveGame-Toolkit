// @vitest-environment node
import { readFileSync } from "node:fs";
import { expect, it } from "vitest";

const css = readFileSync("src/styles/ui.css", "utf8");

it("keeps flooded notifications at their readable height and wraps long diagnostics", () => {
  const toast = /\.toast\s*\{([^}]+)\}/.exec(css)![1]!;
  expect(toast).toMatch(/flex-shrink:\s*0/);
  expect(toast).toMatch(/overflow-wrap:\s*anywhere/);
});
