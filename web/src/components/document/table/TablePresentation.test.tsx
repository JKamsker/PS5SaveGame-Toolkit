import { cleanup, screen } from "@testing-library/react";
import { afterAll, afterEach, beforeAll, describe, expect, it } from "vitest";
import type { FieldSpec } from "../../../api/protocol";
import { field, renderTable, stubViewport, table, text } from "./tableTestUtils";

let restoreViewport: () => void;
beforeAll(() => { restoreViewport = stubViewport(); });
afterAll(() => restoreViewport());
afterEach(cleanup);

describe("table presentation", () => {
  it.each<FieldSpec>([
    { kind: "integer", min: "0", max: "999" },
    { kind: "float" },
    { kind: "exactNumber" },
    { kind: "nullable", inner: { kind: "integer", min: "0", max: "999" } },
  ])("aligns $kind values with their column heading", spec => {
    renderTable(table([{ key: "potion", cells: { name: text("Potion"), value: field("quantity", spec, "123") } }]));
    expect(screen.getByRole("columnheader", { name: "Value" }).classList.contains("table__head--number")).toBe(true);
    expect(screen.getByRole("columnheader", { name: "Name" }).classList.contains("table__head--number")).toBe(false);
    expect(document.querySelector(".table__cell--number")).not.toBeNull();
  });

  it("aligns plain numeric text while preserving mixed and named columns", () => {
    renderTable(table([
      { key: "one", cells: { name: text("Area 1"), value: text("25"), status: text("1") } },
      { key: "two", cells: { name: text("Area 2"), value: text("38"), status: text("Unknown") } },
    ], ["name", "value", "status"]));
    expect(screen.getByRole("columnheader", { name: "Value" }).classList.contains("table__head--number")).toBe(true);
    expect(screen.getByRole("columnheader", { name: "Status" }).classList.contains("table__head--number")).toBe(false);
    expect(screen.getByText("25").closest(".table__cell--number")).not.toBeNull();
  });

  it("exposes the full clipped readonly value and heading in tooltips", () => {
    const value = "Unresolved item with a long name and source qualifier";
    renderTable(table([{ key: "one", cells: { name: text("Potion"), value: field("item", { kind: "text", readOnly: true }, value) } }]));
    expect(screen.getByText(value).title).toBe(value);
    expect(screen.getByRole("columnheader", { name: "Value" }).title).toBe("Value");
  });
});
