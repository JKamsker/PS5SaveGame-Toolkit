import { afterEach, expect, it, vi } from "vitest";
import { graphLayout } from "./graphLayout";
import { graph } from "./spatialTestUtils";

afterEach(() => vi.restoreAllMocks());

it("keeps graph positions identical across German and Swedish collations", () => {
  const compare = String.prototype.localeCompare;
  const diagram = { ...graph, edges: [], items: ["z", "ä", "a"].map(branch => ({ id: branch, label: branch, branch, state: "Unknown" as const })) };
  vi.spyOn(String.prototype, "localeCompare").mockImplementation(function (this: string, other) {
    return compare.call(this, other, "de-DE");
  });
  const german = graphLayout(diagram);
  vi.spyOn(String.prototype, "localeCompare").mockImplementation(function (this: string, other) {
    return compare.call(this, other, "sv-SE");
  });
  expect(graphLayout(diagram)).toEqual(german);
});
