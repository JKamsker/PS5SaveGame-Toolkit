import { cleanup, fireEvent, screen } from "@testing-library/react";
import { afterEach, expect, it } from "vitest";
import { renderTable, stubViewport, table, text } from "./tableTestUtils";

afterEach(cleanup);

it("returns focus to the eager table header before clearing selection", () => {
  const restoreViewport = stubViewport();
  try {
    renderTable(table([{ key: "a", cells: { name: text("Potion") } }], ["name"]));
    fireEvent.click(screen.getByRole("checkbox", { name: "Select Potion" }));
    const clear = screen.getByRole("button", { name: "Clear selection" });
    clear.focus();
    fireEvent.click(clear);
    expect(screen.queryByRole("button", { name: "Clear selection" })).toBeNull();
    expect(document.activeElement).toBe(screen.getByRole("checkbox", { name: "Select all shown rows" }));
  } finally { restoreViewport(); }
});

it("uses a stable table anchor when filters leave the header disabled", () => {
  const restoreViewport = stubViewport();
  try {
    renderTable(table(Array.from({ length: 9 }, (_, index) => ({ key: String(index), cells: { name: text(`Item ${index}`) } })), ["name"]));
    fireEvent.click(screen.getByRole("checkbox", { name: "Select Item 0" }));
    fireEvent.change(screen.getByRole("searchbox", { name: "Filter rows" }), { target: { value: "no match" } });
    const clear = screen.getByRole("button", { name: "Clear selection" });
    clear.focus();
    fireEvent.click(clear);
    expect(document.activeElement).toBe(screen.getByRole("heading", { name: "Items" }));
  } finally { restoreViewport(); }
});
