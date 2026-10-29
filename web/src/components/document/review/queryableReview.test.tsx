import { act, cleanup, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { actionReview, type SemanticChangeLog } from "../../../api/actionReview";
import { api, ApiError } from "../../../api/client";
import type { EditResult, QueryablePage, ViewNode } from "../../../api/protocol";
import { deferred } from "../../../test/deferred";
import { ViewRenderer } from "../ViewRenderer";
import { ChangesPanel } from "./ChangesPanel";
import { clearReviewClients, renderDocument } from "./reviewTestUtils";

afterEach(() => { cleanup(); clearReviewClients(); vi.restoreAllMocks(); vi.unstubAllGlobals(); });

const collectionId = "inventory/%2F?# +";
const rowKey = "slot/%2F?# +127";
const valueId = "quantity/%2F?# +127";
const label = `Inventory / ${rowKey} / Quantity`;
const change = { valueId, label, path: ["section:Inventory", "tab:Items", `row:${rowKey}`],
  originalValue: "2", currentValue: "10", originalExists: true, currentExists: true, canRevert: true,
  target: { collectionId, rowKey, query: { text: "potion + %2F", filters: { kind: "item/ +?#" }, sortColumn: "name/#?",
    descending: true, offset: 0, cursor: "opaque/%2F?# +", limit: 200, includeEmpty: true } } };
const log: SemanticChangeLog = { changes: [change], complete: true, warnings: [], revision: 1 };
const columns = [{ key: "name", label: "Name" }, { key: "count", label: "Count" }];
const page: QueryablePage = { collectionId, columns, totalCount: 500, filteredCount: 500, offset: 200, limit: 200, revision: 1,
  rows: [{ key: rowKey, isEmpty: true, cells: {
    name: { type: "text", text: "Empty potion slot" },
    count: { type: "field", valueId, label: "Slot quantity", spec: { kind: "integer", min: "0", max: "99" }, value: "10" },
  } }] };
const view: ViewNode = { type: "section", title: "Inventory", collapsed: true, children: [{ type: "tabs", tabs: [
  { title: "Stats", children: [] }, { title: "Items", children: [{ type: "queryableTable", collectionId, title: "Inventory slots",
    columns, totalCount: 500, emptyRows: { labelColumnKey: "name" } }] },
] }] };
const reverted: EditResult = { ok: true, errors: [], changes: [{ valueId, oldValue: "10", newValue: "2" }], warnings: [],
  structureChanged: false, state: { revision: 2, isDirty: false, canUndo: true, canRedo: false } };

async function openReview() {
  fireEvent.click(screen.getByText("Changes against original"));
  await screen.findByRole("button", { name: `Jump to ${label}` });
}
const jump = () => fireEvent.click(screen.getByRole("button", { name: `Jump to ${label}` }));

describe("bounded queryable semantic review", () => {
  it("renders the collection, stable row, field label and original/current existence", async () => {
    vi.spyOn(actionReview, "changes").mockResolvedValue({ ...log, changes: [change,
      { ...change, valueId: "removed", label: "Removed slot", currentExists: false, canRevert: false, target: undefined },
      { ...change, valueId: "added", label: "Added slot", originalExists: false, canRevert: false },
    ] });
    renderDocument(<ChangesPanel />);
    await openReview();
    const row = screen.getByText(label).closest("tr")!;
    expect(within(row).getAllByRole("cell").slice(0, 2).map(cell => cell.textContent)).toEqual(["2", "10"]);
    expect(screen.getAllByText("(absent)")).toHaveLength(2);
    expect(screen.getByRole("button", { name: "Jump to Removed slot" })).toHaveProperty("disabled", true);
    expect(screen.getByRole("button", { name: "Revert Added slot" })).toHaveProperty("disabled", true);
  });

  it.each([
    { complete: false, warnings: [] },
    { complete: false, warnings: ["Semantic review truncated: row cap", "Collection could not be read"] },
    { complete: true, warnings: ["Semantic review truncated: byte cap"] },
  ])("keeps empty partial coverage explicit: %j", async coverage => {
    vi.spyOn(actionReview, "changes").mockResolvedValue({ ...log, ...coverage, changes: [] });
    renderDocument(<ChangesPanel />);
    fireEvent.click(screen.getByText("Changes against original"));
    expect((await screen.findByText(/Review coverage is incomplete/)).getAttribute("role")).toBe("status");
    expect(screen.getByText("No proven changes in the reviewed portion.")).toBeTruthy();
    for (const warning of coverage.warnings) expect(screen.getByText(warning)).toBeTruthy();
    expect(screen.queryByText("No semantic changes.")).toBeNull();
  });

  it("opens parents, loads the exact encoded query/revision, reveals an empty row and focuses its field without writes", async () => {
    vi.spyOn(actionReview, "changes").mockResolvedValue(log);
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify(page), { headers: { "content-type": "application/json" } }));
    vi.stubGlobal("fetch", fetch);
    const { context } = renderDocument(<><ChangesPanel /><ViewRenderer node={view} /></>);
    await openReview();
    jump();
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("textbox", { name: "Slot quantity" })));
    expect(screen.getByRole("tab", { name: "Items" }).getAttribute("aria-selected")).toBe("true");
    expect(screen.getByRole("button", { name: /^Details for Empty slot/ }).getAttribute("aria-expanded")).toBe("true");
    expect(screen.getByText("Showing empty slots")).toBeTruthy();
    expect(fetch).toHaveBeenCalledTimes(1);
    const url = new URL(fetch.mock.calls[0]![0] as string, "http://localhost");
    expect(url.pathname).toBe("/api/workspaces/w1/documents/d1/collections");
    expect(Object.fromEntries(url.searchParams)).toEqual({ collectionId, q: change.target.query.text,
      filters: JSON.stringify(change.target.query.filters), sort: change.target.query.sortColumn,
      descending: "true", offset: "0", cursor: change.target.query.cursor, limit: "200", includeEmpty: "true", revision: "1" });
    expect(context.commit).not.toHaveBeenCalled();
    expect(context.runAction).not.toHaveBeenCalled();
    expect(context.acceptResult).not.toHaveBeenCalled();
  });

  it("preserves an offset-only target and lets subsequent search reset its paging", async () => {
    vi.spyOn(actionReview, "changes").mockResolvedValue({ ...log, changes: [{ ...change,
      target: { ...change.target, query: { offset: 200, limit: 200, includeEmpty: true } } }] });
    const query = vi.spyOn(api, "queryCollection").mockResolvedValue(page);
    renderDocument(<><ChangesPanel /><ViewRenderer node={view} /></>);
    await openReview();
    jump();
    const search = await screen.findByRole("searchbox", { name: "Search Inventory slots" });
    await waitFor(() => expect(query).toHaveBeenCalledExactlyOnceWith("w1", "d1", collectionId,
      { offset: 200, limit: 200, includeEmpty: true, q: undefined, sort: undefined }, 1));
    fireEvent.change(search, { target: { value: "ether" } });
    await waitFor(() => expect(query).toHaveBeenLastCalledWith("w1", "d1", collectionId,
      expect.objectContaining({ q: "ether", offset: 0, cursor: undefined, includeEmpty: true }), 1));
  });

  it.each(["409", "wrong revision", "missing row", "missing field", "query failure"])("surfaces %s without publishing a successful jump", async failure => {
    const changes = vi.spyOn(actionReview, "changes").mockResolvedValue(log);
    const query = vi.spyOn(api, "queryCollection");
    if (failure === "409") query.mockRejectedValue(new ApiError("Review page is stale", 409, { code: "stale" }));
    else if (failure === "query failure") query.mockRejectedValue(new Error("Collection unavailable"));
    else query.mockResolvedValue({ ...page, ...(failure === "missing row" ? { rows: [] }
      : failure === "missing field" ? { rows: [{ key: rowKey, cells: {} }] } : { revision: 2 }) });
    const { context } = renderDocument(<><ChangesPanel /><ViewRenderer node={view} /></>);
    await openReview();
    jump();
    expect((await screen.findByRole("alert")).textContent).toMatch(/stale|out of date|not found|unavailable/);
    await waitFor(() => expect(changes).toHaveBeenCalledTimes(2));
    expect(screen.queryByRole("textbox", { name: "Slot quantity" })).toBeNull();
    expect(context.commit).not.toHaveBeenCalled();
    expect(context.runAction).not.toHaveBeenCalled();
    if (failure === "409" || failure === "wrong revision") expect(context.refresh).toHaveBeenCalledTimes(1);
  });

  it("blocks stale targets until a refreshed review and document agree", async () => {
    const changes = vi.spyOn(actionReview, "changes").mockResolvedValue(log);
    const query = vi.spyOn(api, "queryCollection").mockRejectedValueOnce(new ApiError("Review page is stale", 409));
    const { context, store } = renderDocument(<><ChangesPanel /><ViewRenderer node={view} /></>);
    vi.mocked(context.refresh).mockImplementation(async () => { store.reset({ ...store.state, revision: 2 }); });
    await openReview();
    jump();
    await screen.findByRole("alert");
    await waitFor(() => expect(query).toHaveBeenCalledTimes(1));
    expect(screen.getByRole("button", { name: `Jump to ${label}` })).toHaveProperty("disabled", true);
    changes.mockResolvedValue({ ...log, revision: 2 });
    // A new content generation obtains a current review before another explicit jump.
    act(() => store.reset({ ...store.state, revision: 2 }));
    await waitFor(() => expect(screen.getByRole("button", { name: `Jump to ${label}` })).toHaveProperty("disabled", false));
    query.mockResolvedValue({ ...page, revision: 2 });
    jump();
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("textbox", { name: "Slot quantity" })));
    expect(query.mock.calls.at(-1)?.[4]).toBe(2);
  });

  it("ignores a late page after another mutation and refreshes instead of focusing obsolete controls", async () => {
    const pending = deferred<QueryablePage>();
    vi.spyOn(actionReview, "changes").mockResolvedValue(log);
    vi.spyOn(api, "queryCollection").mockReturnValue(pending.promise);
    const { context, store } = renderDocument(<><ChangesPanel /><ViewRenderer node={view} /></>);
    await openReview();
    jump();
    act(() => store.apply(reverted));
    await act(async () => pending.resolve(page));
    expect((await screen.findByRole("alert")).textContent).toContain("out of date");
    expect(context.refresh).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole("textbox", { name: "Slot quantity" })).toBeNull();
  });

  it.each([valueId, "raw/quantity"])("reverting alias %s clears both entries, refreshes pages, and reloads review after undo/redo", async aliasId => {
    const changes = vi.spyOn(actionReview, "changes").mockResolvedValue({ ...log,
      changes: [change, { ...change, valueId: "raw/quantity", label: "Raw quantity", target: undefined }] });
    const revert = vi.spyOn(actionReview, "revert").mockResolvedValue(reverted);
    const query = vi.spyOn(api, "queryCollection").mockResolvedValue(page);
    const { store } = renderDocument(<><ChangesPanel /><ViewRenderer node={view} /></>);
    await openReview();
    jump();
    await screen.findByRole("textbox", { name: "Slot quantity" });
    changes.mockResolvedValue({ ...log, changes: [], revision: 2 });
    query.mockResolvedValue({ ...page, revision: 2 });
    fireEvent.click(screen.getByRole("button", { name: `Revert ${aliasId === valueId ? label : "Raw quantity"}` }));
    await screen.findByText("No semantic changes.");
    expect(revert).toHaveBeenCalledExactlyOnceWith("w1", "d1", aliasId, 1);
    expect(screen.queryByRole("button", { name: "Revert Raw quantity" })).toBeNull();
    expect(screen.queryByRole("button", { name: `Revert ${label}` })).toBeNull();
    await waitFor(() => expect(query.mock.calls.at(-1)?.[4]).toBe(2));
    changes.mockResolvedValue({ ...log, revision: 3 });
    act(() => store.apply({ ...reverted, state: { ...reverted.state, revision: 3 } }));
    await screen.findByRole("button", { name: `Revert ${label}` });
    changes.mockResolvedValue({ ...log, changes: [], revision: 4 });
    act(() => store.apply({ ...reverted, state: { ...reverted.state, revision: 4 } }));
    await screen.findByText("No semantic changes.");
  });
});
