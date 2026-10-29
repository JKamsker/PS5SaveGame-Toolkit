import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { api, ApiError } from "../../../api/client";
import type { QueryablePage } from "../../../api/protocol";
import { DocumentContext, DocumentStore, type DocumentContextValue } from "../../../state/documentStore";
import { ServerTableView } from "./ServerTableView";
import { deferred } from "../../../test/deferred";
import { field } from "./tableTestUtils";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });

const columns = [{ key: "name", label: "Name" }];
const page: QueryablePage = { collectionId: "items", columns, totalCount: 100, filteredCount: 100,
  offset: 0, limit: 50, revision: 1, rows: [{ key: "a", cells: { name: { type: "text", text: "Potion" } } }] };

function setup(refresh = vi.fn(async () => {})) {
  const store = new DocumentStore({ canUndo: false, canRedo: false, isDirty: false, revision: 1 });
  const context: DocumentContextValue = { workspaceId: "w", documentId: "d", store, canEdit: true, structureVersion: 0,
    refresh, commit: vi.fn(async () => true), mutate: async call => call(), acceptResult: vi.fn(), runAction: vi.fn() };
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } });
  const view = (generation: number) => <QueryClientProvider client={client}><DocumentContext.Provider value={{ ...context, structureVersion: generation }}>
    <ServerTableView node={{ type: "queryableTable", collectionId: "items", title: "Items", columns, totalCount: 100 }} />
  </DocumentContext.Provider></QueryClientProvider>;
  const { rerender } = render(view(0));
  return { store, client, refresh, generation: (value: number) => rerender(view(value)) };
}

describe("server table request lifecycle", () => {
  it("recomputes column confidence for each page and preserves cell screen-reader labels", async () => {
    const unknown = field("a", { kind: "text", readOnly: true }, "Potion", { confidence: "Unknown" });
    const verified = field("b", { kind: "text", readOnly: true }, "Ether", { confidence: "Verified" });
    vi.spyOn(api, "queryCollection").mockResolvedValueOnce({ ...page, rows: [
      { key: "a", cells: { name: unknown } }, { key: "b", cells: { name: { ...unknown, valueId: "b", value: "Ether" } } },
    ] }).mockResolvedValueOnce({ ...page, offset: 50, rows: [
      { key: "a", cells: { name: unknown } }, { key: "b", cells: { name: verified } },
    ] });
    const { client } = setup();
    await screen.findByRole("checkbox", { name: "Select Potion" });
    const heading = screen.getByRole("columnheader", { name: /^Name/ });
    expect(within(heading).getByRole("img", { name: "Confidence: meaning unknown" })).toBeTruthy();
    expect(screen.getAllByRole("img", { name: /^Confidence:/ })).toHaveLength(1);
    expect(screen.getAllByText("Confidence: meaning unknown", { selector: ".visually-hidden" })).toHaveLength(2);
    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    await screen.findByRole("img", { name: "Confidence: verified" });
    expect(within(screen.getByRole("columnheader", { name: "Name" })).queryByRole("img")).toBeNull();
    expect(screen.getAllByRole("img", { name: /^Confidence:/ })).toHaveLength(2);
    expect(screen.getByRole("img", { name: "Confidence: verified" }).closest('[role="cell"]')).not.toBeNull();
    client.clear();
  });

  it("distinguishes loading rows from a completed empty result", async () => {
    const pending = deferred<QueryablePage>();
    vi.spyOn(api, "queryCollection").mockReturnValue(pending.promise);
    const { client } = setup();
    expect(screen.getByRole("status").textContent).toBe("Loading rows…");
    expect(screen.getByRole("table").getAttribute("aria-busy")).toBe("true");
    expect(screen.queryByText("No rows match the current filters.")).toBeNull();
    await act(async () => pending.resolve({ ...page, rows: [], filteredCount: 0 }));
    expect(await screen.findByText("No rows match the current filters.")).toBeTruthy();
    expect(screen.queryByRole("status")).toBeNull();
    client.clear();
  });

  it("honors column widths and aligns numeric headings without moving filter or page controls", async () => {
    vi.spyOn(api, "queryCollection").mockResolvedValue({ ...page, columns: [
      { key: "name", label: "Name", width: "Wide" },
      { key: "quantity", label: "Quantity", width: "Narrow", spec: { kind: "integer", min: "0", max: "999" } },
    ], rows: [{ key: "a", cells: { name: { type: "text", text: "Potion" }, quantity: {
      type: "field", valueId: "q", label: "Quantity", spec: { kind: "integer", min: "0", max: "999" }, value: "123",
    } } }] });
    const { client } = setup();
    await screen.findByRole("checkbox", { name: "Select Potion" });
    const table = screen.getByRole("table", { name: "Items" });
    expect(table.style.gridTemplateColumns).toContain("var(--table-column-wide)");
    expect(table.style.gridTemplateColumns).toContain("var(--table-column-narrow)");
    expect(screen.getByRole("columnheader", { name: "Quantity" }).classList.contains("table__head--number")).toBe(true);
    expect(screen.getByRole("columnheader", { name: "Quantity" }).title).toBe("Quantity");
    expect(screen.getByRole("searchbox", { name: "Search Items" })).toBeTruthy();
    expect(screen.getByRole("navigation", { name: "Items pages" })).toBeTruthy();
    client.clear();
  });

  it.each([false, true])("preserves row drafts and selection when a read fails (new generation=%s)", async newGeneration => {
    const editable: QueryablePage = { ...page, rows: [{ key: "a", cells: { name: {
      type: "field", valueId: "quantity", label: "Quantity", value: "2", spec: { kind: "integer", min: "0", max: "99" },
    } } }] };
    vi.spyOn(api, "queryCollection").mockResolvedValue(editable);
    const { client, generation } = setup();
    const input = await screen.findByRole<HTMLInputElement>("textbox", { name: /Quantity/ });
    const selected = screen.getByRole<HTMLInputElement>("checkbox", { name: "Select 2" });
    fireEvent.click(selected);
    fireEvent.change(input, { target: { value: "9" } });
    vi.mocked(api.queryCollection).mockRejectedValueOnce(new ApiError("Rows unavailable", 400));
    if (newGeneration) generation(1);
    else await act(async () => { await client.refetchQueries({ queryKey: ["collection"] }); });
    expect(await screen.findByRole("alert")).toHaveProperty("textContent", "Rows unavailable");
    expect(input.isConnected).toBe(true);
    expect(input.value).toBe("9");
    expect(selected.checked).toBe(true);
    if (newGeneration) expect(input.closest("[inert]")).not.toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Retry rows" }));
    await waitFor(() => expect(screen.queryByRole("alert")).toBeNull());
    expect(screen.getByRole("textbox", { name: /Quantity/ })).toBe(input);
    expect(input.value).toBe("9");
    expect(selected.checked).toBe(true);
    expect(input.closest("[inert]")).toBeNull();
    client.clear();
  });

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
