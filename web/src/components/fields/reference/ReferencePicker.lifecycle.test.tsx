import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { api, ApiError } from "../../../api/client";
import type { ReferenceCandidate, ReferenceCandidatePage } from "../../../api/protocol";
import { DocumentContext, DocumentStore } from "../../../state/documentStore";
import { ReferencePicker } from "./ReferencePicker";

const potion: ReferenceCandidate = { rawId: "1001", label: "Potion",
  eligibility: { available: true, eligible: true, selectable: true, reasons: [] } };
const page: ReferenceCandidatePage = { catalogId: "items", items: [potion], total: 1,
  offset: 0, limit: 30, categories: [], revision: 7 };

beforeEach(() => {
  vi.useFakeTimers();
  Object.defineProperty(HTMLDialogElement.prototype, "showModal", { configurable: true,
    value: function (this: HTMLDialogElement) { this.open = true; } });
  vi.spyOn(api, "referenceCandidates").mockResolvedValue(page);
});
afterEach(() => {
  cleanup(); vi.restoreAllMocks(); vi.useRealTimers();
  Reflect.deleteProperty(HTMLDialogElement.prototype, "showModal");
});

function setup() {
  const onCommit = vi.fn(async () => true);
  const onClose = vi.fn();
  const refresh = vi.fn(async () => {});
  const rendered = render(<DocumentContext.Provider value={{ workspaceId: "w", documentId: "d", canEdit: true,
    store: new DocumentStore({ canUndo: false, canRedo: false, isDirty: false, revision: 7 }), structureVersion: 0,
    refresh, commit: vi.fn(), mutate: async (call: () => Promise<import("../../../api/protocol").EditResult>) => call(), acceptResult: vi.fn(), runAction: vi.fn() }}>
    <ReferencePicker id="item-picker" spec={{ kind: "reference", catalogId: "items" }} valueId="item" value="9999"
      onCommit={onCommit} onClose={onClose} />
  </DocumentContext.Provider>);
  return { ...rendered, onCommit, onClose, refresh };
}

async function advanceSearch(milliseconds = 180) {
  await act(async () => { await vi.advanceTimersByTimeAsync(milliseconds); });
}

it("retries candidate reads without losing raw-ID or search drafts", async () => {
  vi.mocked(api.referenceCandidates).mockRejectedValueOnce(new ApiError("Candidates unavailable", 503));
  const { onClose } = setup();
  fireEvent.change(screen.getByRole("combobox", { name: "Search" }), { target: { value: "pot" } });
  fireEvent.change(screen.getByLabelText("Raw ID"), { target: { value: "7777" } });
  await advanceSearch();
  expect(screen.getByRole("alert").textContent).toBe("Candidates unavailable");
  expect(screen.getByRole("status").textContent).toBe("Candidates unavailable");
  fireEvent.click(screen.getByRole("button", { name: "Retry candidates" }));
  await advanceSearch();
  expect(screen.queryByRole("alert")).toBeNull();
  expect(screen.getByRole("option", { name: /Potion/ })).toBeTruthy();
  expect(screen.getByLabelText("Raw ID")).toHaveProperty("value", "7777");
  expect(api.referenceCandidates).toHaveBeenLastCalledWith("w", "d", expect.objectContaining({ q: "pot" }));
  expect(onClose).not.toHaveBeenCalled();
});

it.each(["candidates", "lookup"])("keeps drafts open after a stale %s response and explicitly refreshes choices", async kind => {
  const stale = new ApiError("This view is out of date", 409, { code: "stale" });
  if (kind === "candidates") vi.mocked(api.referenceCandidates).mockRejectedValueOnce(stale);
  else vi.spyOn(api, "referenceCandidate").mockRejectedValueOnce(stale);
  const { onClose, onCommit, refresh } = setup();
  fireEvent.change(screen.getByLabelText("Raw ID"), { target: { value: "7777" } });
  await advanceSearch();
  if (kind === "lookup") await act(async () => { fireEvent.click(screen.getByRole("button", { name: "Preview raw ID" })); });
  expect(onClose).not.toHaveBeenCalled();
  expect(screen.getByLabelText("Raw ID")).toHaveProperty("value", "7777");
  expect(screen.getByRole("button", { name: "Use selected item" })).toHaveProperty("disabled", true);
  await act(async () => { fireEvent.click(screen.getByRole("button", { name: "Refresh choices" })); });
  await advanceSearch();
  expect(refresh).toHaveBeenCalledOnce();
  expect(screen.queryByRole("alert")).toBeNull();
  expect(screen.getByLabelText("Raw ID")).toHaveProperty("value", "7777");
  expect(onCommit).not.toHaveBeenCalled();
});

it("coalesces typing into one request after the full debounce and cancels timers on unmount", async () => {
  const { unmount, onCommit } = setup();
  const search = screen.getByRole("combobox", { name: "Search" });
  await advanceSearch(100);
  fireEvent.change(search, { target: { value: "p" } });
  await advanceSearch(100);
  fireEvent.change(search, { target: { value: "pot" } });
  await advanceSearch(179);
  expect(api.referenceCandidates).not.toHaveBeenCalled();
  expect(screen.getByRole("status").textContent).toBe("Loading candidates…");
  await advanceSearch(1);
  expect(api.referenceCandidates).toHaveBeenCalledExactlyOnceWith("w", "d", {
    valueId: "item", actionId: undefined, revision: 7, q: "pot", category: "", sortBy: undefined,
    descending: false, offset: 0, limit: 30,
  });
  expect(screen.getByRole("option", { name: /Potion/ })).toBeTruthy();
  fireEvent.change(search, { target: { value: "ether" } });
  unmount();
  await advanceSearch();
  expect(api.referenceCandidates).toHaveBeenCalledTimes(1);
  expect(onCommit).not.toHaveBeenCalled();
});

it("shows an empty result without offering a commit or paging path", async () => {
  vi.mocked(api.referenceCandidates).mockResolvedValue({ ...page, items: [], total: 0 });
  const { onCommit } = setup();
  await advanceSearch();
  expect(screen.getByRole("status").textContent).toBe("0 matches");
  const list = screen.getByRole("listbox", { name: "Candidates" });
  expect(list.getAttribute("aria-busy")).toBe("false");
  expect(within(list).queryByRole("option")).toBeNull();
  for (const name of ["Previous", "Next", "Use selected item"]) {
    expect(screen.getByRole("button", { name })).toHaveProperty("disabled", true);
  }
  fireEvent.keyDown(screen.getByRole("combobox", { name: "Search" }), { key: "Enter" });
  expect(onCommit).not.toHaveBeenCalled();
});

it("ignores an obsolete search failure after the current search succeeds", async () => {
  let rejectOld!: (failure: Error) => void;
  vi.mocked(api.referenceCandidates).mockImplementationOnce(() => new Promise((_, reject) => { rejectOld = reject; }));
  setup();
  await advanceSearch();
  fireEvent.change(screen.getByRole("combobox", { name: "Search" }), { target: { value: "pot" } });
  await advanceSearch();
  await act(async () => { rejectOld(new Error("Obsolete failure")); });
  expect(screen.queryByRole("alert")).toBeNull();
  expect(screen.getByRole("option", { name: /Potion/ })).toBeTruthy();
  expect(screen.getByRole("status").textContent).toBe("1 matches · 1–1");
});

it("allows raw lookup to recover after a request failure without committing the failed ID", async () => {
  vi.spyOn(api, "referenceCandidate").mockRejectedValueOnce(new Error("Lookup unavailable")).mockResolvedValue(potion);
  const { onCommit, onClose } = setup();
  await advanceSearch();
  const lookup = screen.getByRole("button", { name: "Preview raw ID" });
  await act(async () => { fireEvent.click(lookup); });
  expect(screen.getByRole("alert").textContent).toBe("Lookup unavailable");
  expect(lookup).toHaveProperty("disabled", false);
  expect(onCommit).not.toHaveBeenCalled();
  expect(onClose).not.toHaveBeenCalled();
  fireEvent.change(screen.getByLabelText("Raw ID"), { target: { value: "1001" } });
  await act(async () => { fireEvent.click(lookup); });
  expect(api.referenceCandidate).toHaveBeenLastCalledWith("w", "d", { valueId: "item", actionId: undefined, revision: 7, rawId: "1001" });
  expect(screen.queryByRole("alert")).toBeNull();
  expect(screen.getByRole("heading", { name: "Potion" })).toBeTruthy();
  expect(onCommit).not.toHaveBeenCalled();
});

it("keeps a failed commit open, re-enables controls and permits an explicit retry", async () => {
  const { onCommit, onClose } = setup();
  onCommit.mockRejectedValueOnce(new Error("Write unavailable"));
  await advanceSearch();
  fireEvent.click(screen.getByRole("option", { name: /Potion/ }));
  const apply = screen.getByRole("button", { name: "Use selected item" });
  await act(async () => { fireEvent.click(apply); });
  expect(screen.getByRole("alert").textContent).toBe("Write unavailable");
  expect(onClose).not.toHaveBeenCalled();
  expect(apply).toHaveProperty("disabled", false);
  await act(async () => { fireEvent.click(apply); });
  expect(screen.queryByRole("alert")).toBeNull();
  expect(onCommit.mock.calls).toEqual([["1001"], ["1001"]]);
  expect(onClose).toHaveBeenCalledExactlyOnceWith();
});
