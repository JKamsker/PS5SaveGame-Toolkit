import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { EmptyRowsToggle } from "./EmptyRowsToggle";
import { ServerTableToolbar } from "./ServerTableToolbar";
import type { ServerTableModel } from "./ServerTableView";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });

it("intentionally uses German grouping for human row and selection counts", () => {
  const format = Number.prototype.toLocaleString;
  vi.spyOn(Number.prototype, "toLocaleString").mockImplementation(function (this: number, locale, options) {
    return format.call(this, locale ?? "de-DE", options);
  });
  // Only presentation is exercised; no query or selection callback is invoked.
  const table = {
    page: { filteredCount: 1234, totalCount: 5678 }, result: {}, includeEmpty: false,
    selection: { count: 1234, queryScope: null }, rows: [], columns: [], filters: {}, text: "", sort: "",
  } as unknown as ServerTableModel;
  render(<ServerTableToolbar node={{ type: "queryableTable", collectionId: "items", title: "Items", columns: [], totalCount: 5678 }} table={table} />);
  expect(screen.getByText("1.234 of 5.678 rows")).toBeTruthy();
  expect(screen.getByText("1.234 selected")).toBeTruthy();
  expect(screen.getByRole("button", { name: "Select all 1.234 filtered rows" })).toBeTruthy();
  render(<EmptyRowsToggle presentation={{ labelColumnKey: "name", pluralLabel: "slots" }} includeEmpty={false} hiddenCount={1234} onChange={() => {}} />);
  expect(screen.getByText("1.234 slots hidden")).toBeTruthy();
});
