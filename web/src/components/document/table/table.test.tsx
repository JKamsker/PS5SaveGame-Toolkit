import { act, renderHook } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { EnumSpec, FieldNode, FlagsSpec, TableRow } from "../../../api/protocol";
import { cellSearchText, facetValue, rowLabel, setFlagLabels, snapshotValue } from "./cellText";
import { useRowSelection } from "./selection";
import { field, table, text } from "./tableTestUtils";
import { computeFacets, filterRows } from "./useTableRows";

const flags: FlagsSpec = { kind: "flags", bitWidth: 8, bits: [{ bit: 0, label: "Poisoned" }, { bit: 3, label: "Cursed" }] };
const rarity: EnumSpec = { kind: "enum", options: [{ value: "4", label: "Legendary" }] };

describe("cell text", () => {
  it("names set flag bits and tolerates garbage", () => {
    expect(setFlagLabels(flags, "9")).toEqual(["Poisoned", "Cursed"]);
    expect(setFlagLabels(flags, "not a number")).toEqual([]);
  });

  it("searches the live value through the resolver", () => {
    const node = field("x", rarity, "1");
    const live = (n: FieldNode) => (n.valueId === "x" ? "4" : n.value);
    expect(cellSearchText(node, snapshotValue)).not.toContain("Legendary");
    expect(cellSearchText(node, live)).toContain("Legendary");
  });

  it("only treats text and enum/bool cells as categories", () => {
    expect(facetValue(text("Weapon"), snapshotValue)).toBe("Weapon");
    expect(facetValue(field("b", { kind: "bool" }, true), snapshotValue)).toBe("Yes");
    expect(facetValue(field("n", { kind: "integer", min: "0", max: "9" }, "1"), snapshotValue)).toBeNull();
  });

  it("labels a row by its first text cell", () => {
    const row: TableRow = { key: "r", cells: { a: field("a", { kind: "bool" }, true), b: text("Sword") } };
    expect(rowLabel(row, ["a", "b"], snapshotValue)).toBe("Sword");
    expect(rowLabel({ key: "r9", cells: {} }, ["a"], snapshotValue)).toBe("r9");
  });
});

describe("filters", () => {
  const rows: TableRow[] = Array.from({ length: 10 }, (_, i) => ({
    key: `r${i}`,
    cells: { name: text(`Item ${i}`), kind: text(i < 5 ? "A" : "B"), qty: field(`q${i}`, { kind: "integer", min: "0", max: "99" }, String(i)) },
  }));

  it("derives facets only for categorical columns", () => {
    const facets = computeFacets(table(rows, ["name", "kind", "qty"]), snapshotValue);
    expect(facets.map((f) => f.key)).toEqual(["kind"]);
    expect(facets[0]?.values).toEqual(["A", "B"]);
  });

  it("combines query and facets and returns the input when inactive", () => {
    expect(filterRows(rows, "", {}, snapshotValue)).toBe(rows);
    expect(filterRows(rows, "item", { kind: "B" }, snapshotValue)).toHaveLength(5);
    expect(filterRows(rows, "item 7", { kind: "A" }, snapshotValue)).toHaveLength(0);
  });
});

describe("useRowSelection", () => {
  const rows: TableRow[] = ["a", "b", "c"].map((key) => ({ key, cells: {} }));

  it("toggles, selects many, clears and ignores rows that disappeared", () => {
    const { result, rerender } = renderHook(({ r }) => useRowSelection(r), { initialProps: { r: rows } });
    act(() => result.current.toggle("b"));
    expect(result.current.isSelected("b")).toBe(true);
    act(() => result.current.setMany(["a", "c"], true));
    expect(result.current.selectedRows.map((r) => r.key)).toEqual(["a", "b", "c"]);

    rerender({ r: rows.slice(0, 2) });
    expect(result.current.count).toBe(2);
    expect(result.current.isSelected("c")).toBe(false);

    act(() => result.current.clear());
    expect(result.current.count).toBe(0);
  });
});
