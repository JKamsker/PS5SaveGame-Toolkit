import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { api } from "../../../api/client";
import type { QueryablePage } from "../../../api/protocol";
import { DocumentContext, DocumentStore, type DocumentContextValue } from "../../../state/documentStore";
import { ReviewNavigationProvider, useReviewNavigation } from "../review/navigation";
import { ServerTableView } from "./ServerTableView";

const clients: QueryClient[] = [];
afterEach(() => { cleanup(); clients.splice(0).forEach(client => client.clear()); vi.restoreAllMocks(); });
const columns = [{ key: "name", label: "Name" }, { key: "type", label: "Type" }];

function Jump({ includeEmpty }: { includeEmpty: boolean }) {
  const { target, jump } = useReviewNavigation();
  return <>
    <button onClick={() => jump({ path: ["row:a"], collection: { collectionId: "items", rowKey: "a",
      query: { q: "Potion", filters: { name: "Healing", type: "number" }, offset: 50, cursor: "jump-page",
        sort: "name", descending: true, includeEmpty, limit: 50 } } })}>Jump to filtered row</button>
    <output aria-label="Jump intent">{target ? "Active" : "None"}</output>
  </>;
}

function setup(includeEmpty: boolean) {
  const read = vi.spyOn(api, "queryCollection").mockImplementation(async (_w, _d, _id, query = {}): Promise<QueryablePage> => ({
    collectionId: "items", columns, revision: 1, totalCount: 150, filteredCount: query.q === "missing" ? 0 : 100,
    offset: query.cursor ? 50 : query.offset ?? 0, limit: 50, nextCursor: query.cursor || query.offset ? undefined : "next-page",
    rows: query.q === "missing" ? [] : [{ key: "a", cells: { name: { type: "text", text: query.q ? "Filtered" : "Restored" } } }],
  }));
  const store = new DocumentStore({ canUndo: false, canRedo: false, isDirty: false, revision: 1 });
  const context: DocumentContextValue = { workspaceId: "w", documentId: "d", store, canEdit: true, structureVersion: 0,
    refresh: vi.fn(), commit: vi.fn(), mutate: call => call(), acceptResult: vi.fn(), runAction: vi.fn() };
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } });
  clients.push(client);
  render(<QueryClientProvider client={client}><DocumentContext.Provider value={context}><ReviewNavigationProvider>
    <Jump includeEmpty={includeEmpty} />
    <ServerTableView node={{ type: "queryableTable", collectionId: "items", title: "Items", columns, totalCount: 150,
      emptyRows: { labelColumnKey: "name", hideByDefault: !includeEmpty } }} />
  </ReviewNavigationProvider></DocumentContext.Provider></QueryClientProvider>);
  return read;
}

function expectClearedQuery(read: ReturnType<typeof setup>, includeEmpty: boolean) {
  expect(read).toHaveBeenLastCalledWith("w", "d", "items", expect.objectContaining({ q: undefined, filters: {}, offset: 0,
    cursor: undefined, sort: "name", descending: true, includeEmpty }), 1);
  expect(screen.getByRole("searchbox", { name: "Search Items" })).toHaveProperty("value", "");
  expect(screen.getByLabelText("Filter Name")).toHaveProperty("value", "");
  expect(screen.getByLabelText("Filter Type")).toHaveProperty("value", "");
  expect(screen.getByRole("status", { name: "Items selection" }).textContent).toBe("No rows selected");
  expect(screen.queryByRole("button", { name: "Clear search and filters" })).toBeNull();
  expect(document.activeElement).toBe(screen.getByRole("searchbox", { name: "Search Items" }));
}

it.each([false, true])("clears combined criteria, paging and all-query selection, preserving includeEmpty=%s and sort", async includeEmpty => {
  const read = setup(includeEmpty);
  await screen.findByRole("checkbox", { name: "Select Restored" });
  expect(screen.queryByRole("button", { name: "Clear search and filters" })).toBeNull();
  fireEvent.change(screen.getByRole("searchbox"), { target: { value: "Potion" } });
  fireEvent.change(screen.getByLabelText("Filter Name"), { target: { value: "Healing" } });
  fireEvent.change(screen.getByLabelText("Filter Type"), { target: { value: "number" } });
  fireEvent.change(screen.getByRole("combobox", { name: "Sort rows" }), { target: { value: "name" } });
  fireEvent.click(screen.getByRole("button", { name: "Ascending" }));
  await screen.findByRole("checkbox", { name: "Select Filtered" });
  fireEvent.click(screen.getByRole("button", { name: "Next" }));
  await waitFor(() => expect(read).toHaveBeenLastCalledWith("w", "d", "items", expect.objectContaining({
    q: "Potion", filters: { name: "Healing", type: "number" }, offset: 0, cursor: "next-page",
  }), 1));
  await screen.findByRole("checkbox", { name: "Select Filtered" });
  expect(screen.getByText("Page 2")).toBeTruthy();
  fireEvent.click(screen.getByRole("button", { name: "Select all 100 filtered rows" }));
  expect(screen.getByRole("status", { name: "Items selection" }).textContent).toContain("100 selected");
  const clear = screen.getByRole("button", { name: "Clear search and filters" });
  clear.focus();
  fireEvent.click(clear);
  await screen.findByRole("checkbox", { name: "Select Restored" });
  expectClearedQuery(read, includeEmpty);
});

it("retires a review jump's filtered page and focus intent when clearing", async () => {
  const read = setup(true);
  await screen.findByRole("checkbox", { name: "Select Restored" });
  fireEvent.click(screen.getByRole("button", { name: "Jump to filtered row" }));
  await screen.findByRole("checkbox", { name: "Select Filtered" });
  expect(screen.getByLabelText("Jump intent").textContent).toBe("Active");
  expect(read).toHaveBeenLastCalledWith("w", "d", "items", expect.objectContaining({ cursor: "jump-page", offset: 50 }), 1);
  fireEvent.click(screen.getByRole("button", { name: "Clear search and filters" }));
  await screen.findByRole("checkbox", { name: "Select Restored" });
  expectClearedQuery(read, true);
  expect(screen.getByLabelText("Jump intent").textContent).toBe("None");
});

it("recovers an empty combined search and filters in one action", async () => {
  const read = setup(false);
  await screen.findByRole("checkbox", { name: "Select Restored" });
  fireEvent.change(screen.getByRole("searchbox"), { target: { value: "missing" } });
  fireEvent.change(screen.getByLabelText("Filter Name"), { target: { value: "Healing" } });
  fireEvent.change(screen.getByLabelText("Filter Type"), { target: { value: "number" } });
  await screen.findByText("No rows match the current filters.");
  fireEvent.click(screen.getByRole("button", { name: "Clear search and filters" }));
  await screen.findByRole("checkbox", { name: "Select Restored" });
  expect(read).toHaveBeenLastCalledWith("w", "d", "items", expect.objectContaining({ q: undefined, filters: {}, offset: 0 }), 1);
  expect(screen.queryByText("No rows match the current filters.")).toBeNull();
});
