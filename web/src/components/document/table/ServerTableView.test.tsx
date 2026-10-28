import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { api, ApiError } from "../../../api/client";
import type { QueryablePage } from "../../../api/protocol";
import { DocumentContext, DocumentStore, type DocumentContextValue } from "../../../state/documentStore";
import { ServerTableView } from "./ServerTableView";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });

const columns = [{ key: "name", label: "Name" }];
const page: QueryablePage = { collectionId: "items", columns, totalCount: 100, filteredCount: 100,
  offset: 0, limit: 50, revision: 1, rows: [{ key: "a", cells: { name: { type: "text", text: "Potion" } } }] };

function setup(refresh = vi.fn(async () => {})) {
  const store = new DocumentStore({ canUndo: false, canRedo: false, isDirty: false, revision: 1 });
  const context: DocumentContextValue = { workspaceId: "w", documentId: "d", store, canEdit: true, structureVersion: 0,
    refresh, commit: vi.fn(async () => true), acceptResult: vi.fn(), runAction: vi.fn() };
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } });
  render(<QueryClientProvider client={client}><DocumentContext.Provider value={context}>
    <ServerTableView node={{ type: "queryableTable", collectionId: "items", title: "Items", columns, totalCount: 100 }} />
  </DocumentContext.Provider></QueryClientProvider>);
  return { store, client, refresh };
}

describe("server table request lifecycle", () => {
  it("hides old interactive rows during a new query and exposes failures", async () => {
    let reject!: (error: Error) => void;
    vi.spyOn(api, "queryCollection").mockResolvedValueOnce(page)
      .mockImplementation(() => new Promise((_, fail) => { reject = fail; }));
    const { client } = setup();
    await screen.findByRole("checkbox", { name: "Select Potion" });
    fireEvent.change(screen.getByRole("searchbox"), { target: { value: "ether" } });
    await waitFor(() => expect(api.queryCollection).toHaveBeenCalledTimes(2));
    expect(screen.queryByRole("checkbox", { name: "Select Potion" })).toBeNull();
    expect(screen.queryByRole("button", { name: /Select all 100/ })).toBeNull();
    await act(async () => { reject(new ApiError("Query unavailable", 400)); });
    expect(await screen.findByRole("alert")).toHaveProperty("textContent", "Query unavailable");
    expect(screen.queryByText("No rows match the current filters.")).toBeNull();
    client.clear();
  });

  it("discards old pages and selections after undo or a group edit", async () => {
    let finish!: (value: QueryablePage) => void;
    vi.spyOn(api, "queryCollection").mockResolvedValueOnce(page)
      .mockImplementation(() => new Promise(resolve => { finish = resolve; }));
    const { store, client } = setup();
    fireEvent.click(await screen.findByRole("button", { name: "Select all 100 filtered rows" }));
    act(() => store.apply({ ok: true, errors: [], changes: [], warnings: [], structureChanged: false,
      state: { canUndo: false, canRedo: true, isDirty: false, revision: 2 } }));
    await waitFor(() => expect(api.queryCollection).toHaveBeenCalledTimes(2));
    expect(screen.queryByRole("checkbox", { name: "Select Potion" })).toBeNull();
    expect(screen.queryByText(/100 selected/)).toBeNull();
    await act(async () => { finish({ ...page, revision: 2, rows: [] }); });
    await screen.findByText("No rows match the current filters.");
    client.clear();
  });

  it("attempts stale recovery once and keeps a manual refresh available", async () => {
    vi.spyOn(api, "queryCollection").mockRejectedValue(new ApiError("Stale view", 409, { code: "stale" }));
    const refresh = vi.fn(async () => { throw new Error("Offline"); });
    const { client } = setup(refresh);
    await waitFor(() => expect(refresh).toHaveBeenCalledTimes(1));
    expect(await screen.findByRole("alert")).toHaveProperty("textContent", expect.stringContaining("out of date"));
    fireEvent.click(screen.getByRole("button", { name: "Refresh document" }));
    await waitFor(() => expect(refresh).toHaveBeenCalledTimes(2));
    client.clear();
  });

  it("uses absolute row indexes on later pages", async () => {
    vi.spyOn(api, "queryCollection").mockResolvedValueOnce(page).mockResolvedValue({ ...page, offset: 50 });
    const { client } = setup();
    await screen.findByRole("checkbox", { name: "Select Potion" });
    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    await waitFor(() => expect(screen.getByRole("checkbox", { name: "Select Potion" }).closest('[role="row"]')?.getAttribute("aria-rowindex")).toBe("52"));
    client.clear();
  });
});
