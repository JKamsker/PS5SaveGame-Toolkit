import { cleanup, fireEvent, screen, within } from "@testing-library/react";
import { afterAll, afterEach, beforeAll, describe, expect, it } from "vitest";
import type { TableRow } from "../../../api/protocol";
import { field, renderTable, stubViewport, table, text } from "./tableTestUtils";

let restoreViewport: () => void;
beforeAll(() => { restoreViewport = stubViewport(); });
afterAll(() => restoreViewport());
afterEach(cleanup);

const rows: TableRow[] = [
  { key: "slot/0", cells: { name: text("Potion"), value: field("qty/0", { kind: "integer", min: "0", max: "999" }, "3") } },
  { key: "slot/127", isEmpty: true, cells: { name: text("Unknown ID 0xFFFFFFFF"), value: field("qty/127", { kind: "integer", min: "0", max: "999" }, "0") } },
  { key: "slot/255", isEmpty: true, cells: { name: text("Unknown ID -1"), value: text("0") } },
  { key: "slot/256", cells: { name: text("Unknown ID 0"), value: text("0") } },
];

describe("empty inventory row visibility and selection", () => {
  it("hides only explicit empty rows and preserves normal unknown or zero-quantity rows", () => {
    const node = { ...table(rows), emptyRows: { labelColumnKey: "name" } };
    renderTable(node);
    expect(screen.getByText("2 empty slots hidden")).toBeTruthy();
    expect(screen.getByText("2 of 4 rows")).toBeTruthy();
    expect(screen.queryByText("Unknown ID 0xFFFFFFFF")).toBeNull();
    expect(screen.getByRole("checkbox", { name: "Select Unknown ID 0" })).toBeTruthy();
    fireEvent.click(screen.getByRole("checkbox", { name: "Select all shown rows" }));
    expect(screen.getByText("2 selected")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Show empty slots" }));
    expect(screen.getAllByText("Empty slot")).toHaveLength(2);
    const empty = screen.getByRole("checkbox", { name: "Select Empty slot slot/127" });
    expect((empty as HTMLInputElement).checked).toBe(false);
    expect(empty.closest('[role="row"]')?.classList.contains("table__row--empty")).toBe(true);
    expect(screen.queryByText("Unknown ID 0xFFFFFFFF")).toBeNull();
    expect(node.rows).toBe(rows);
    expect(node.rows[1]!.cells.name).toEqual(text("Unknown ID 0xFFFFFFFF"));
  });

  it("drops hidden empty selections without changing occupied slot selections or identities", () => {
    renderTable({ ...table(rows), emptyRows: { labelColumnKey: "name" } });
    fireEvent.click(screen.getByRole("button", { name: "Show empty slots" }));
    fireEvent.click(screen.getByRole("checkbox", { name: "Select all shown rows" }));
    expect(screen.getByText("4 selected")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Hide empty slots" }));
    expect(screen.getByText("2 selected")).toBeTruthy();
    expect((screen.getByRole("checkbox", { name: "Select Potion" }) as HTMLInputElement).checked).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: "Show empty slots" }));
    expect((screen.getByRole("checkbox", { name: "Select Empty slot slot/127" }) as HTMLInputElement).checked).toBe(false);
    expect(screen.getByText("2 selected")).toBeTruthy();
  });
});

describe("empty inventory row details and filtering", () => {
  it("keeps original cells and editing available in details when a slot is shown", () => {
    const { commit } = renderTable({ ...table(rows), emptyRows: { labelColumnKey: "name", hideByDefault: false } });
    const row = screen.getByRole("checkbox", { name: "Select Empty slot slot/127" }).closest('[role="row"]') as HTMLElement;
    fireEvent.click(within(row).getByText("Empty slot"));
    expect(screen.getByText("Unknown ID 0xFFFFFFFF")).toBeTruthy();
    const quantity = within(row).getByRole("textbox", { name: /qty\/127/ });
    fireEvent.change(quantity, { target: { value: "7" } });
    fireEvent.blur(quantity);
    expect(commit).toHaveBeenCalledWith("qty/127", "7");
  });

  it("applies text filters and the hidden count before select-all", () => {
    const many = [...rows, ...Array.from({ length: 8 }, (_, i) => ({ key: `other/${i}`, cells: { name: text(`Other ${i}`) } }))];
    renderTable({ ...table(many), emptyRows: { labelColumnKey: "name", pluralLabel: "vacant slots" } });
    fireEvent.change(screen.getByRole("searchbox", { name: "Filter rows" }), { target: { value: "empty slot" } });
    expect(screen.getByText("2 vacant slots hidden")).toBeTruthy();
    expect((screen.getByRole("checkbox", { name: "Select all shown rows" }) as HTMLInputElement).disabled).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: "Show vacant slots" }));
    expect(screen.getByText("2 of 12 rows")).toBeTruthy();
    fireEvent.click(screen.getByRole("checkbox", { name: "Select all shown rows" }));
    expect(screen.getByText("2 selected")).toBeTruthy();
  });

  it("leaves legacy tables visible without empty-row metadata", () => {
    renderTable(table(rows));
    expect(screen.getByText("4 rows")).toBeTruthy();
    expect(screen.getByText("Unknown ID 0xFFFFFFFF")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Show empty slots" })).toBeNull();
  });
});
