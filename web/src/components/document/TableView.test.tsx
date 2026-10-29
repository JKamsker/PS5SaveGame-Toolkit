import { act, cleanup, fireEvent, screen, within } from "@testing-library/react";
import { afterAll, afterEach, beforeAll, describe, expect, it } from "vitest";
import type { EnumSpec, FlagsSpec, IntegerSpec, TableRow, ViewNode } from "../../api/protocol";
import { field, renderTable, stubViewport, table, text } from "./table/tableTestUtils";

const count: IntegerSpec = { kind: "integer", min: "0", max: "999" };
const rarity: EnumSpec = {
  kind: "enum",
  options: [
    { value: "1", label: "Common" },
    { value: "4", label: "Legendary" },
  ],
};
const status: FlagsSpec = { kind: "flags", bitWidth: 8, bits: [{ bit: 0, label: "Poisoned" }, { bit: 2, label: "Burning" }] };

const names = ["Potion", "Ether", "Elixir", "Phoenix Down", "Antidote", "Echo Herbs", "Hi-Potion", "Remedy", "Tent", "Megalixir"];

function inventory(): TableRow[] {
  return names.map((name, i) => ({
    key: `item${i}`,
    cells: { name: text(name), value: field(`count${i}`, count, String(i + 1)) },
  }));
}

let restoreViewport: () => void;
beforeAll(() => {
  restoreViewport = stubViewport();
});
afterAll(() => restoreViewport());
afterEach(cleanup);

const rowCount = () => screen.getByText(/rows$/).textContent;

describe("TableView cells", () => {
  it("renders every node type and a visible placeholder for unknown ones", () => {
    const cells: Record<string, ViewNode> = {
      name: text("Potion"),
      icon: { type: "image", assetId: "icons/potion", alt: "Potion icon" },
      sell: { type: "action", actionId: "sell", label: "Sell", description: "Sell for gold" },
      stats: { type: "stats", items: [{ label: "Level", value: "12" }] },
      more: { type: "section", title: "Raw record", children: [], lazyId: "raw:1" },
      bytes: { type: "hex", blobId: "b1", length: 64 },
      odd: { type: "futureNode" } as unknown as ViewNode,
    };
    renderTable(table([{ key: "r1", cells }], Object.keys(cells)));

    const img = screen.getByRole("img", { name: "Potion icon" });
    expect(img.getAttribute("src")).toContain("icons%2Fpotion");
    expect(screen.getByRole("button", { name: "Sell: Potion" }).getAttribute("title")).toBe("Sell for gold");
    expect(screen.getByText("Level 12")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Show Raw record for Potion" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Show 64 bytes for Potion" })).toBeTruthy();
    expect(screen.getByText("Unsupported: futureNode")).toBeTruthy();
  });

  it("summarises flags in one line instead of a checkbox group", () => {
    renderTable(table([{ key: "r1", cells: { name: text("Hero"), value: field("st", status, "5") } }]));
    expect(screen.getByRole("button", { name: "Edit st (Hero): Poisoned, Burning" })).toBeTruthy();
    expect(screen.queryByRole("checkbox", { name: "Poisoned" })).toBeNull();
  });

  it("shows confidence, description, warning and errors with accessible names", () => {
    const cell = field("c1", count, "3", { label: "Count", confidence: "Inferred", description: "Units held", warning: "Above 99 may crash" });
    const { store } = renderTable(table([{ key: "r1", cells: { name: text("Potion"), value: cell } }]));

    expect(screen.getByRole("img", { name: "Confidence: inferred" })).toBeTruthy();
    expect(screen.getByRole("img", { name: "Description: Units held" })).toBeTruthy();
    expect(screen.getByRole("img", { name: "Warning: Above 99 may crash" })).toBeTruthy();
    expect(screen.getByRole("textbox", { name: "Count (Potion)" })).toBeTruthy();

    act(() => store.setError("c1", "Value rejected by plugin"));
    expect(screen.getByRole("img", { name: "Error: Value rejected by plugin" })).toBeTruthy();
    expect(screen.getByRole("alert").textContent).toBe("Value rejected by plugin");
  });

  it("opens a detail row with the full field editor", () => {
    const cell = field("c1", count, "3", { label: "Count", description: "Units held" });
    renderTable(table([{ key: "r1", cells: { name: text("Potion"), value: cell } }]));

    const toggle = screen.getByRole("button", { name: "Details for Potion" });
    expect(toggle.getAttribute("aria-expanded")).toBe("false");
    fireEvent.click(toggle);
    expect(screen.getByRole("button", { name: "Details for Potion" }).getAttribute("aria-expanded")).toBe("true");
    expect(screen.getByText("Units held", { selector: ".field__description" })).toBeTruthy();
  });
});

describe("TableView filtering", () => {
  it("matches the live value after an edit, not the loaded one", async () => {
    renderTable(table(inventory()));
    const search = screen.getByRole("searchbox", { name: "Filter rows" });

    fireEvent.change(search, { target: { value: "777" } });
    expect(rowCount()).toBe("0 of 10 rows");

    fireEvent.change(search, { target: { value: "" } });
    const input = screen.getByRole("textbox", { name: "count3 (Phoenix Down)" });
    fireEvent.change(input, { target: { value: "777" } });
    await act(async () => {
      fireEvent.keyDown(input, { key: "Enter" });
    });

    fireEvent.change(search, { target: { value: "777" } });
    expect(rowCount()).toBe("1 of 10 rows");
    expect(screen.getByText("Phoenix Down")).toBeTruthy();
  });

  it("matches enum labels of the current value", async () => {
    const rows = inventory().map((r, i) => ({ ...r, cells: { ...r.cells, rarity: field(`rar${i}`, rarity, "1") } }));
    renderTable(table(rows, ["name", "value", "rarity"]));
    const search = screen.getByRole("searchbox", { name: "Filter rows" });

    fireEvent.change(search, { target: { value: "legendary" } });
    expect(rowCount()).toBe("0 of 10 rows");

    fireEvent.change(search, { target: { value: "" } });
    await act(async () => {
      fireEvent.change(screen.getByRole("combobox", { name: "rar0 (Potion)" }), { target: { value: "4" } });
    });
    fireEvent.change(search, { target: { value: "legendary" } });
    expect(rowCount()).toBe("1 of 10 rows");
  });

  it("offers category filters for low-cardinality columns", () => {
    const kinds = ["Weapon", "Armor", "Consumable"];
    const rows = inventory().map((r, i) => ({ ...r, cells: { ...r.cells, kind: text(kinds[i % 3]!) } }));
    renderTable(table(rows, ["name", "value", "kind"]));

    expect(screen.queryByLabelText("Filter by Name")).toBeNull();
    fireEvent.change(screen.getByLabelText("Filter by Kind"), { target: { value: "Weapon" } });
    expect(rowCount()).toBe("4 of 10 rows");
    fireEvent.click(screen.getByRole("button", { name: "Reset filters" }));
    expect(rowCount()).toBe("10 rows");
  });
});

describe("TableView selection", () => {
  it("makes distant virtual rows reachable with named keyboard scrolling", async () => {
    renderTable(table(Array.from({ length: 200 }, (_, index) => ({ key: `r${index}`,
      cells: { name: text(`Item ${index}`), value: field(`count${index}`, count, "1") } }))));
    const region = screen.getByRole("region", { name: "Items rows" });
    expect(region.tabIndex).toBe(0);
    region.focus();
    expect(screen.queryByRole("checkbox", { name: "Select Item 199" })).toBeNull();
    fireEvent.keyDown(region, { key: "End" });
    expect(region.scrollTop).toBeGreaterThan(0);
    // jsdom has no layout engine to emit the native scroll event after scrollTop changes.
    fireEvent.scroll(region);
    const last = await screen.findByRole("checkbox", { name: "Select Item 199" });
    last.focus();
    fireEvent.click(last);
    expect((last as HTMLInputElement).checked).toBe(true);
    const input = screen.getByRole("textbox", { name: "count199 (Item 199)" });
    input.focus();
    const offset = region.scrollTop;
    fireEvent.keyDown(input, { key: "ArrowUp" });
    expect(region.scrollTop).toBe(offset);
    region.focus();
    fireEvent.keyDown(region, { key: "Home" });
    expect(region.scrollTop).toBe(0);
    fireEvent.keyDown(region, { key: "PageDown" });
    expect(region.scrollTop).toBe(region.clientHeight);
    fireEvent.keyDown(region, { key: "PageUp" });
    expect(region.scrollTop).toBe(0);
    fireEvent.keyDown(region, { key: "ArrowDown" });
    expect(region.scrollTop).toBeGreaterThan(0);
    fireEvent.keyDown(region, { key: "ArrowUp" });
    expect(region.scrollTop).toBe(0);
    expect(document.activeElement).toBe(region);
  });

  it("selects rows by keyboard-accessible checkboxes and shows the count", () => {
    renderTable(table(inventory()));

    fireEvent.click(screen.getByRole("checkbox", { name: "Select Ether" }));
    expect(screen.getByText("1 selected")).toBeTruthy();

    fireEvent.change(screen.getByRole("searchbox", { name: "Filter rows" }), { target: { value: "potion" } });
    const all = screen.getByRole("checkbox", { name: "Select all shown rows" });
    fireEvent.click(all);
    expect(screen.getByText("3 selected (1 hidden by filters)")).toBeTruthy();
    expect((all as HTMLInputElement).checked).toBe(true);

    fireEvent.click(all);
    expect(screen.getByText("1 selected (1 hidden by filters)")).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Clear selection" }));
    expect(screen.queryByText(/selected/)).toBeNull();
  });

  it("marks selected rows", () => {
    renderTable(table(inventory()));
    const box = screen.getByRole("checkbox", { name: "Select Tent" });
    fireEvent.click(box);
    expect(box.closest('[role="row"]')?.className).toContain("table__row--selected");
    expect(within(box.closest('[role="row"]') as HTMLElement).getByText("Tent")).toBeTruthy();
  });
});
