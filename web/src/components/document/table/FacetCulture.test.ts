import { afterEach, expect, it, vi } from "vitest";
import type { TableNode } from "../../../api/protocol";
import { snapshotValue } from "./cellText";
import { computeFacets } from "./useTableRows";

afterEach(() => vi.restoreAllMocks());

it.each([
  ["de-DE", ["a", "ä", "z"]],
  ["sv-SE", ["a", "z", "ä"]],
] as const)("intentionally sorts human facet labels for %s", (locale, labels) => {
  const compare = String.prototype.localeCompare;
  vi.spyOn(String.prototype, "localeCompare").mockImplementation(function (this: string, other) {
    return compare.call(this, other, locale);
  });
  const table: TableNode = {
    type: "table", columns: [{ key: "name", label: "Name" }],
    rows: Array.from({ length: 9 }, (_, index) => ({ key: String(index), cells: { name: { type: "text", text: ["z", "ä", "a"][index % 3]! } } })),
  };
  expect(computeFacets(table, snapshotValue)[0]?.values).toEqual(labels);
  expect(table.rows.map(row => row.key)).toEqual(["0", "1", "2", "3", "4", "5", "6", "7", "8"]);
});
