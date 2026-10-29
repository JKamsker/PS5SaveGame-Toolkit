import { defaultScheduler, notifyManager, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { api, ApiError } from "../../../api/client";
import type { QueryablePage } from "../../../api/protocol";
import { DocumentContext, DocumentStore, type DocumentContextValue } from "../../../state/documentStore";
import { deferred } from "../../../test/deferred";
import { ServerTableView } from "./ServerTableView";

const clients: QueryClient[] = [];
beforeEach(() => { notifyManager.setScheduler(queueMicrotask); });
afterEach(() => { cleanup(); clients.splice(0).forEach(client => client.clear()); vi.restoreAllMocks(); notifyManager.setScheduler(defaultScheduler); });
const columns = [{ key: "name", label: "Name" }];
const page = (label: string): QueryablePage => ({ collectionId: "items", revision: 1, offset: 0, limit: 50,
  totalCount: 100, filteredCount: 1, columns, rows: [{ key: label, cells: { name: { type: "text", text: label } } }] });

it.each(["resolve", "reject"] as const)("keeps the latest search/filter rows when obsolete queries %s", async outcome => {
  const first = deferred<QueryablePage>(); const second = deferred<QueryablePage>(); const filtered = deferred<QueryablePage>();
  const read = vi.spyOn(api, "queryCollection").mockImplementation((_w, _d, _collection, query) => {
    if (query?.filters?.name) return filtered.promise;
    if (query?.q === "p") return first.promise;
    if (query?.q === "pot") return second.promise;
    return Promise.resolve(page("Initial"));
  });
  const store = new DocumentStore({ revision: 1, isDirty: false, canUndo: false, canRedo: false });
  const context: DocumentContextValue = { workspaceId: "w", documentId: "d", structureVersion: 0, canEdit: true, store,
    commit: vi.fn(), refresh: vi.fn(), acceptResult: vi.fn(), runAction: vi.fn(), mutate: call => call() };
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } }); clients.push(client);
  await act(async () => { render(<QueryClientProvider client={client}><DocumentContext.Provider value={context}>
    <ServerTableView node={{ type: "queryableTable", collectionId: "items", title: "Items", columns, totalCount: 100 }} />
  </DocumentContext.Provider></QueryClientProvider>); });
  const search = screen.getByRole("searchbox");
  await act(async () => { fireEvent.change(search, { target: { value: "p" } }); });
  await act(async () => { fireEvent.change(search, { target: { value: "pot" } }); });
  await act(async () => { fireEvent.change(screen.getByLabelText("Filter Name"), { target: { value: "healing" } }); });
  expect(read).toHaveBeenCalledTimes(4);
  expect(screen.queryByRole("checkbox", { name: "Select Initial" })).toBeNull();
  await act(async () => { filtered.resolve(page("Latest potion")); await filtered.promise; });
  await act(async () => { second.resolve(page("Old search")); await second.promise; });
  await act(async () => {
    if (outcome === "resolve") first.resolve(page("Oldest search")); else first.reject(new ApiError("Obsolete search failed", 400));
    await first.promise.catch(() => {});
  });
  expect(screen.getByRole("checkbox", { name: "Select Latest potion" })).toBeTruthy();
  expect(screen.queryByRole("checkbox", { name: "Select Old search" })).toBeNull();
  expect(screen.queryByRole("checkbox", { name: "Select Oldest search" })).toBeNull();
  expect(screen.queryByRole("alert")).toBeNull();
});
