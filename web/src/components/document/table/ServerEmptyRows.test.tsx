import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeAll, expect, it, vi } from "vitest";
import { actionReview } from "../../../api/actionReview";
import { api } from "../../../api/client";
import type { CollectionQuery, QueryablePage } from "../../../api/protocol";
import { DocumentContext, DocumentStore, type DocumentContextValue } from "../../../state/documentStore";
import { ServerTableView } from "./ServerTableView";

beforeAll(() => {
  HTMLDialogElement.prototype.showModal = function () { this.open = true; };
  HTMLDialogElement.prototype.close = function () { this.open = false; };
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

const columns = [{ key: "name", label: "Name" }, { key: "action", label: "Action" }];
const refill = { type: "action" as const, actionId: "refill", label: "Refill", supportsPlanning: true };

function page(query: CollectionQuery): QueryablePage {
  const includeEmpty = query.includeEmpty === true;
  return { collectionId: "items", columns, totalCount: 150, filteredCount: includeEmpty ? 150 : 100,
    hiddenEmptyCount: includeEmpty ? 0 : 50, offset: query.offset ?? 0, limit: 50, revision: 1,
    rows: [{ key: "slot/1", cells: { name: { type: "text", text: "Potion" }, action: refill } },
      ...(includeEmpty ? [{ key: "slot/127", isEmpty: true, cells: { name: { type: "text" as const, text: "Unknown ID 0xFFFFFFFF" } } }] : [])] };
}

function setup() {
  vi.spyOn(api, "queryCollection").mockImplementation(async (_w, _d, _id, query = {}) => page(query));
  const plan = vi.spyOn(actionReview, "plan").mockResolvedValue({ revision: 1, scope: {},
    plan: { changes: [], skippedRows: [], warnings: [], errors: [] } });
  const store = new DocumentStore({ canUndo: false, canRedo: false, isDirty: false, revision: 1 });
  const context: DocumentContextValue = { workspaceId: "w", documentId: "d", store, canEdit: true, structureVersion: 0,
    refresh: vi.fn(async () => {}), commit: vi.fn(async () => true), mutate: async call => call(), acceptResult: vi.fn(), runAction: vi.fn() };
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } });
  render(<QueryClientProvider client={client}><DocumentContext.Provider value={context}>
    <ServerTableView node={{ type: "queryableTable", collectionId: "items", title: "Items", columns, totalCount: 150,
      emptyRows: { labelColumnKey: "name" } }} />
  </DocumentContext.Provider></QueryClientProvider>);
  return { client, plan };
}

it("resets paging and selection when showing/hiding, while retaining filters and sort", async () => {
  const { client } = setup();
  await screen.findByText("50 empty slots hidden");
  expect(api.queryCollection).toHaveBeenLastCalledWith("w", "d", "items", expect.objectContaining({ includeEmpty: false }), 1);
  const search = screen.getByRole("searchbox");
  const sort = screen.getByRole("combobox", { name: "Sort rows" });
  act(() => {
    fireEvent.change(search, { target: { value: "Potion" } });
    fireEvent.change(sort, { target: { value: "name" } });
  });
  await waitFor(() => expect((screen.getByRole("button", { name: "Next" }) as HTMLButtonElement).disabled).toBe(false));
  fireEvent.click(screen.getByRole("button", { name: "Next" }));
  await waitFor(() => expect(api.queryCollection).toHaveBeenLastCalledWith("w", "d", "items", expect.objectContaining({ offset: 50 }), 1));
  fireEvent.click(await screen.findByRole("checkbox", { name: "Select Potion" }));
  expect(screen.getByText("1 selected")).toBeTruthy();
  fireEvent.click(screen.getByRole("button", { name: "Show empty slots" }));
  const empty = await screen.findByRole("checkbox", { name: "Select Empty slot slot/127" });
  expect(empty.closest('[role="row"]')?.classList.contains("table__row--empty")).toBe(true);
  expect(screen.getByText("Empty slot")).toBeTruthy();
  expect(screen.queryByText("Unknown ID 0xFFFFFFFF")).toBeNull();
  expect(screen.queryByText("1 selected")).toBeNull();
  expect(api.queryCollection).toHaveBeenLastCalledWith("w", "d", "items", expect.objectContaining({ offset: 0, q: "Potion", sort: "name", includeEmpty: true }), 1);
  fireEvent.click(screen.getByRole("checkbox", { name: "Select all rows on this page" }));
  expect(screen.getByText("2 selected")).toBeTruthy();
  fireEvent.click(screen.getByRole("button", { name: "Hide empty slots" }));
  await screen.findByText("50 empty slots hidden");
  expect(screen.queryByRole("checkbox", { name: "Select Empty slot slot/127" })).toBeNull();
  expect(screen.queryByText("2 selected")).toBeNull();
  client.clear();
}, 10_000); // Multiple server pages, filters and selection resets: 1.4 s locally; allow slower Windows CI.

it.each([false, true])("carries includeEmpty=%s and exclusions into select-all-matching F6 requests", async includeEmpty => {
  const { client, plan } = setup();
  await screen.findByText("50 empty slots hidden");
  if (includeEmpty) {
    fireEvent.click(screen.getByRole("button", { name: "Show empty slots" }));
    await screen.findByText("Showing empty slots");
    await screen.findByRole("checkbox", { name: "Select Empty slot slot/127" });
  }
  const count = includeEmpty ? 150 : 100;
  fireEvent.click(screen.getByRole("button", { name: `Select all ${count} filtered rows` }));
  fireEvent.click(screen.getByRole("checkbox", { name: "Select Potion" }));
  expect(screen.getByText(`${count - 1} selected`)).toBeTruthy();
  fireEvent.click(screen.getByRole("button", { name: "Refill: Potion" }));
  await waitFor(() => expect(plan).toHaveBeenCalled());
  expect(plan).toHaveBeenLastCalledWith("w", "d", "refill", {}, { queryScope: {
    collectionId: "items", revision: 1, excludedRowKeys: ["slot/1"],
    query: expect.objectContaining({ includeEmpty }),
  } }, 1);
  await act(async () => client.clear());
});
