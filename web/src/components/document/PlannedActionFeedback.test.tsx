import { defaultScheduler, notifyManager, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { actionReview } from "../../api/actionReview";
import { api, ApiError } from "../../api/client";
import type { EditResult, SaveDocument } from "../../api/protocol";
import { installDialogStub } from "../../test/dialog";
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

async function mount() {
  const document: SaveDocument = { id: "d", fileId: "f", title: "Save", filePath: "save.dat", canEdit: true, state,
    plugin: { id: "test", name: "Test", version: "1", maturity: "Experimental", games: [], references: [] },
    view: { type: "section", title: "Fields", children: [
      { type: "field", valueId: "flag", label: "Flag", spec: { kind: "bool" }, value: false },
      { type: "action", actionId: "direct", label: "Direct action" },
      { type: "action", actionId: "rename", label: "Rename", supportsPlanning: true, parameters: [
        { type: "field", valueId: "caption", label: "Caption", value: "old", spec: { kind: "text", maxUtf8Bytes: 4 } },
      ] },
    ] } };
  vi.spyOn(api, "document").mockResolvedValue(document);
  vi.spyOn(api, "sources").mockResolvedValue({ datasets: [] });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } });
  clients.push(client);
  const toast = vi.fn();
  await act(async () => { render(<QueryClientProvider client={client}><DocumentEditor workspaceId="w" documentId="d"
    file={undefined} onSwitchPlugin={vi.fn()} onToast={toast} /></QueryClientProvider>); });
  return toast;
}

it.each(["envelope", "exception"])("gives a stale planned apply one diagnostic owner through the session (%s)", async kind => {
  const plan = vi.spyOn(actionReview, "plan").mockResolvedValue({ planId: "plan", revision: 1, scope: {}, plan: {
    errors: [], warnings: [], skippedRows: [], changes: [
      { valueId: "caption", label: "Caption", oldValue: "old", newValue: "🎮", oldExists: true, newExists: true, derived: false },
    ],
  } });
  const apply = vi.spyOn(actionReview, "apply");
  if (kind === "envelope") apply.mockResolvedValue(stale);
  else apply.mockRejectedValue(new ApiError("Metadata changed", 409, { code: "stale" }));
  const toast = await mount();
  const trigger = screen.getByRole("button", { name: "Rename" });
  fireEvent.click(trigger);
  fireEvent.change(screen.getByRole("textbox", { name: "Caption" }), { target: { value: "🎮" } });
  fireEvent.blur(screen.getByRole("textbox", { name: "Caption" }));
  fireEvent.click(screen.getByRole("button", { name: "Preview" }));
  fireEvent.click(await screen.findByRole("button", { name: "Apply" }));
  const reason = await screen.findByRole("alert");
  await waitFor(() => expect(document.activeElement).toBe(reason));
  expect(reason.textContent).toContain("Metadata changed");
  expect(reason.textContent).toContain("This preview is stale");
  expect(screen.getAllByRole("alert")).toHaveLength(1);
  expect(toast).not.toHaveBeenCalled();
  expect(screen.getByRole("textbox", { name: "Caption" })).toHaveProperty("value", "🎮");
  expect(screen.queryByRole("button", { name: "Apply" })).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "Preview again" }));
  await screen.findByRole("button", { name: "Apply" });
  expect(plan).toHaveBeenLastCalledWith("w", "d", "rename", { caption: "🎮" }, {}, 1);
  expect(apply).toHaveBeenCalledOnce();
  fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
  expect(document.activeElement).toBe(trigger);
});

it.each(["envelope", "exception"])("preserves ordinary field failure toasts (%s)", async kind => {
  const edit = vi.spyOn(api, "edit");
  if (kind === "envelope") edit.mockResolvedValue(stale);
  else edit.mockRejectedValue(new ApiError("Metadata changed", 409));
  const toast = await mount();
  await act(async () => { fireEvent.click(screen.getByRole("switch", { name: "Flag" })); });
  expect(toast).toHaveBeenCalledOnce();
  expect(toast).toHaveBeenLastCalledWith(kind === "envelope"
    ? "The document changed. Please try again with the current view." : "Metadata changed", true);
});

it("preserves non-dialog action failure toasts", async () => {
  vi.spyOn(api, "action").mockRejectedValue(new ApiError("Metadata changed", 409));
  const toast = await mount();
  await act(async () => { fireEvent.click(screen.getByRole("button", { name: "Direct action" })); });
  expect(toast).toHaveBeenCalledExactlyOnceWith("Metadata changed", true);
});
