import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import type { ReactNode } from "react";
import { actionReview, type ActionPreview, type SemanticChange, type SemanticChangeLog } from "../../../api/actionReview";
import type { EditResult } from "../../../api/protocol";
import { DocumentContext, DocumentStore, type DocumentContextValue } from "../../../state/documentStore";
import { ActionButton } from "../ActionButton";
import { api } from "../../../api/client";
import { ViewRenderer } from "../ViewRenderer";
import { TableSelectionContext, useRowSelection } from "../table/selection";
import { ChangesPanel } from "./ChangesPanel";
import { ReviewNavigationProvider } from "./navigation";
import { stubViewport } from "../table/tableTestUtils";

beforeAll(() => {
  HTMLDialogElement.prototype.showModal = function () { this.open = true; };
  HTMLDialogElement.prototype.close = function () { this.open = false; };
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

const change: SemanticChange = { valueId: "quantity", label: "Potion quantity", path: ["section:Inventory", "tab:Items"],
  originalValue: "2", currentValue: "10", originalExists: true, currentExists: true, canRevert: true };
const log: SemanticChangeLog = { changes: [change, { ...change, valueId: "total", label: "Total", canRevert: false }], complete: true, warnings: [], revision: 1 };
const preview: ActionPreview = { planId: "p1", revision: 1, scope: { rowKeys: ["a"] }, plan: {
  changes: [{ valueId: "quantity", label: "Potion quantity", oldValue: "2", newValue: "10", derived: false, oldExists: true, newExists: true },
    { valueId: "total", label: "Total", oldValue: "6", newValue: "14", derived: true, oldExists: true, newExists: true }],
  skippedRows: [{ rowKey: "locked", reason: "Already full" }], warnings: ["Check dependencies"], errors: [],
} };
const result: EditResult = { ok: true, errors: [], changes: [], warnings: [], structureChanged: false,
  state: { canUndo: true, canRedo: false, isDirty: true, revision: 2 } };

function renderDocument(children: ReactNode, canEdit = true) {
  const store = new DocumentStore({ canUndo: true, canRedo: false, isDirty: true, revision: 1 });
  const context: DocumentContextValue = { workspaceId: "w1", documentId: "d1", canEdit, store, structureVersion: 0,
    refresh: vi.fn(async () => {}), commit: vi.fn(async () => true), runAction: vi.fn(async () => result),
    acceptResult: vi.fn(async outcome => { store.apply(outcome); }),
  };
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const rendered = render(<QueryClientProvider client={client}><DocumentContext.Provider value={context}>
    <ReviewNavigationProvider>{children}</ReviewNavigationProvider>
  </DocumentContext.Provider></QueryClientProvider>);
  return { ...rendered, context, store };
}

function SelectedAction() {
  const selection = useRowSelection([{ key: "a", cells: {} }, { key: "b", cells: {} }]);
  return <TableSelectionContext.Provider value={selection}>
    <button onClick={() => selection.toggle("a")}>Select a</button>
    <ActionButton node={{ type: "action", actionId: "refill", label: "Refill", supportsPlanning: true }} />
  </TableSelectionContext.Provider>;
}

describe("action preview", () => {
  it("sends the complete filtered query and exclusions from a server table and shows the resolved count", async () => {
    const columns = [{ key: "name", label: "Name" }, { key: "action", label: "Action" }];
    vi.spyOn(api, "queryCollection").mockResolvedValue({ collectionId: "items", columns, totalCount: 100_000,
      filteredCount: 60, offset: 0, limit: 50, revision: 1, rows: [
        { key: "a", cells: { name: { type: "text", text: "Potion" }, action: { type: "action", actionId: "refill", label: "Refill", supportsPlanning: true } } },
        { key: "b", cells: { name: { type: "text", text: "Ether" } } },
      ] });
    const plan = vi.spyOn(actionReview, "plan").mockResolvedValue({ ...preview,
      scope: { rowKeys: Array.from({ length: 58 }, (_, i) => `row-${i}`) } });
    const apply = vi.spyOn(actionReview, "apply").mockResolvedValue(result);
    renderDocument(<ViewRenderer node={{ type: "queryableTable", collectionId: "items", title: "Items", columns, totalCount: 100_000 }} />);
    fireEvent.change(screen.getByRole("searchbox", { name: "Search Items" }), { target: { value: "potion" } });
    fireEvent.change(screen.getByLabelText("Filter Name"), { target: { value: "healing" } });
    fireEvent.change(screen.getByLabelText("Sort rows"), { target: { value: "name" } });
    fireEvent.click(screen.getByRole("button", { name: "Ascending" }));
    fireEvent.click(await screen.findByRole("button", { name: "Select all 60 filtered rows" }));
    fireEvent.click(screen.getByRole("checkbox", { name: "Select Ether" }));
    expect(screen.getByText("59 selected")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: /Refill/ }));
    await screen.findByText("58 selected rows");
    expect(plan).toHaveBeenCalledWith("w1", "d1", "refill", {}, { queryScope: {
      collectionId: "items", query: { text: "potion", filters: { name: "healing" }, sortColumn: "name", descending: true },
      revision: 1, excludedRowKeys: ["b"],
    } }, 1);
    fireEvent.click(screen.getByRole("checkbox", { name: "Select Ether" }));
    expect(screen.getByRole("alert").textContent).toContain("selection changed");
    expect(screen.queryByRole("button", { name: "Apply" })).toBeNull();
    expect(apply).not.toHaveBeenCalled();
  }, 15_000); // full server-table flow; exceeds the 5 s default on slow Windows CI runners

  it("uses F3 selection, shows named and derived effects, warnings and skipped rows, and applies the retained token", async () => {
    const plan = vi.spyOn(actionReview, "plan").mockResolvedValue(preview);
    const apply = vi.spyOn(actionReview, "apply").mockResolvedValue(result);
    const { context } = renderDocument(<SelectedAction />);
    fireEvent.click(screen.getByRole("button", { name: "Select a" }));
    fireEvent.click(screen.getByRole("button", { name: "Refill" }));
    expect(await screen.findByRole("dialog", { name: "Preview: Refill" })).toBeTruthy();
    await screen.findByText("Potion quantity");
    expect(plan).toHaveBeenCalledWith("w1", "d1", "refill", {}, { rowKeys: ["a"] }, 1);
    expect(screen.getByText("1 selected row")).toBeTruthy();
    expect(screen.getByText(/derived/)).toBeTruthy();
    expect(screen.getByText("Check dependencies")).toBeTruthy();
    expect(screen.getByText(/locked: Already full/)).toBeTruthy();
    expect(apply).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Apply" }));
    await waitFor(() => expect(apply).toHaveBeenCalledWith("w1", "d1", "p1", 1));
    await waitFor(() => expect(context.acceptResult).toHaveBeenCalledWith(result));
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("cancel never applies, and an empty selection is explicitly empty", async () => {
    const plan = vi.spyOn(actionReview, "plan").mockResolvedValue({ ...preview, scope: { rowKeys: [] } });
    const apply = vi.spyOn(actionReview, "apply").mockResolvedValue(result);
    renderDocument(<SelectedAction />);
    fireEvent.click(screen.getByRole("button", { name: "Refill" }));
    await screen.findByText("Potion quantity");
    expect(plan).toHaveBeenCalledWith("w1", "d1", "refill", {}, { rowKeys: [] }, 1);
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(apply).not.toHaveBeenCalled();
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("invalidates a displayed preview after a scalar edit", async () => {
    vi.spyOn(actionReview, "plan").mockResolvedValue(preview);
    const { store } = renderDocument(<SelectedAction />);
    fireEvent.click(screen.getByRole("button", { name: "Select a" }));
    fireEvent.click(screen.getByRole("button", { name: "Refill" }));
    await screen.findByText("Potion quantity");
    act(() => store.apply(result));
    expect(screen.getByRole("alert").textContent).toContain("changed");
    expect(screen.queryByRole("button", { name: "Apply" })).toBeNull();
  });

  it("shows planning errors and failed applies, and lets read-only users preview", async () => {
    vi.spyOn(actionReview, "plan").mockResolvedValue({ ...preview, planId: undefined, plan: { ...preview.plan, errors: [{ valueId: "a", message: "Unavailable" }] } });
    const rendered = renderDocument(<SelectedAction />, false);
    fireEvent.click(screen.getByRole("button", { name: "Refill" }));
    expect(await screen.findByRole("alert")).toHaveProperty("textContent", "a: Unavailable");
    expect(screen.queryByRole("button", { name: "Apply" })).toBeNull();
    rendered.unmount();

    vi.spyOn(actionReview, "plan").mockResolvedValue(preview);
    vi.spyOn(actionReview, "apply").mockResolvedValue({ ...result, ok: false, stale: true, errors: [{ valueId: "", message: "Preview is stale" }] });
    renderDocument(<SelectedAction />);
    fireEvent.click(screen.getByRole("button", { name: "Refill" }));
    await screen.findByText("Potion quantity");
    fireEvent.click(screen.getByRole("button", { name: "Apply" }));
    expect(await screen.findByText("Preview is stale")).toBeTruthy();
  });

  it("preserves immediate invocation for legacy actions", async () => {
    const plan = vi.spyOn(actionReview, "plan");
    const { context } = renderDocument(<ActionButton node={{ type: "action", actionId: "legacy", label: "Legacy" }} />);
    fireEvent.click(screen.getByRole("button", { name: "Legacy" }));
    await waitFor(() => expect(context.runAction).toHaveBeenCalledWith("legacy", {}));
    expect(plan).not.toHaveBeenCalled();
  });

  it("collects parameters before planning and disables apply on read-only documents", async () => {
    const plan = vi.spyOn(actionReview, "plan").mockResolvedValue({ ...preview, scope: {} });
    renderDocument(<ActionButton node={{ type: "action", actionId: "refill", label: "Refill", supportsPlanning: true,
      parameters: [{ type: "field", valueId: "quantity", label: "Target quantity", spec: { kind: "integer", min: "0", max: "99" }, value: "10" }] }} />, false);
    fireEvent.click(screen.getByRole("button", { name: "Refill" }));
    expect(plan).not.toHaveBeenCalled();
    const input = screen.getByRole("textbox", { name: "Target quantity" });
    fireEvent.change(input, { target: { value: "15" } });
    fireEvent.blur(input);
    fireEvent.click(screen.getByRole("button", { name: "Preview" }));
    await screen.findByText("Potion quantity");
    expect(plan).toHaveBeenCalledWith("w1", "d1", "refill", { quantity: "15" }, {}, 1);
    expect((screen.getByRole("button", { name: "Apply" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("binds reference parameters of planned actions so the picker is usable", async () => {
    const candidate = vi.spyOn(api, "referenceCandidate").mockResolvedValue({ id: "World/COOLING", label: "Cooling Springs" } as never);
    renderDocument(<ActionButton node={{ type: "action", actionId: "set-world", label: "Set world", supportsPlanning: true,
      parameters: [{ type: "field", valueId: "world", label: "World", spec: { kind: "reference", catalogId: "astro_worlds" } as never, value: "World/COOLING" }] }} />);
    fireEvent.click(screen.getByRole("button", { name: "Set world" }));
    const trigger = screen.getByLabelText("World") as HTMLButtonElement;
    expect(trigger.textContent).toContain("World/COOLING");
    expect(trigger.disabled).toBe(false);
    await waitFor(() => expect(candidate).toHaveBeenCalledWith("w1", "d1",
      expect.objectContaining({ valueId: "world", actionId: "set-world", rawId: "World/COOLING" })));
  });

  it("requires another preview when the table selection changes", async () => {
    vi.spyOn(actionReview, "plan").mockResolvedValue(preview);
    renderDocument(<SelectedAction />);
    fireEvent.click(screen.getByRole("button", { name: "Select a" }));
    fireEvent.click(screen.getByRole("button", { name: "Refill" }));
    await screen.findByText("Potion quantity");
    fireEvent.click(screen.getByRole("button", { name: "Select a" }));
    expect(screen.queryByRole("button", { name: "Apply" })).toBeNull();
    expect(screen.getByRole("alert").textContent).toContain("selection changed");
  });
});

describe("semantic changes panel", () => {
  it("loads changes against original, filters them and reverts with the log revision", async () => {
    vi.spyOn(actionReview, "changes").mockResolvedValue(log);
    const revert = vi.spyOn(actionReview, "revert").mockResolvedValue(result);
    const { context } = renderDocument(<ChangesPanel />);
    fireEvent.click(screen.getByText("Changes against original"));
    await screen.findByText("Potion quantity");
    expect((screen.getByRole("button", { name: "Revert Total" }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.change(screen.getByRole("searchbox"), { target: { value: "Potion" } });
    expect(screen.queryByRole("button", { name: "Revert Total" })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Revert Potion quantity" }));
    await waitFor(() => expect(revert).toHaveBeenCalledWith("w1", "d1", "quantity", 1));
    await waitFor(() => expect(context.acceptResult).toHaveBeenCalledWith(result));
  });

  it("refreshes after undo/redo and reports incomplete coverage", async () => {
    const changes = vi.spyOn(actionReview, "changes").mockResolvedValue(log);
    const { store } = renderDocument(<ChangesPanel />);
    fireEvent.click(screen.getByText("Changes against original"));
    await screen.findByText("Potion quantity");
    changes.mockResolvedValue({ changes: [], complete: false, warnings: ["Memory limit reached"], revision: 2 });
    act(() => store.apply(result));
    await screen.findByText("No semantic changes.");
    expect(screen.getByText("Memory limit reached")).toBeTruthy();
    changes.mockResolvedValue({ ...log, revision: 3 });
    act(() => store.apply({ ...result, state: { ...result.state, revision: 3 } }));
    await screen.findByText("Potion quantity");
  });

  it("jumps into a collapsed section and an inactive tab, focusing the field", async () => {
    vi.spyOn(actionReview, "changes").mockResolvedValue(log);
    renderDocument(<><ChangesPanel /><ViewRenderer node={{ type: "section", title: "Inventory", collapsed: true, children: [
      { type: "tabs", tabs: [{ title: "Stats", children: [] }, { title: "Items", children: [
        { type: "field", valueId: "quantity", label: "Potion count", spec: { kind: "integer", min: "0", max: "99" }, value: "10" },
      ] }] },
    ] }} /></>);
    fireEvent.click(screen.getByText("Changes against original"));
    await screen.findByText("Potion quantity");
    fireEvent.click(screen.getByRole("button", { name: "Jump to Potion quantity" }));
    await screen.findByRole("tab", { name: "Items" });
    await waitFor(() => expect(document.activeElement?.getAttribute("aria-labelledby")).toBe(screen.getByText("Potion count").id));
  });

  it("jumps into a lazy section and virtualised row", async () => {
    const restore = stubViewport();
    vi.spyOn(actionReview, "changes").mockResolvedValue({ ...log, changes: [{ ...change, path: ["section:lazy-items", "row:a"] }] });
    const { api } = await import("../../../api/client");
    vi.spyOn(api, "expand").mockResolvedValue([{ type: "table", title: "Items", columns: [{ key: "count", label: "Count" }],
      rows: [{ key: "a", cells: { count: { type: "field", valueId: "quantity", label: "Potion count", spec: { kind: "integer", min: "0", max: "99" }, value: "10" } } }] }]);
    try {
      renderDocument(<><ChangesPanel /><ViewRenderer node={{ type: "section", title: "Items", lazyId: "lazy-items", collapsed: true, children: [] }} /></>);
      fireEvent.click(screen.getByText("Changes against original"));
      await screen.findByText("Potion quantity");
      fireEvent.click(screen.getByRole("button", { name: "Jump to Potion quantity" }));
      await screen.findByRole("table", { name: "Items" });
      await waitFor(() => expect(document.activeElement?.getAttribute("aria-labelledby")).toBe(screen.getByText("Potion count").id));
    } finally { restore(); }
  });

  it("reveals a hidden empty slot when jumping to its stable field identity", async () => {
    const restore = stubViewport();
    vi.spyOn(actionReview, "changes").mockResolvedValue({ ...log, changes: [{ ...change, path: ["row:slot/127"] }] });
    try {
      renderDocument(<><ChangesPanel /><ViewRenderer node={{ type: "table", title: "Inventory",
        emptyRows: { labelColumnKey: "name" }, columns: [{ key: "name", label: "Name" }, { key: "count", label: "Count" }],
        rows: [{ key: "slot/127", isEmpty: true, cells: {
          name: { type: "text", text: "Unknown ID 0xFFFFFFFF" },
          count: { type: "field", valueId: "quantity", label: "Slot quantity", spec: { kind: "integer", min: "0", max: "99" }, value: "0" },
        } }],
      }} /></>);
      expect(screen.getByText("1 empty slots hidden")).toBeTruthy();
      fireEvent.click(screen.getByText("Changes against original"));
      await screen.findByText("Potion quantity");
      fireEvent.click(screen.getByRole("button", { name: "Jump to Potion quantity" }));
      await screen.findByText("Showing empty slots");
      await waitFor(() => expect(document.activeElement?.getAttribute("aria-labelledby")).toBe(screen.getByText("Slot quantity").id));
    } finally { restore(); }
  });

  it("follows nested tab paths when titles also occur at another level", async () => {
    vi.spyOn(actionReview, "changes").mockResolvedValue({ ...log, changes: [{ ...change, path: ["tab:Character", "tab:Inventory"] }] });
    renderDocument(<><ChangesPanel /><ViewRenderer node={{ type: "tabs", tabs: [
      { title: "Inventory", children: [] },
      { title: "Character", children: [{ type: "tabs", tabs: [
        { title: "Stats", children: [] }, { title: "Inventory", children: [
          { type: "field", valueId: "quantity", label: "Potion count", spec: { kind: "integer", min: "0", max: "99" }, value: "10" },
        ] },
      ] }] },
    ] }} /></>);
    fireEvent.click(screen.getByText("Changes against original"));
    await screen.findByText("Potion quantity");
    fireEvent.click(screen.getByRole("button", { name: "Jump to Potion quantity" }));
    await screen.findByText("Potion count");
    await waitFor(() => expect(document.activeElement?.getAttribute("aria-labelledby")).toBe(screen.getByText("Potion count").id));
  });

  it("bounds the rendered change list with paging", async () => {
    vi.spyOn(actionReview, "changes").mockResolvedValue({ ...log, changes: Array.from({ length: 101 }, (_, index) => ({ ...change, valueId: `item/${index}`, label: `Item ${index}` })) });
    renderDocument(<ChangesPanel />);
    fireEvent.click(screen.getByText("Changes against original"));
    await screen.findByText("Item 0");
    expect(screen.queryByText("Item 100")).toBeNull();
    expect(screen.getByText("Page 1 of 2")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    expect(screen.getByText("Item 100")).toBeTruthy();
    expect(screen.queryByText("Item 0")).toBeNull();
  });
});
