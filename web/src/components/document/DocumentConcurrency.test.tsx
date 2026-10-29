import { defaultScheduler, notifyManager, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { api, ApiError } from "../../api/client";
import { actionReview } from "../../api/actionReview";
import type { EditResult, SaveDocument, Workspace } from "../../api/protocol";
import { deferred } from "../../test/deferred";
import { DocumentEditor } from "./DocumentEditor";
import { WorkspaceActivity, WorkspaceActivityContext } from "../../state/workspaceActivity";

const clients: QueryClient[] = [];
beforeEach(() => {
  notifyManager.setScheduler(queueMicrotask);
  Object.defineProperty(HTMLDialogElement.prototype, "showModal", { configurable: true, value: function (this: HTMLDialogElement) { this.open = true; } });
  Object.defineProperty(HTMLDialogElement.prototype, "close", { configurable: true, value: function (this: HTMLDialogElement) { this.open = false; } });
});
afterEach(() => { cleanup(); clients.splice(0).forEach(client => client.clear()); vi.restoreAllMocks(); notifyManager.setScheduler(defaultScheduler); Reflect.deleteProperty(HTMLDialogElement.prototype, "showModal"); Reflect.deleteProperty(HTMLDialogElement.prototype, "close"); });

const state = { revision: 1, isDirty: true, canUndo: true, canRedo: true };
const result = (revision: number, extra: Partial<EditResult> = {}): EditResult => ({ ok: true, changes: [], errors: [], warnings: [],
  structureChanged: false, state: { ...state, revision }, ...extra });
const snapshot = (): SaveDocument => ({ id: "d", fileId: "f", title: "Save", filePath: "save.dat", canEdit: true,
  plugin: { id: "test", name: "Test", version: "1", maturity: "Experimental", games: [], references: [] }, state,
  view: { type: "section", title: "Fields", children: ["a", "b"].map(id => ({ type: "field", valueId: id, label: id, spec: { kind: "bool" }, value: false })) } });

async function mount(document = snapshot(), activity: WorkspaceActivity | null = null) {
  vi.spyOn(api, "document").mockResolvedValue(document);
  vi.spyOn(api, "sources").mockResolvedValue({ datasets: [] });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } });
  clients.push(client);
  const toast = vi.fn();
  let view!: ReturnType<typeof render>;
  await act(async () => { view = render(<QueryClientProvider client={client}><WorkspaceActivityContext.Provider value={activity}><DocumentEditor workspaceId="w" documentId="d"
    file={undefined} onSwitchPlugin={vi.fn()} onToast={toast} /></WorkspaceActivityContext.Provider></QueryClientProvider>); });
  return { ...view, client, toast };
}

it("serializes different field edits and reads the acknowledged revision for the next write", async () => {
  const first = deferred<EditResult>();
  const second = deferred<EditResult>();
  const edit = vi.spyOn(api, "edit").mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise);
  await mount();
  await act(async () => {
    fireEvent.click(screen.getByRole("switch", { name: "a" }));
    fireEvent.click(screen.getByRole("switch", { name: "b" }));
  });
  expect(edit).toHaveBeenCalledTimes(1);
  await act(async () => { first.resolve(result(2)); await first.promise; });
  expect(edit).toHaveBeenCalledTimes(2);
  expect(edit.mock.calls.map(call => call[3])).toEqual([1, 2]);
  await act(async () => { second.resolve(result(3)); await second.promise; });
});

it("queues undo after an edit and ignores repeated undo while it is pending", async () => {
  const pending = deferred<EditResult>();
  const history = deferred<EditResult>();
  const edit = vi.spyOn(api, "edit").mockReturnValue(pending.promise);
  const undo = vi.spyOn(api, "undo").mockReturnValue(history.promise);
  await mount();
  await act(async () => {
    fireEvent.click(screen.getByRole("switch", { name: "a" }));
    fireEvent.click(screen.getByRole("button", { name: "Undo" }));
    fireEvent.keyDown(window, { key: "z", ctrlKey: true });
  });
  expect(edit).toHaveBeenCalledOnce();
  expect(undo).not.toHaveBeenCalled();
  await act(async () => { pending.resolve(result(2)); await pending.promise; });
  expect(undo).toHaveBeenCalledOnce();
  await act(async () => { history.resolve(result(3)); await history.promise; });
});

it("does not send queued old bindings after a structural edit refreshes the view", async () => {
  const pending = deferred<EditResult>();
  const edit = vi.spyOn(api, "edit").mockReturnValue(pending.promise);
  await mount();
  await act(async () => {
    fireEvent.click(screen.getByRole("switch", { name: "a" }));
    fireEvent.click(screen.getByRole("switch", { name: "b" }));
  });
  await act(async () => { pending.resolve(result(2, { structureChanged: true })); await pending.promise; });
  expect(api.document).toHaveBeenCalledTimes(2);
  expect(edit).toHaveBeenCalledOnce();
});

it("retires queued writes and completion toasts when the document unmounts", async () => {
  const pending = deferred<EditResult>();
  const edit = vi.spyOn(api, "edit").mockReturnValue(pending.promise);
  const { unmount, toast } = await mount();
  await act(async () => {
    fireEvent.click(screen.getByRole("switch", { name: "a" }));
    fireEvent.click(screen.getByRole("switch", { name: "b" }));
  });
  unmount();
  await act(async () => { pending.resolve(result(2, { message: "Old action completed" })); await pending.promise; });
  expect(edit).toHaveBeenCalledOnce();
  expect(toast).not.toHaveBeenCalled();
});

it.each([409, 412, 503])("does not replay a failed HTTP %s mutation or release queued intents", async status => {
  const pending = deferred<EditResult>();
  const edit = vi.spyOn(api, "edit").mockReturnValueOnce(pending.promise).mockResolvedValue(result(2));
  await mount();
  await act(async () => {
    fireEvent.click(screen.getByRole("switch", { name: "a" }));
    fireEvent.click(screen.getByRole("switch", { name: "b" }));
  });
  await act(async () => { pending.reject(new ApiError("Write rejected", status)); await pending.promise.catch(() => {}); });
  expect(edit).toHaveBeenCalledOnce();
  expect(api.document).toHaveBeenCalledOnce();
  await act(async () => { fireEvent.click(screen.getByRole("switch", { name: "b" })); });
  expect(edit).toHaveBeenCalledTimes(2);
});

it("cancels a snapshot begun during a write before it can overwrite the acknowledgement", async () => {
  const pending = deferred<EditResult>();
  const read = deferred<SaveDocument>();
  vi.spyOn(api, "edit").mockReturnValue(pending.promise);
  const { client } = await mount();
  await act(async () => { fireEvent.click(screen.getByRole("switch", { name: "a" })); });
  vi.mocked(api.document).mockReturnValueOnce(read.promise);
  let refresh!: Promise<void>;
  await act(async () => { refresh = client.refetchQueries({ queryKey: ["document", "w", "d"] }); });
  await act(async () => { pending.resolve(result(2, { changes: [{ valueId: "a", oldValue: false, newValue: true }] })); await pending.promise; });
  await act(async () => { read.resolve(snapshot()); await read.promise; await refresh; });
  expect(screen.getByRole("switch", { name: /^a/ }).getAttribute("aria-checked")).toBe("true");
});

it.each([false, true])("guards repeated legacy action submits (dialog=%s)", async dialog => {
  const pending = deferred<EditResult>();
  const action = vi.spyOn(api, "action").mockReturnValue(pending.promise);
  const doc = snapshot();
  doc.view = { type: "action", actionId: "increment", label: "Increment", confirm: dialog ? "Confirm increment" : undefined };
  await mount(doc);
  if (dialog) fireEvent.click(screen.getByRole("button", { name: "Increment" }));
  await act(async () => {
    if (dialog) {
      const form = screen.getByRole("dialog").querySelector("form")!;
      fireEvent.submit(form); fireEvent.submit(form);
    } else {
      const button = screen.getByRole("button", { name: "Increment" });
      fireEvent.click(button); fireEvent.click(button);
    }
  });
  expect(action).toHaveBeenCalledOnce();
  await act(async () => { pending.resolve(result(2)); await pending.promise; });
});

it("serializes planned apply with edits and guards repeated form submits", async () => {
  vi.spyOn(actionReview, "plan").mockResolvedValue({ planId: "plan", revision: 1, scope: {},
    plan: { changes: [], warnings: [], errors: [], skippedRows: [] } });
  const pending = deferred<EditResult>();
  const apply = vi.spyOn(actionReview, "apply").mockReturnValue(pending.promise);
  const edit = vi.spyOn(api, "edit").mockResolvedValue(result(3));
  const doc = snapshot();
  if (doc.view.type !== "section") throw new Error("Expected fixture section");
  doc.view.children.push({ type: "action", actionId: "increment", label: "Increment", supportsPlanning: true });
  await mount(doc);
  await act(async () => { fireEvent.click(screen.getByRole("button", { name: "Increment" })); });
  await act(async () => {
    const form = screen.getByRole("dialog").querySelector("form")!;
    fireEvent.submit(form); fireEvent.submit(form);
    fireEvent.click(screen.getByRole("switch", { name: "a", hidden: true }));
  });
  expect(apply).toHaveBeenCalledOnce();
  expect(edit).not.toHaveBeenCalled();
  await act(async () => { pending.resolve(result(2)); await pending.promise; });
  expect(edit).toHaveBeenCalledOnce();
  expect(edit.mock.calls[0]![3]).toBe(2);
});

it("serializes a field revert with edits and coalesces duplicate revert clicks", async () => {
  vi.spyOn(actionReview, "changes").mockResolvedValue({ revision: 1, complete: true, warnings: [], changes: [{
    valueId: "a", label: "Flag", path: [], originalValue: false, currentValue: true,
    originalExists: true, currentExists: true, canRevert: true,
  }] });
  const pending = deferred<EditResult>();
  const revert = vi.spyOn(actionReview, "revert").mockReturnValue(pending.promise);
  const edit = vi.spyOn(api, "edit").mockResolvedValue(result(3));
  await mount();
  await act(async () => {
    const details = screen.getByText("Changes against original").closest("details")!;
    details.open = true; fireEvent(details, new Event("toggle"));
  });
  await act(async () => {
    const button = screen.getByRole("button", { name: "Revert Flag" });
    fireEvent.click(button); fireEvent.click(button);
    fireEvent.click(screen.getByRole("switch", { name: "b" }));
  });
  expect(revert).toHaveBeenCalledOnce(); expect(edit).not.toHaveBeenCalled();
  await act(async () => { pending.resolve(result(2)); await pending.promise; });
  expect(edit).toHaveBeenCalledOnce(); expect(edit.mock.calls[0]![3]).toBe(2);
});

it("registers queued document writes with the workspace export barrier", async () => {
  const activity = new WorkspaceActivity();
  const first = deferred<EditResult>(); const second = deferred<EditResult>();
  vi.spyOn(api, "edit").mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise);
  await mount(snapshot(), activity);
  await act(async () => {
    fireEvent.click(screen.getByRole("switch", { name: "a" }));
    fireEvent.click(screen.getByRole("switch", { name: "b" }));
  });
  let ready = false;
  const waiting = activity.waitForMutations().then(() => { ready = true; });
  await act(async () => { first.resolve(result(2)); await first.promise; });
  expect(ready).toBe(false);
  await act(async () => { second.resolve(result(3)); await waiting; });
  expect(ready).toBe(true);
});

it("does not route history shortcuts through an open modal", async () => {
  const undo = vi.spyOn(api, "undo").mockResolvedValue(result(2));
  await mount();
  const modal = document.createElement("dialog"); modal.open = true; document.body.append(modal);
  try {
    await act(async () => { fireEvent.keyDown(window, { key: "z", ctrlKey: true }); });
    expect(undo).not.toHaveBeenCalled();
  } finally { modal.remove(); }
});

it("publishes acknowledged dirty state before the workspace refetch completes", async () => {
  const pending = deferred<EditResult>(); vi.spyOn(api, "edit").mockReturnValue(pending.promise);
  const { client } = await mount();
  client.setQueryData<Workspace>(["workspace", "w"], { id: "w", name: "Dump", createdAt: "2026-10-29", titles: [],
    documents: [{ id: "d", fileId: "f", filePath: "save.dat", title: "Save", pluginId: "test", isDirty: false }] });
  await act(async () => { fireEvent.click(screen.getByRole("switch", { name: "a" })); });
  await act(async () => { pending.resolve(result(2)); await pending.promise; });
  expect(client.getQueryData<Workspace>(["workspace", "w"])?.documents[0]?.isDirty).toBe(true);
});
