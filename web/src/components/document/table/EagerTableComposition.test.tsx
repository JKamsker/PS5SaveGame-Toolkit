import { cleanup, fireEvent, screen } from "@testing-library/react";
import { afterEach, beforeEach, expect, it } from "vitest";
import { field, renderTable, stubViewport, table, text } from "./tableTestUtils";

let restoreViewport: () => void;
beforeEach(() => { restoreViewport = stubViewport(); });
afterEach(() => { cleanup(); restoreViewport(); });

it("keeps selection announcements mounted as the header transitions through partial and empty selection", () => {
  const { container } = renderTable(table([
    { key: "a", cells: { name: text("Potion") } },
    { key: "b", cells: { name: text("Ether") } },
  ], ["name"]));
  const announcement = container.querySelector(".table__selected [aria-live]")!;
  const all = screen.getByRole<HTMLInputElement>("checkbox", { name: "Select all shown rows" });
  expect(announcement.textContent).toBe("");
  fireEvent.click(screen.getByRole("checkbox", { name: "Select Potion" }));
  expect(all.indeterminate).toBe(true);
  expect(announcement.textContent).toBe("1 selected");
  fireEvent.click(all);
  expect(all.checked).toBe(true);
  expect(all.indeterminate).toBe(false);
  fireEvent.click(screen.getByRole("button", { name: "Clear selection" }));
  expect(container.querySelector(".table__selected [aria-live]")).toBe(announcement);
  expect(announcement.textContent).toBe("");
  expect(all.checked).toBe(false);
});

it("connects details for row keys with spaces without resetting compact drafts or selection", () => {
  renderTable(table([{ key: "slot with spaces", cells: {
    name: text("Potion"), value: field("quantity", { kind: "integer", min: "0", max: "99" }, "2", { description: "Units held" }),
  } }]));
  const input = screen.getByRole<HTMLInputElement>("textbox", { name: "quantity (Potion)" });
  fireEvent.change(input, { target: { value: "9" } });
  const selected = screen.getByRole<HTMLInputElement>("checkbox", { name: "Select Potion" });
  fireEvent.click(selected);
  const toggle = screen.getByRole("button", { name: "Details for Potion" });
  fireEvent.click(toggle);
  const detailId = toggle.getAttribute("aria-controls")!;
  expect(detailId).not.toMatch(/\s/);
  expect(document.getElementById(detailId)?.textContent).toContain("Units held");
  fireEvent.click(toggle);
  expect(document.getElementById(detailId)).toBeNull();
  expect(input.isConnected).toBe(true);
  expect(input.value).toBe("9");
  expect(selected.checked).toBe(true);
});
