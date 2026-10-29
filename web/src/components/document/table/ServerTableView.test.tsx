import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
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
  const view = (generation: number) => <QueryClientProvider client={client}><DocumentContext.Provider value={{ ...context, structureVersion: generation }}>
    <ServerTableView node={{ type: "queryableTable", collectionId: "items", title: "Items", columns, totalCount: 100 }} />
  </DocumentContext.Provider></QueryClientProvider>;
  const { rerender } = render(view(0));
  return { store, client, refresh, generation: (value: number) => rerender(view(value)) };
}

describe("server table request lifecycle", () => {
  it("keeps the current page and selection through validation-only notifications", async () => {
    vi.spyOn(api, "queryCollection").mockResolvedValue(page);
    const { store, client } = setup();
    fireEvent.click(await screen.findByRole("checkbox", { name: "Select Potion" }));
    for (let i = 0; i < 10; i++) {
      await act(async () => store.setError("quantity", `Rejected ${i}`));
      await act(async () => store.apply({ ok: false, errors: [{ valueId: "quantity", message: "Rejected" }],
        changes: [], warnings: [], structureChanged: false, state: store.state }));
    }
    expect(api.queryCollection).toHaveBeenCalledTimes(1);
    expect((screen.getByRole("checkbox", { name: "Select Potion" }) as HTMLInputElement).checked).toBe(true);
    client.clear();
  });

  it("refetches after scalar changes even if the structural revision is unchanged", async () => {
    vi.spyOn(api, "queryCollection").mockResolvedValue(page);
    const { store, client } = setup();
    await screen.findByRole("checkbox", { name: "Select Potion" });
    act(() => store.apply({ ok: true, errors: [], changes: [{ valueId: "quantity", oldValue: "1", newValue: "2" }],
      warnings: [], structureChanged: false, state: store.state }));
    await waitFor(() => expect(api.queryCollection).toHaveBeenCalledTimes(2));
    client.clear();
  });

  it("exposes full field warnings through a labelled, collapsible details control", async () => {
    vi.spyOn(api, "queryCollection").mockResolvedValue({ ...page, rows: [{ key: "slot with spaces", cells: {
      name: { type: "field", valueId: "quantity", label: "Potion quantity", value: "2",
        spec: { kind: "integer", min: "0", max: "99" }, warning: "Changing this count is unverified in-game." },
    } }] });
    const { client } = setup();
    const toggle = await screen.findByRole("button", { name: "Details for 2" });
    expect(toggle.getAttribute("aria-expanded")).toBe("false");
    expect(screen.queryByRole("textbox", { name: "Potion quantity" })).toBeNull();
    fireEvent.click(toggle);
    expect(toggle.getAttribute("aria-expanded")).toBe("true");
    const detailId = toggle.getAttribute("aria-controls")!;
    expect(detailId).not.toMatch(/\s/);
    const details = document.getElementById(detailId)!;
    expect(details.getAttribute("role")).toBe("cell");
    expect(details.textContent).toContain("Changing this count is unverified in-game.");
    expect(details.closest('[role="row"]')?.getAttribute("aria-rowindex")).toBe("3");
    expect(screen.getByRole("table").getAttribute("aria-rowcount")).toBe("102");
    fireEvent.click(toggle);
    expect(toggle.getAttribute("aria-expanded")).toBe("false");
    expect(document.getElementById(detailId)).toBeNull();
    expect(screen.getByRole("table").getAttribute("aria-rowcount")).toBe("101");
    client.clear();
  });

  it("counts expanded detail rows when indexing subsequent data rows", async () => {
    vi.spyOn(api, "queryCollection").mockResolvedValue({ ...page, rows: [
      { key: "a", cells: { name: { type: "text", text: "Potion".repeat(20) } } },
      { key: "b", cells: { name: { type: "text", text: "Ether" } } },
    ] });
    const { client } = setup();
    fireEvent.click(await screen.findByRole("button", { name: `Details for ${"Potion".repeat(20)}` }));
    const row = screen.getByRole("checkbox", { name: "Select Ether" }).closest('[role="row"]')!;
    expect(row.getAttribute("aria-rowindex")).toBe("4");
    expect(within(row as HTMLElement).getByText("Ether")).toBeTruthy();
    client.clear();
  });

  it("makes truncated text available without relying on a hover tooltip", async () => {
    const longText = "A long item description that exceeds the compact cell and must be readable in full without hovering.";
    vi.spyOn(api, "queryCollection").mockResolvedValue({ ...page, rows: [{ key: "long", cells: { name: { type: "text", text: longText } } }] });
    const { client } = setup();
    fireEvent.click(await screen.findByRole("button", { name: `Details for ${longText}` }));
    expect(screen.getAllByText(longText)).toHaveLength(2);
    client.clear();
  });

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
    const { client, generation } = setup(refresh);
    await waitFor(() => expect(refresh).toHaveBeenCalledTimes(1));
    expect(await screen.findByRole("alert")).toHaveProperty("textContent", expect.stringContaining("out of date"));
    generation(1);
    await waitFor(() => expect(api.queryCollection).toHaveBeenCalledTimes(2));
    await screen.findByRole("alert");
    expect(refresh).toHaveBeenCalledTimes(1);
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
