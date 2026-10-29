import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { api, ApiError } from "../../../api/client";
import type { ReferenceCandidate, ReferenceCandidatePage, ReferenceDatasetInfo } from "../../../api/protocol";
import { DocumentContext, DocumentStore, type DocumentContextValue } from "../../../state/documentStore";
import { field, renderTable, stubViewport, table } from "../../document/table/tableTestUtils";
import { FieldRow } from "../FieldRow";

const dataset: ReferenceDatasetInfo = {
  packId: "test", id: "items", kind: "Records", itemCount: 2,
  attribution: { author: "Fixture contributors", sourceUrl: "https://example.org/items", license: "CC0", licenseUrl: "https://example.org/license", retrieved: "2026-10-28", modifications: "Synthetic fixture" },
};
const potion: ReferenceCandidate = {
  rawId: "1001", label: "Potion", description: "Restores health", category: "consumable", assetId: "test:icons/potion.png", dataset,
  assetDataset: { ...dataset, id: "icons", kind: "Assets" },
  eligibility: { available: true, eligible: true, selectable: true, reasons: [] },
};
const sword: ReferenceCandidate = {
  rawId: "2001", label: "Sword", category: "weapon", dataset,
  eligibility: { available: true, eligible: false, selectable: false, reasons: ["Requires a quest"] },
};
const unknown = (rawId: string, selectable = false): ReferenceCandidate => ({ rawId, eligibility: { available: true, eligible: selectable, selectable, reasons: ["Id absent from catalog"] } });
const page = (items = [potion, sword]): ReferenceCandidatePage => ({ catalogId: "items", items, total: items.length, offset: 0, limit: 30, categories: ["consumable", "weapon"], sortKeys: ["Key", "Name", "Category"], revision: 0 });
const node = field("item/id", { kind: "reference", catalogId: "items" }, "9999", { label: "Item" });

function renderField(options: { readOnly?: boolean; canEdit?: boolean; custom?: boolean } = {}) {
  const commit = vi.fn(async () => true);
  const context: DocumentContextValue = {
    workspaceId: "w1", documentId: "d1", canEdit: options.canEdit ?? true,
    store: new DocumentStore({ canUndo: false, canRedo: false, isDirty: false, revision: 0 }), structureVersion: 0, commit,
    acceptResult: vi.fn(async () => {}), runAction: vi.fn(), refresh: vi.fn(async () => {}),
  };
  const spec = { kind: "reference" as const, catalogId: "items", readOnly: options.readOnly, allowCustom: options.custom };
  render(<DocumentContext.Provider value={context}><FieldRow node={{ ...node, spec }} /></DocumentContext.Provider>);
  return context;
}

beforeEach(() => {
  Object.defineProperty(HTMLDialogElement.prototype, "showModal", { configurable: true, value: function (this: HTMLDialogElement) { this.setAttribute("open", ""); } });
  vi.spyOn(api, "referenceCandidates").mockResolvedValue(page());
  vi.spyOn(api, "referenceCandidate").mockImplementation(async (_w, _d, query) => query.rawId === "1001" ? potion : unknown(query.rawId));
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); vi.useRealTimers(); Reflect.deleteProperty(HTMLDialogElement.prototype, "showModal"); });

// Exercise the picker's 180 ms debounce without spending wall time on each query.
async function flushCandidateSearch(milliseconds = 180) {
  await act(async () => { await vi.advanceTimersByTimeAsync(milliseconds); });
}

async function openPicker() {
  fireEvent.click(screen.getByRole("button", { name: /^Item/ }));
  if (vi.isFakeTimers()) {
    await flushCandidateSearch();
    return screen.getByRole("option", { name: /Potion/ });
  }
  return await screen.findByRole("option", { name: /Potion/ });
}

describe("reference field picker", () => {
  it("preserves an unknown raw id and supports keyboard selection with full attribution", async () => {
    vi.useFakeTimers();
    const context = renderField();
    expect(screen.getByRole("button", { name: /^Item/ }).textContent).toBe("9999");
    await openPicker();
    const search = screen.getByRole("combobox", { name: "Search" });
    expect(document.activeElement).toBe(search);
    fireEvent.keyDown(search, { key: "ArrowDown" });
    expect(search.getAttribute("aria-activedescendant")).toBe(screen.getByRole("option", { name: /Potion/ }).id);
    expect(screen.getByText("Restores health")).toBeTruthy();
    expect(screen.getAllByText(/Fixture contributors/)).toHaveLength(2);
    expect(screen.getAllByRole("link", { name: "CC0" })[0]?.getAttribute("href")).toBe("https://example.org/license");
    expect(screen.getByText("Icon attribution")).toBeTruthy();
    expect(document.querySelectorAll("img.reference-icon").length).toBeGreaterThan(0);
    await act(async () => { fireEvent.keyDown(search, { key: "Enter" }); });
    expect(context.commit).toHaveBeenCalledExactlyOnceWith("item/id", "1001");
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(document.activeElement).toBe(screen.getByRole("button", { name: /^Item/ }));
  });

  it("previews disabled choices and explains eligibility without committing", async () => {
    const context = renderField();
    await openPicker();
    fireEvent.keyDown(screen.getByRole("combobox", { name: "Search" }), { key: "End" });
    expect(screen.getByRole("option", { name: /Sword/ }).getAttribute("aria-disabled")).toBe("true");
    expect(screen.getAllByText("Requires a quest").length).toBeGreaterThan(0);
    expect((screen.getByRole("button", { name: "Use selected item" }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.keyDown(screen.getByRole("combobox", { name: "Search" }), { key: "Enter" });
    expect(context.commit).not.toHaveBeenCalled();
  });

  it("queries search, categories and pages on the server with the view revision", async () => {
    vi.useFakeTimers();
    vi.mocked(api.referenceCandidates).mockResolvedValue({ ...page(), total: 61 });
    renderField();
    await openPicker();
    fireEvent.change(screen.getByRole("combobox", { name: "Search" }), { target: { value: "pot" } });
    await flushCandidateSearch();
    expect(api.referenceCandidates).toHaveBeenLastCalledWith("w1", "d1", expect.objectContaining({ q: "pot", revision: 0, limit: 30 }));
    expect(screen.getByRole("option", { name: /Potion/ })).toBeTruthy();
    fireEvent.change(screen.getByLabelText("Category"), { target: { value: "consumable" } });
    await flushCandidateSearch();
    expect(api.referenceCandidates).toHaveBeenLastCalledWith("w1", "d1", expect.objectContaining({ q: "pot", category: "consumable", offset: 0 }));
    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    await flushCandidateSearch();
    expect(api.referenceCandidates).toHaveBeenLastCalledWith("w1", "d1", expect.objectContaining({ q: "pot", category: "consumable", offset: 30 }));
    fireEvent.change(screen.getByRole("combobox", { name: "Search" }), { target: { value: "ether" } });
    await flushCandidateSearch();
    expect(api.referenceCandidates).toHaveBeenLastCalledWith("w1", "d1", expect.objectContaining({ q: "ether", category: "consumable", offset: 0 }));
    expect(api.referenceCandidates).toHaveBeenCalledTimes(5);
  });

  it("previews custom exact ids without changing them, and only commits after confirmation", async () => {
    vi.mocked(api.referenceCandidate).mockImplementation(async (_w, _d, query) => unknown(query.rawId, true));
    const context = renderField({ custom: true });
    await openPicker();
    const rawId = "18446744073709551615";
    fireEvent.change(screen.getByLabelText("Raw ID"), { target: { value: rawId } });
    fireEvent.click(screen.getByRole("button", { name: "Preview raw ID" }));
    await waitFor(() => expect(api.referenceCandidate).toHaveBeenLastCalledWith("w1", "d1", expect.objectContaining({ rawId })));
    await screen.findByRole("heading", { name: rawId });
    expect(context.commit).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Use selected item" }));
    await waitFor(() => expect(context.commit).toHaveBeenCalledWith("item/id", rawId));
  });

  it("keeps the dialog open when the server rejects a selection", async () => {
    const context = renderField();
    vi.mocked(context.commit).mockResolvedValue(false);
    await openPicker();
    fireEvent.click(screen.getByRole("option", { name: /Potion/ }));
    fireEvent.click(screen.getByRole("button", { name: "Use selected item" }));
    expect((await screen.findByRole("alert")).textContent).toBe("The server rejected this selection. Choose another item or refresh the document.");
    expect(context.commit).toHaveBeenCalledExactlyOnceWith("item/id", "1001");
    expect(screen.getByRole("dialog")).toBeTruthy();
  });

  it("offers raw-id lookup when candidate search fails", async () => {
    vi.mocked(api.referenceCandidates).mockRejectedValue(new Error("Search unavailable"));
    const context = renderField();
    fireEvent.click(screen.getByRole("button", { name: /^Item/ }));
    expect((await screen.findByRole("alert")).textContent).toBe("Search unavailable");
    fireEvent.change(screen.getByLabelText("Raw ID"), { target: { value: "1001" } });
    fireEvent.click(screen.getByRole("button", { name: "Preview raw ID" }));
    await screen.findByRole("heading", { name: "Potion" });
    fireEvent.click(screen.getByRole("button", { name: "Use selected item" }));
    await waitFor(() => expect(context.commit).toHaveBeenCalledWith("item/id", "1001"));
  });

  it("ignores late results from an older search", async () => {
    vi.useFakeTimers();
    let resolveOld!: (result: ReferenceCandidatePage) => void;
    vi.mocked(api.referenceCandidates).mockImplementationOnce(() => new Promise((resolve) => { resolveOld = resolve; }));
    renderField();
    fireEvent.click(screen.getByRole("button", { name: /^Item/ }));
    await flushCandidateSearch();
    expect(api.referenceCandidates).toHaveBeenCalledTimes(1);
    vi.mocked(api.referenceCandidates).mockResolvedValue(page([sword]));
    fireEvent.change(screen.getByRole("combobox", { name: "Search" }), { target: { value: "Sword" } });
    await flushCandidateSearch();
    expect(screen.getByRole("option", { name: /Sword/ })).toBeTruthy();
    // Settle the obsolete request before checking that it cannot replace current results.
    await act(async () => { resolveOld(page([potion])); });
    expect(screen.getByRole("option", { name: /Sword/ })).toBeTruthy();
    expect(screen.queryByRole("option", { name: /Potion/ })).toBeNull();
  });

  it("refreshes and closes a stale picker without submitting anything", async () => {
    vi.mocked(api.referenceCandidates).mockRejectedValue(new ApiError("View is stale", 409, { code: "stale" }));
    const context = renderField();
    fireEvent.click(screen.getByRole("button", { name: /^Item/ }));
    await waitFor(() => expect(context.refresh).toHaveBeenCalled());
    expect(context.commit).not.toHaveBeenCalled();
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("cancel and Escape preserve the value and restore focus", async () => {
    const context = renderField();
    await openPicker();
    fireEvent(screen.getByRole("dialog"), new Event("cancel", { cancelable: true }));
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(context.commit).not.toHaveBeenCalled();
    expect(document.activeElement).toBe(screen.getByRole("button", { name: /^Item/ }));
  });

  it.each([{ readOnly: true }, { canEdit: false }])("browses read-only details and attribution without a commit path (%j)", async (options) => {
    const context = renderField(options);
    await openPicker();
    expect(screen.getByRole("heading", { name: "Browse items" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Use selected item" })).toBeNull();
    const search = screen.getByRole("combobox", { name: "Search" });
    fireEvent.keyDown(search, { key: "ArrowDown" });
    expect(screen.getByText("Restores health")).toBeTruthy();
    expect(screen.getAllByText(/Fixture contributors/)).toHaveLength(2);
    fireEvent.keyDown(search, { key: "Enter" });
    fireEvent.click(screen.getByRole("option", { name: /Sword/ }));
    expect(screen.getByRole("heading", { name: "Sword" })).toBeTruthy();
    fireEvent.change(screen.getByLabelText("Raw ID"), { target: { value: "1001" } });
    fireEvent.click(screen.getByRole("button", { name: "Preview raw ID" }));
    await screen.findByRole("heading", { name: "Potion" });
    expect(context.commit).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Close" }));
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(document.activeElement).toBe(screen.getByRole("button", { name: /^Item/ }));
  });

  it("sorts by declared keys and resets paging while retaining the category", async () => {
    vi.useFakeTimers();
    vi.mocked(api.referenceCandidates).mockResolvedValue({ ...page(), total: 61 });
    renderField({ canEdit: false });
    await openPicker();
    fireEvent.change(screen.getByLabelText("Category"), { target: { value: "consumable" } });
    await flushCandidateSearch();
    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    await flushCandidateSearch();
    expect(api.referenceCandidates).toHaveBeenLastCalledWith("w1", "d1", expect.objectContaining({ category: "consumable", offset: 30 }));
    fireEvent.change(screen.getByLabelText("Sort by"), { target: { value: "Name" } });
    await flushCandidateSearch();
    expect(api.referenceCandidates).toHaveBeenLastCalledWith("w1", "d1", expect.objectContaining({ category: "consumable", sortBy: "Name", descending: false, offset: 0 }));
    fireEvent.click(screen.getByLabelText("Descending"));
    await flushCandidateSearch();
    expect(api.referenceCandidates).toHaveBeenLastCalledWith("w1", "d1", expect.objectContaining({ category: "consumable", sortBy: "Name", descending: true, offset: 0 }));
    expect(screen.getByRole("option", { name: /Potion/ })).toBeTruthy();
  });

  it("offers browse-only mode inside a read-only table", async () => {
    const restore = stubViewport();
    try {
      const result = renderTable(table([{ key: "one", cells: { value: node } }], ["value"]), { canEdit: false });
      await openPicker();
      fireEvent.keyDown(screen.getByRole("combobox", { name: "Search" }), { key: "Home" });
      fireEvent.keyDown(screen.getByRole("combobox", { name: "Search" }), { key: "Enter" });
      expect(result.commit).not.toHaveBeenCalled();
      expect(screen.queryByRole("button", { name: "Use selected item" })).toBeNull();
    } finally { restore(); }
  });

  it("opens the same picker inside a virtualised table cell", async () => {
    const restore = stubViewport();
    try {
      const result = renderTable(table([{ key: "one", cells: { value: node } }], ["value"]));
      await openPicker();
      fireEvent.keyDown(screen.getByRole("combobox", { name: "Search" }), { key: "Home" });
      fireEvent.keyDown(screen.getByRole("combobox", { name: "Search" }), { key: "Enter" });
      await waitFor(() => expect(result.commit).toHaveBeenCalledWith("item/id", "1001"));
      await waitFor(() => expect(screen.getByRole("button", { name: /^Item/ }).textContent).toContain("Potion"));
    } finally { restore(); }
  });
});
