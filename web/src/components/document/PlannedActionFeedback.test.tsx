import { defaultScheduler, notifyManager, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { actionReview, type ActionPreview } from "../../api/actionReview";
import { api, ApiError } from "../../api/client";
import type { EditResult, SaveDocument } from "../../api/protocol";
import { installDialogStub } from "../../test/dialog";
import { deferred } from "../../test/deferred";
import { DocumentEditor } from "./DocumentEditor";

const clients: QueryClient[] = [];
let restoreDialog: () => void;
beforeEach(() => {
  notifyManager.setScheduler(queueMicrotask);
  restoreDialog = installDialogStub({ dispatchCloseEvent: true });
});
afterEach(() => {
  cleanup(); clients.splice(0).forEach(client => client.clear()); vi.restoreAllMocks();
  notifyManager.setScheduler(defaultScheduler); restoreDialog();
});

const state = { revision: 1, isDirty: false, canUndo: false, canRedo: false };
const stale: EditResult = { ok: false, stale: true, changes: [], warnings: [], structureChanged: false, state,
  errors: [{ valueId: "", message: "Metadata changed" }] };
const preview = (revision: number): ActionPreview => ({ planId: "plan", revision, scope: {}, plan: {
  errors: [], warnings: [], skippedRows: [], changes: [
    { valueId: "caption", label: "Caption", oldValue: "old", newValue: "🎮", oldExists: true, newExists: true, derived: false },
  ],
} });

const snapshot = (revision = 1, flag = false): SaveDocument => ({ id: "d", fileId: "f", title: "Save", filePath: "save.dat", canEdit: true, state: { ...state, revision },
    plugin: { id: "test", name: "Test", version: "1", maturity: "Experimental", games: [], references: [] },
    view: { type: "section", title: "Fields", children: [
      { type: "field", valueId: "flag", label: "Flag", spec: { kind: "bool" }, value: flag },
      { type: "action", actionId: "direct", label: "Direct action" },
      { type: "action", actionId: "rename", label: "Rename", supportsPlanning: true, parameters: [
        { type: "field", valueId: "caption", label: "Caption", value: "old", spec: { kind: "text", maxUtf8Bytes: 4 } },
      ] },
    ] } });

async function mount() {
  vi.spyOn(api, "document").mockResolvedValue(snapshot());
  vi.spyOn(api, "sources").mockResolvedValue({ datasets: [] });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } });
  clients.push(client);
  const toast = vi.fn();
  await act(async () => { render(<QueryClientProvider client={client}><DocumentEditor workspaceId="w" documentId="d"
    file={undefined} onSwitchPlugin={vi.fn()} onToast={toast} /></QueryClientProvider>); });
  return toast;
}

it.each(["envelope", "exception"])("refreshes revision and fields before the first stale retry with one diagnostic (%s)", async kind => {
  let serverRevision = 1;
  const plan = vi.spyOn(actionReview, "plan").mockImplementation(async (_w, _d, _a, _params, _scope, revision) => {
    expect(revision).toBe(serverRevision);
    return preview(serverRevision);
  });
  const apply = vi.spyOn(actionReview, "apply").mockImplementation(async () => {
    serverRevision = 2;
    if (kind === "exception") throw new ApiError("Metadata changed", 409, { code: "stale" });
    return { ...stale, state: { ...state, revision: serverRevision } };
  });
  const toast = await mount();
  const refreshed = deferred<SaveDocument>();
  vi.mocked(api.document).mockReturnValueOnce(refreshed.promise);
  const trigger = screen.getByRole("button", { name: "Rename" });
  fireEvent.click(trigger);
  fireEvent.change(screen.getByRole("textbox", { name: "Caption" }), { target: { value: "🎮" } });
  fireEvent.blur(screen.getByRole("textbox", { name: "Caption" }));
  fireEvent.click(screen.getByRole("button", { name: "Preview" }));
  fireEvent.click(await screen.findByRole("button", { name: "Apply" }));
  await waitFor(() => expect(api.document).toHaveBeenCalledTimes(2));
  expect(screen.queryByRole("button", { name: "Preview again" })).toBeNull();
  await act(async () => { refreshed.resolve(snapshot(serverRevision, true)); await refreshed.promise; });
  const reason = await screen.findByRole("alert");
  await waitFor(() => expect(document.activeElement).toBe(reason));
  expect(reason.textContent).toContain("Metadata changed");
  expect(reason.textContent).toContain("This preview is stale");
  expect(screen.getAllByRole("alert")).toHaveLength(1);
  expect(toast).not.toHaveBeenCalled();
  expect(screen.getByRole("switch", { name: "Flag" }).getAttribute("aria-checked")).toBe("true");
  expect(screen.getByRole("textbox", { name: "Caption" })).toHaveProperty("value", "🎮");
  expect(screen.queryByRole("button", { name: "Apply" })).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "Preview again" }));
  await screen.findByRole("button", { name: "Apply" });
  expect(plan).toHaveBeenLastCalledWith("w", "d", "rename", { caption: "🎮" }, {}, 2);
  expect(plan).toHaveBeenCalledTimes(2);
  expect(apply).toHaveBeenCalledOnce();
  fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
  expect(document.activeElement).toBe(trigger);
});

it.each([undefined, "conflict"])("keeps an ordinary 409 non-stale and dialog-owned (code=%s)", async code => {
  vi.spyOn(actionReview, "plan").mockResolvedValue(preview(1));
  vi.spyOn(actionReview, "apply").mockRejectedValue(new ApiError("Action conflicts with current settings", 409, code === undefined ? undefined : { code }));
  const toast = await mount();
  fireEvent.click(screen.getByRole("button", { name: "Rename" }));
  fireEvent.change(screen.getByRole("textbox", { name: "Caption" }), { target: { value: "🎮" } });
  fireEvent.blur(screen.getByRole("textbox", { name: "Caption" }));
  fireEvent.click(screen.getByRole("button", { name: "Preview" }));
  fireEvent.click(await screen.findByRole("button", { name: "Apply" }));
  const reason = await screen.findByRole("alert");
  await waitFor(() => expect(document.activeElement).toBe(reason));
  expect(reason.textContent).toBe("Action conflicts with current settings");
  expect(screen.getAllByRole("alert")).toHaveLength(1);
  expect(screen.queryByRole("button", { name: "Preview again" })).toBeNull();
  expect(screen.getByRole("button", { name: "Preview" })).toHaveProperty("disabled", false);
  expect(screen.getByRole("textbox", { name: "Caption" })).toHaveProperty("value", "🎮");
  expect(api.document).toHaveBeenCalledOnce();
  expect(toast).not.toHaveBeenCalled();
});

it.each(["envelope", "exception", "stale-exception"])("preserves ordinary field failure toasts (%s)", async kind => {
  const edit = vi.spyOn(api, "edit");
  if (kind === "envelope") edit.mockResolvedValue(stale);
  else edit.mockRejectedValue(new ApiError("Metadata changed", 409, kind === "stale-exception" ? { code: "stale" } : undefined));
  const toast = await mount();
  await act(async () => { fireEvent.click(screen.getByRole("switch", { name: "Flag" })); });
  expect(toast).toHaveBeenCalledOnce();
  expect(toast).toHaveBeenLastCalledWith(kind === "envelope"
    ? "The document changed. Please try again with the current view." : "Metadata changed", true);
  expect(api.document).toHaveBeenCalledTimes(kind === "exception" ? 1 : 2);
});

it("preserves non-dialog action failure toasts", async () => {
  vi.spyOn(api, "action").mockRejectedValue(new ApiError("Metadata changed", 409));
  const toast = await mount();
  await act(async () => { fireEvent.click(screen.getByRole("button", { name: "Direct action" })); });
  expect(toast).toHaveBeenCalledExactlyOnceWith("Metadata changed", true);
});

async function failedStaleRead(kind: string) {
  let serverRevision = 1;
  const plan = vi.spyOn(actionReview, "plan").mockImplementation(async (_w, _d, _a, _params, _scope, revision) => {
    expect(revision).toBe(serverRevision);
    return preview(serverRevision);
  });
  const apply = vi.spyOn(actionReview, "apply").mockImplementation(async () => {
    serverRevision = 2;
    if (kind === "exception") throw new ApiError("Metadata changed", 409, { code: "stale" });
    return { ...stale, state: { ...state, revision: serverRevision } };
  });
  const toast = await mount();
  vi.mocked(api.document).mockRejectedValue(new ApiError("Snapshot service unavailable", 503));
  const trigger = screen.getByRole("button", { name: "Rename" });
  fireEvent.click(trigger);
  const dialog = within(screen.getByRole("dialog"));
  fireEvent.change(dialog.getByRole("textbox", { name: "Caption" }), { target: { value: "🎮" } });
  fireEvent.blur(dialog.getByRole("textbox", { name: "Caption" }));
  fireEvent.click(dialog.getByRole("button", { name: "Preview" }));
  fireEvent.click(await dialog.findByRole("button", { name: "Apply" }));
  await dialog.findByRole("button", { name: "Retry document refresh" });
  return { toast, plan, apply, trigger, dialog };
}

it.each(["envelope", "exception"])("requires a successful read after repeated stale refresh failures (%s)", async kind => {
  const { toast, plan, apply, dialog } = await failedStaleRead(kind);
  await waitFor(() => expect(document.activeElement).toBe(dialog.getByRole("alert")));
  expect(dialog.getByRole("alert").textContent).toContain("Snapshot service unavailable");
  expect(dialog.queryByRole("button", { name: "Preview again" })).toBeNull();
  expect(dialog.getAllByRole("alert")).toHaveLength(1);
  expect(screen.getAllByRole("alert")).toHaveLength(1);
  expect(dialog.getByRole("textbox", { name: "Caption" })).toHaveProperty("value", "🎮");
  fireEvent.click(dialog.getByRole("button", { name: "Retry document refresh" }));
  await waitFor(() => expect(api.document).toHaveBeenCalledTimes(3));
  await waitFor(() => expect(document.activeElement).toBe(dialog.getByRole("alert")));
  expect(plan).toHaveBeenCalledTimes(1);
  const refreshed = deferred<SaveDocument>();
  vi.mocked(api.document).mockReturnValueOnce(refreshed.promise);
  fireEvent.click(dialog.getByRole("button", { name: "Retry document refresh" }));
  await waitFor(() => expect(api.document).toHaveBeenCalledTimes(4));
  expect(plan).toHaveBeenCalledTimes(1);
  expect(dialog.queryByRole("button", { name: "Preview again" })).toBeNull();
  await act(async () => { refreshed.resolve(snapshot(2, true)); await refreshed.promise; });
  const retryPlan = await dialog.findByRole("button", { name: "Preview again" });
  expect(screen.queryByText(/Could not refresh the document/)).toBeNull();
  expect(screen.getByRole("switch", { name: "Flag" }).getAttribute("aria-checked")).toBe("true");
  expect(dialog.getByRole("textbox", { name: "Caption" })).toHaveProperty("value", "🎮");
  fireEvent.click(retryPlan);
  await dialog.findByRole("button", { name: "Apply" });
  expect(plan.mock.calls.map(call => call[5])).toEqual([1, 2]);
  expect(apply).toHaveBeenCalledOnce();
  expect(toast).not.toHaveBeenCalled();
  expect(screen.getByRole("button", { name: "Undo" })).toHaveProperty("disabled", true);
});

it("keeps background read recovery reachable after cancelling the failed dialog", async () => {
  const { toast, plan, apply, trigger, dialog } = await failedStaleRead("envelope");
  fireEvent.click(dialog.getByRole("button", { name: "Cancel" }));
  await waitFor(() => expect(document.activeElement).toBe(trigger));
  expect(screen.getByRole("alert").textContent).toContain("Snapshot service unavailable");
  expect(screen.getByRole("button", { name: "Refresh document" })).toHaveProperty("disabled", false);
  // Reopening before any successful read must not expose cached revision 1 as a fresh preview.
  fireEvent.click(trigger);
  expect(dialog.getByRole("button", { name: "Retry document refresh" })).toHaveProperty("disabled", false);
  expect(screen.getAllByRole("alert")).toHaveLength(1);
  expect(plan).toHaveBeenCalledOnce();
  fireEvent.click(dialog.getByRole("button", { name: "Cancel" }));
  vi.mocked(api.document).mockResolvedValue(snapshot(2, true));
  await act(async () => { fireEvent.click(screen.getByRole("button", { name: "Refresh document" })); });
  expect(screen.queryByText(/Could not refresh the document/)).toBeNull();
  expect(screen.getByRole("switch", { name: "Flag" }).getAttribute("aria-checked")).toBe("true");
  expect(apply).toHaveBeenCalledOnce();
  expect(toast).not.toHaveBeenCalled();
});

it.each(["close", "unmount"])("retires a pending dialog read retry after %s", async retirement => {
  const { toast, plan, apply, dialog, trigger } = await failedStaleRead("exception");
  const refreshed = deferred<SaveDocument>();
  vi.mocked(api.document).mockReturnValueOnce(refreshed.promise);
  fireEvent.click(dialog.getByRole("button", { name: "Retry document refresh" }));
  await waitFor(() => expect(api.document).toHaveBeenCalledTimes(3));
  if (retirement === "unmount") cleanup();
  else {
    act(() => { screen.getByRole<HTMLDialogElement>("dialog").close(); });
    await waitFor(() => expect(document.activeElement).toBe(trigger));
  }
  await act(async () => { refreshed.resolve(snapshot(2, true)); await refreshed.promise; });
  expect(screen.queryByRole("dialog")).toBeNull();
  expect(plan).toHaveBeenCalledOnce();
  expect(apply).toHaveBeenCalledOnce();
  expect(toast).not.toHaveBeenCalled();
  if (retirement === "close") expect(document.activeElement).toBe(trigger);
});

it("does not mistake cancelled cached success for a newly installed snapshot", async () => {
  const plan = vi.spyOn(actionReview, "plan").mockImplementation(async (_w, _d, _a, _params, _scope, revision) => preview(revision!));
  vi.spyOn(actionReview, "apply").mockResolvedValue(stale);
  const toast = await mount();
  const cancelledRead = deferred<SaveDocument>();
  vi.mocked(api.document).mockReturnValueOnce(cancelledRead.promise);
  fireEvent.click(screen.getByRole("button", { name: "Rename" }));
  const dialog = within(screen.getByRole("dialog"));
  fireEvent.change(dialog.getByRole("textbox", { name: "Caption" }), { target: { value: "🎮" } });
  fireEvent.blur(dialog.getByRole("textbox", { name: "Caption" }));
  fireEvent.click(dialog.getByRole("button", { name: "Preview" }));
  fireEvent.click(await dialog.findByRole("button", { name: "Apply" }));
  await waitFor(() => expect(api.document).toHaveBeenCalledTimes(2));
  await act(async () => { await clients.at(-1)!.cancelQueries({ queryKey: ["document", "w", "d"] }); });
  await dialog.findByRole("button", { name: "Retry document refresh" });
  expect(dialog.queryByRole("button", { name: "Preview again" })).toBeNull();
  expect(plan).toHaveBeenCalledOnce();
  fireEvent.click(dialog.getByRole("button", { name: "Cancel" }));
  fireEvent.click(screen.getByRole("button", { name: "Rename" }));
  expect(dialog.getByRole("button", { name: "Retry document refresh" })).toHaveProperty("disabled", false);
  expect(plan).toHaveBeenCalledOnce();
  vi.mocked(api.document).mockResolvedValue(snapshot(2, true));
  fireEvent.click(dialog.getByRole("button", { name: "Retry document refresh" }));
  const retry = await dialog.findByRole("button", { name: "Preview again" });
  await act(async () => { cancelledRead.resolve(snapshot()); await cancelledRead.promise; });
  fireEvent.click(retry);
  await dialog.findByRole("button", { name: "Apply" });
  expect(plan.mock.calls.map(call => call[5])).toEqual([1, 2]);
  expect(toast).not.toHaveBeenCalled();
});

async function pendingStaleRead(kind: string) {
  let serverRevision = 1;
  const plan = vi.spyOn(actionReview, "plan").mockImplementation(async (_w, _d, _a, _params, _scope, revision) => {
    expect(revision).toBe(serverRevision);
    return preview(serverRevision);
  });
  const apply = vi.spyOn(actionReview, "apply").mockImplementation(async () => {
    serverRevision = 2;
    if (kind === "exception") throw new ApiError("Metadata changed", 409, { code: "stale" });
    return { ...stale, state: { ...state, revision: serverRevision } };
  });
  const toast = await mount();
  const firstRead = deferred<SaveDocument>();
  vi.mocked(api.document).mockReturnValueOnce(firstRead.promise);
  const trigger = screen.getByRole("button", { name: "Rename" });
  fireEvent.click(trigger);
  const element = screen.getByRole<HTMLDialogElement>("dialog");
  const dialog = within(element);
  fireEvent.change(dialog.getByRole("textbox", { name: "Caption" }), { target: { value: "🎮" } });
  fireEvent.blur(dialog.getByRole("textbox", { name: "Caption" }));
  fireEvent.click(dialog.getByRole("button", { name: "Preview" }));
  fireEvent.click(await dialog.findByRole("button", { name: "Apply" }));
  await waitFor(() => expect(api.document).toHaveBeenCalledTimes(2));
  return { plan, apply, toast, firstRead, trigger, element, dialog };
}

it.each(["envelope", "exception"].flatMap(kind => ["success", "failure", "cancelled"].map(outcome => ({ kind, outcome }))))(
  "keeps the initial stale read required across close/reopen ($kind, $outcome)", async ({ kind, outcome }) => {
    const { plan, apply, toast, firstRead, trigger, element, dialog } = await pendingStaleRead(kind);
    act(() => element.close()); // Native Escape closes even during the mutation's direct read.
    await waitFor(() => expect(document.activeElement).toBe(trigger));
    fireEvent.click(trigger);
    expect(dialog.getByRole("button", { name: "Refreshing document…" })).toHaveProperty("disabled", true);
    expect(dialog.getByRole("textbox", { name: "Caption" })).toHaveProperty("value", "🎮");
    expect(document.activeElement).toBe(dialog.getByRole("heading"));
    fireEvent.submit(element.querySelector("form")!); // The handler must also block keyboard/script submission.
    expect(plan).toHaveBeenCalledOnce();
    expect(dialog.queryByRole("button", { name: "Apply" })).toBeNull();
    if (outcome === "cancelled") {
      await act(async () => { await clients.at(-1)!.cancelQueries({ queryKey: ["document", "w", "d"] }); });
    } else {
      await act(async () => {
        if (outcome === "failure") firstRead.reject(new ApiError("Snapshot service unavailable", 503));
        else firstRead.resolve(snapshot(2, true));
        await firstRead.promise.catch(() => {});
      });
    }
    if (outcome !== "success") {
      const retry = await dialog.findByRole("button", { name: "Retry document refresh" });
      await waitFor(() => expect(document.activeElement).toBe(dialog.getByRole("alert")));
      expect(screen.getAllByRole("alert")).toHaveLength(1);
      expect(plan).toHaveBeenCalledOnce();
      vi.mocked(api.document).mockResolvedValue(snapshot(2, true));
      fireEvent.click(retry);
    }
    const previewAgain = await dialog.findByRole("button", { name: "Preview again" });
    expect(previewAgain).toHaveProperty("disabled", false);
    expect(dialog.getByRole("textbox", { name: "Caption" })).toHaveProperty("value", "🎮");
    expect(screen.getByRole("switch", { name: "Flag" }).getAttribute("aria-checked")).toBe("true");
    expect(plan).toHaveBeenCalledOnce(); // Snapshot recovery never plans automatically.
    fireEvent.click(previewAgain);
    await dialog.findByRole("button", { name: "Apply" });
    expect(plan.mock.calls.map(call => call[5])).toEqual([1, 2]);
    expect(plan).toHaveBeenLastCalledWith("w", "d", "rename", { caption: "🎮" }, {}, 2);
    expect(apply).toHaveBeenCalledOnce();
    expect(toast).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "Undo" })).toHaveProperty("disabled", true);
    if (outcome === "cancelled") await act(async () => { firstRead.resolve(snapshot()); await firstRead.promise; });
    expect(plan).toHaveBeenCalledTimes(2);
    fireEvent.click(dialog.getByRole("button", { name: "Cancel" }));
    expect(document.activeElement).toBe(trigger);
  },
);
