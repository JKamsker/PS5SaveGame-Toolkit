import { cleanup, fireEvent, screen, within } from "@testing-library/react";
import { afterAll, afterEach, beforeAll, expect, it, vi } from "vitest";
import { api } from "../../../api/client";
import { actionReview } from "../../../api/actionReview";
import type { ActionNode, TableNode, ViewNode } from "../../../api/protocol";
import { ViewRenderer } from "../ViewRenderer";
import { renderSpatial } from "../spatial/spatialTestUtils";
import { stubViewport } from "./tableTestUtils";

let restore: () => void;
beforeAll(() => { restore = stubViewport(); });
afterAll(() => restore());
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

const action: ActionNode = { type: "action", actionId: "restore", label: "Restore selected", supportsPlanning: true, selectionTableId: "items" };
const columns = [{ key: "name", label: "Name" }, { key: "action", label: "Action" }];
const table: TableNode = { type: "table", selectionId: "items", title: "Inventory", columns,
  rows: ["Potion", "Ether"].map((name, i) => ({ key: String(i), cells: { name: { type: "text", text: name } } })) };
const root = (...children: ViewNode[]): ViewNode => ({ type: "section", title: "Root", children });

function mockPlan() {
  HTMLDialogElement.prototype.showModal = function () { this.open = true; };
  return vi.spyOn(actionReview, "plan").mockResolvedValue({ revision: 0, scope: {},
    plan: { changes: [], errors: [], warnings: [], skippedRows: [] } });
}

async function preview() {
  fireEvent.click(screen.getByRole("button", { name: action.label }));
  const dialog = within(await screen.findByRole("dialog", { name: `Preview: ${action.label}` }));
  await dialog.findByText("No changes. No undo step will be created.");
}

it("binds a sibling before an eager table, names the table accessibly and isolates other selections", async () => {
  const plan = mockPlan();
  renderSpatial(<ViewRenderer node={root(action, table, { ...table, title: "Other", selectionId: "other",
    rows: [{ key: "foreign", cells: { name: { type: "text", text: "Foreign" } } }] })} />);
  fireEvent.click(screen.getByRole("checkbox", { name: "Select Potion" }));
  fireEvent.click(screen.getByRole("checkbox", { name: "Select Foreign" }));
  const button = screen.getByRole("button", { name: action.label });
  expect(document.getElementById(button.getAttribute("aria-describedby")!)?.textContent).toContain("Inventory");
  await preview();
  expect(plan).toHaveBeenCalledExactlyOnceWith("w1", "d1", "restore", {}, { rowKeys: ["0"] }, 0);
});

it("keeps document scope for an unselected bound table and empty scope for unbound cell actions", async () => {
  const plan = mockPlan();
  renderSpatial(<ViewRenderer node={root(action, { ...table, rows: [{ key: "0", cells: { name: { type: "text", text: "Potion" },
    action: { ...action, label: "Cell restore", selectionTableId: null } } }] })} />);
  await preview();
  expect(plan.mock.calls[0]?.[4]).toEqual({});
  fireEvent.click(screen.getByRole("button", { name: "Cell restore: Potion" }));
  await screen.findByRole("dialog", { name: "Preview: Cell restore" });
  expect(plan.mock.calls[1]?.[4]).toEqual({ rowKeys: [] });
});

it("disables unresolved and ambiguous bindings instead of silently using document scope", () => {
  mockPlan();
  renderSpatial(<ViewRenderer node={root(action)} />);
  expect(screen.getByRole("button", { name: action.label }).hasAttribute("disabled")).toBe(true);
  cleanup();
  renderSpatial(<ViewRenderer node={root(action, table, { ...table, title: "Duplicate" })} />);
  expect(screen.getByRole("button", { name: action.label }).hasAttribute("disabled")).toBe(true);
});

it("uses query selection with filters, ordering, hidden-slot policy and exclusions from unloaded pages", async () => {
  const plan = mockPlan();
  vi.spyOn(api, "queryCollection").mockImplementation(async (_w, _d, id, query) => ({ collectionId: id, columns,
    totalCount: 100, filteredCount: 100, offset: query?.offset ?? 0, limit: 50, revision: 0,
    rows: [{ key: query?.offset ? "b" : "a", cells: { name: { type: "text", text: query?.offset ? "Ether" : "Potion" } } }] }));
  renderSpatial(<ViewRenderer node={root(action, { type: "queryableTable", collectionId: "items", title: "Inventory", columns, totalCount: 100 })} />);
  await screen.findByRole("checkbox", { name: "Select Potion" });
  fireEvent.change(screen.getByRole("textbox", { name: "Filter Name" }), { target: { value: "item" } });
  fireEvent.change(screen.getByRole("combobox", { name: "Sort rows" }), { target: { value: "name" } });
  fireEvent.click(screen.getByRole("button", { name: "Ascending" }));
  await screen.findByRole("checkbox", { name: "Select Potion" });
  fireEvent.click(screen.getByRole("button", { name: "Select all 100 filtered rows" }));
  fireEvent.click(screen.getByRole("button", { name: "Next" }));
  fireEvent.click(await screen.findByRole("checkbox", { name: "Select Ether" }));
  await preview();
  expect(plan.mock.calls[0]?.[4]).toEqual({ queryScope: { collectionId: "items", revision: 0, excludedRowKeys: ["b"],
    query: { text: undefined, filters: { name: "item" }, sortColumn: "name", descending: true, includeEmpty: undefined } } });
});

it("keeps explicit selections from unloaded pages for a sibling control", async () => {
  const plan = mockPlan();
  vi.spyOn(api, "queryCollection").mockImplementation(async (_w, _d, id, query) => ({ collectionId: id, columns,
    totalCount: 100, filteredCount: 100, offset: query?.offset ?? 0, limit: 50, revision: 0,
    rows: [{ key: query?.offset ? "b" : "a", cells: { name: { type: "text", text: query?.offset ? "Ether" : "Potion" } } }] }));
  renderSpatial(<ViewRenderer node={root(action, { type: "queryableTable", collectionId: "items", title: "Inventory", columns, totalCount: 100 })} />);
  fireEvent.click(await screen.findByRole("checkbox", { name: "Select Potion" }));
  fireEvent.click(screen.getByRole("button", { name: "Next" }));
  fireEvent.click(await screen.findByRole("checkbox", { name: "Select Ether" }));
  await preview();
  expect(plan.mock.calls[0]?.[4]).toEqual({ rowKeys: ["a", "b"] });
});
