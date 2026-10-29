import { cleanup, fireEvent, screen, within } from "@testing-library/react";
import { afterAll, afterEach, beforeAll, describe, expect, it } from "vitest";
import type { Confidence, TableRow } from "../../../api/protocol";
import { uniformConfidence } from "./columnConfidence";
import { field, renderTable, stubViewport, table, text } from "./tableTestUtils";

let restoreViewport: () => void;
beforeAll(() => { restoreViewport = stubViewport(); });
afterAll(() => restoreViewport());
afterEach(cleanup);

function row(key: string, confidence?: Confidence): TableRow {
  return { key, cells: { name: text(key), value: field(key, { kind: "integer", min: "0", max: "99" }, "3", { confidence }) } };
}

describe("column confidence", () => {
  it.each(["Verified", "Inferred", "Unknown"] as const)("shows uniform %s once in the header and keeps each cell's screen-reader text", confidence => {
    renderTable(table([row("Potion", confidence), row("Ether", confidence)]));
    const heading = screen.getByRole("columnheader", { name: /^Value/ });
    const badge = within(heading).getByRole("img");
    expect(badge.textContent).toContain(confidence);
    expect(screen.getAllByRole("img", { name: /^Confidence:/ })).toHaveLength(1);
    const meaning = confidence === "Unknown" ? "meaning unknown" : confidence.toLowerCase();
    for (const name of ["Potion", "Ether"]) {
      const cell = screen.getByRole("textbox", { name }).closest('[role="cell"]')!;
      expect(within(cell as HTMLElement).queryByRole("img")).toBeNull();
      const label = within(cell as HTMLElement).getByText(`Confidence: ${meaning}`);
      expect(label.className).toBe("visually-hidden");
    }
    const trigger = badge.closest<HTMLElement>("[tabindex]")!;
    expect(trigger.tabIndex).toBe(0);
    expect(document.getElementById(trigger.getAttribute("aria-describedby")!)?.textContent).toContain("all currently shown rows");
  });

  it("keeps all badges in a mixed column and never adds a header claim", () => {
    renderTable(table([row("Potion", "Unknown"), row("Ether", "Verified"), row("Elixir", "Inferred")]));
    expect(within(screen.getByRole("columnheader", { name: "Value" })).queryByRole("img")).toBeNull();
    expect(screen.getAllByRole("img", { name: /^Confidence:/ })).toHaveLength(3);
    expect(screen.getByRole("img", { name: "Confidence: meaning unknown" }).closest('[role="cell"]')).not.toBeNull();
  });

  it("recalculates the header when filters remove and restore a differing row", () => {
    renderTable(table([row("Potion", "Unknown"), ...Array.from({ length: 9 }, (_, i) => row(`Ether${i}`, "Verified"))]));
    fireEvent.change(screen.getByRole("searchbox"), { target: { value: "Potion" } });
    expect(within(screen.getByRole("columnheader", { name: /^Value/ })).getByRole("img", { name: "Confidence: meaning unknown" })).toBeTruthy();
    fireEvent.change(screen.getByRole("searchbox"), { target: { value: "" } });
    expect(within(screen.getByRole("columnheader", { name: "Value" })).queryByRole("img")).toBeNull();
    expect(screen.getAllByRole("img", { name: /^Confidence:/ })).toHaveLength(10);
  });

  it("includes offscreen rows when deciding whether confidence is uniform", () => {
    renderTable(table(Array.from({ length: 100 }, (_, i) => row(`item${i}`, i === 99 ? "Verified" : "Unknown"))));
    expect(screen.queryByRole("textbox", { name: "item99" })).toBeNull();
    expect(within(screen.getByRole("columnheader", { name: "Value" })).queryByRole("img")).toBeNull();
    expect(screen.getAllByRole("img", { name: "Confidence: meaning unknown" }).length).toBeGreaterThan(1);
  });

  it.each(["Unknown", "Verified"] as const)("preserves an empty slot's %s confidence", confidence => {
    const node = table([row("Potion", "Unknown"), { ...row("Vacant", confidence), isEmpty: true }]);
    node.emptyRows = { labelColumnKey: "value", hideByDefault: false };
    renderTable(node);
    const cell = screen.getByRole("button", { name: /^Show Empty slot for .*Vacant$/ }).closest('[role="cell"]') as HTMLElement;
    if (confidence === "Unknown") {
      expect(within(cell).getByText("Confidence: meaning unknown").className).toBe("visually-hidden");
    } else {
      expect(within(cell).getByRole("img", { name: "Confidence: verified" })).toBeTruthy();
      expect(within(screen.getByRole("columnheader", { name: "Value" })).queryByRole("img")).toBeNull();
    }
  });

  it.each([
    [], [row("one")], [row("one", "Unknown"), row("two")],
    [row("one", "Unknown"), { key: "missing", cells: {} }],
    [row("one", "Unknown"), { key: "text", cells: { value: text("raw") } }],
  ])("does not infer confidence from absent or non-field metadata (%#)", (...rows: TableRow[]) => {
    expect(uniformConfidence([{ key: "value", label: "Value" }], rows).size).toBe(0);
  });

  it("keeps descriptions and inspection reachable in row details", () => {
    const item = row("Potion", "Unknown");
    item.cells.value = { ...field("quantity", { kind: "integer", min: "0", max: "99" }, "3"), confidence: "Unknown", description: "Raw count; inspect before editing." };
    renderTable(table([item]));
    expect(screen.getByRole("img", { name: "Description: Raw count; inspect before editing." }).tabIndex).toBe(0);
    fireEvent.click(screen.getByRole("button", { name: "Details for Potion" }));
    expect(screen.getByText("Raw count; inspect before editing.", { selector: ".field__description" })).toBeTruthy();
    expect(screen.getByText("meaning unknown", { selector: ".tag" })).toBeTruthy();
  });
});
