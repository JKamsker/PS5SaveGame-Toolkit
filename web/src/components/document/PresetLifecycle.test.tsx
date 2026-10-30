import { defaultScheduler, notifyManager, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { api } from "../../api/client";
import type { EditResult, SaveDocument, ToolkitPreset } from "../../api/protocol";
import { useDocument, type DocumentContextValue } from "../../state/documentStore";
import { deferred } from "../../test/deferred";
import { installDialogStub } from "../../test/dialog";
import { DocumentEditor } from "./DocumentEditor";

let context: DocumentContextValue | undefined;
vi.mock("./ViewRenderer", () => ({ ViewRenderer: function Probe() { context = useDocument(); return null; } }));
const clients: QueryClient[] = [];
let restoreDialog: () => void;
beforeEach(() => { notifyManager.setScheduler(queueMicrotask); restoreDialog = installDialogStub(); });
afterEach(() => {
  cleanup(); clients.splice(0).forEach(client => client.clear()); vi.restoreAllMocks();
  notifyManager.setScheduler(defaultScheduler); restoreDialog(); context = undefined;
});

const snapshot = (revision = 1, actionId = "capture"): SaveDocument => ({
  id: "d", fileId: "f", title: "Authored save", filePath: "save.dat", canEdit: true,
  plugin: { id: "test", name: "Test", version: "1", maturity: "Experimental", games: [], references: [] },
  state: { revision, isDirty: false, canUndo: false, canRedo: false },
  view: { type: "action", actionId, label: "Capture", supportsPlanning: true },
});
const preset: ToolkitPreset = { schemaVersion: "1", id: "p", name: "Saved",
  compatibility: { pluginId: "test", pluginVersion: "1", titleIds: [] },
  actions: [{ actionId: "capture", parameters: {}, scopeKind: "document" }], createdAt: "", updatedAt: "" };
const preview = (stale = false, label = "Reviewed") => ({ preset, preview: {
  planId: stale ? undefined : "plan", revision: 1, stale, scope: {},
  plan: { changes: [{ valueId: "value", label, oldValue: 1, newValue: 2, oldExists: true, newExists: true, derived: false }],
    errors: [], warnings: [], skippedRows: [] },
} });
const accepted: EditResult = { ok: true, changes: [], errors: [], warnings: [], structureChanged: false,
  state: { revision: 2, isDirty: true, canUndo: true, canRedo: false } };

async function mount() {
  vi.spyOn(api, "document").mockResolvedValue(snapshot());
  vi.spyOn(api, "sources").mockResolvedValue({ datasets: [] });
  vi.spyOn(api, "documentPresets").mockResolvedValue([{ preset,
    compatibility: { compatible: true, canPreview: true, canApply: true, readOnly: false,
      presetKind: "toolkit.action-parameters", savedPluginVersion: "1", currentPluginVersion: "1", unsupportedActions: [] } }]);
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } });
  clients.push(client);
  const toast = vi.fn();
  const editor = () => <QueryClientProvider client={client}><DocumentEditor workspaceId="w" documentId="d"
    file={undefined} onSwitchPlugin={vi.fn()} onToast={toast} /></QueryClientProvider>;
  const rendered = render(editor());
  fireEvent.click(await screen.findByText("Toolkit presets"));
  await screen.findByRole("button", { name: "Preview" });
  return { client, toast, rendered, editor };
}

it("clears a failed read through one controller owner after same-descriptor retry", async () => {
  vi.spyOn(api, "presetPreview").mockResolvedValueOnce(preview(true)).mockResolvedValue(preview());
  await mount();
  vi.mocked(api.document).mockRejectedValueOnce(new Error("503 authored read failure"));
  fireEvent.click(screen.getByRole("button", { name: "Preview" }));
  await waitFor(() => expect(screen.getAllByRole("alert")).toHaveLength(1));
  await waitFor(() => expect(context?.refreshState).toBe("failed"));
  // The lazy review leaf must be mounted before checking its installed-read guard/alert owner.
  await screen.findByRole("button", { name: "Preview again" });
  expect(screen.getByRole("alert").textContent).toContain("503 authored read failure");
  expect(screen.getByRole("button", { name: "Preview again" }).hasAttribute("disabled")).toBe(true);
  vi.mocked(api.document).mockResolvedValue(snapshot(3));
  fireEvent.click(screen.getByRole("button", { name: "Refresh document" }));
  await waitFor(() => expect(context?.refreshState).toBe("ready"));
  expect(screen.getAllByRole("alert")).toHaveLength(1); // The still-stale preview owns its one message.
  expect(screen.getByRole("button", { name: "Preview again" }).hasAttribute("disabled")).toBe(false);
  fireEvent.click(screen.getByRole("button", { name: "Preview again" }));
  await waitFor(() => expect(api.presetPreview).toHaveBeenLastCalledWith("w", "d", "p", 3, {}));
});

it("guards capture retry, form submission and Apply while a required read is pending or failed", async () => {
  const capture = vi.spyOn(api, "capturePreset").mockRejectedValueOnce(new Error("capture unavailable")).mockResolvedValue(preset);
  const apply = vi.spyOn(api, "presetApply").mockResolvedValue(accepted);
  vi.spyOn(api, "presetPreview").mockResolvedValue(preview());
  await mount();
  fireEvent.change(screen.getByLabelText("Name"), { target: { value: "Saved" } });
  fireEvent.click(screen.getByRole("button", { name: "Save preset" }));
  await screen.findByRole("button", { name: "Retry saving preset" });
  fireEvent.click(screen.getByRole("button", { name: "Preview" }));
  await screen.findByRole("heading", { name: "Preset preview" });
  const read = deferred<SaveDocument>();
  vi.mocked(api.document).mockReturnValueOnce(read.promise);
  let refresh!: Promise<void>;
  await act(async () => { refresh = context!.refresh(); });
  for (const status of ["pending", "failed"]) {
    if (status === "failed") await act(async () => { read.reject(new Error("503")); await refresh; });
    await waitFor(() => expect(context?.refreshState).toBe(status));
    expect(screen.getByRole("button", { name: "Retry saving preset" }).hasAttribute("disabled")).toBe(true);
    expect(screen.getByRole("button", { name: "Apply preset" }).hasAttribute("disabled")).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: "Retry saving preset" }));
    fireEvent.submit(screen.getByRole("button", { name: "Save preset" }).closest("form")!);
    fireEvent.click(screen.getByRole("button", { name: "Apply preset" }));
    expect(capture).toHaveBeenCalledTimes(1);
    expect(apply).not.toHaveBeenCalled();
  }
  vi.mocked(api.document).mockResolvedValue(snapshot(2));
  await act(async () => { await context!.refresh(); });
  await waitFor(() => expect(context?.refreshState).toBe("ready"));
  fireEvent.click(screen.getByRole("button", { name: "Retry saving preset" }));
  await waitFor(() => expect(capture).toHaveBeenCalledTimes(2));
  fireEvent.click(screen.getByRole("button", { name: "Apply preset" }));
  await waitFor(() => expect(apply).toHaveBeenCalledOnce());
});

it.each(["descriptor", "delete"])("does not reinstall an obsolete preview after %s invalidation", async kind => {
  const pending = deferred<ReturnType<typeof preview>>();
  vi.spyOn(api, "presetPreview").mockReturnValue(pending.promise);
  vi.spyOn(api, "deletePreset").mockResolvedValue(undefined);
  await mount();
  fireEvent.click(screen.getByRole("button", { name: "Preview" }));
  await waitFor(() => expect(api.presetPreview).toHaveBeenCalledOnce());
  if (kind === "descriptor") {
    vi.mocked(api.document).mockResolvedValue(snapshot(2, "replacement"));
    await act(async () => { await context!.refresh(); });
  } else {
    fireEvent.click(screen.getByRole("button", { name: "Delete" }));
    fireEvent.click(await screen.findByRole("button", { name: "Delete preset" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  }
  await act(async () => { pending.resolve(preview()); await pending.promise; });
  expect(screen.queryByRole("heading", { name: "Preset preview" })).toBeNull();
});

it("does not let an old Apply settlement close or focus a replacement review", async () => {
  const pending = deferred<EditResult>();
  vi.spyOn(api, "presetApply").mockReturnValue(pending.promise);
  vi.spyOn(api, "presetPreview").mockResolvedValueOnce(preview(false, "Old intent")).mockResolvedValue(preview(false, "Replacement intent"));
  const { client, toast } = await mount();
  fireEvent.click(screen.getByRole("button", { name: "Preview" }));
  await screen.findByRole("heading", { name: "Preset preview" });
  fireEvent.click(screen.getByRole("button", { name: "Apply preset" }));
  await waitFor(() => expect(api.presetApply).toHaveBeenCalledOnce());
  await act(async () => { client.setQueryData(["document", "w", "d"], snapshot(2, "replacement")); });
  await waitFor(() => expect(screen.queryByRole("heading", { name: "Preset preview" })).toBeNull());
  // An in-flight Apply keeps row triggers disabled. Replacing a review must not require bypassing that guard.
  await act(async () => { pending.resolve(accepted); await pending.promise; });
  fireEvent.click(screen.getByRole("button", { name: "Preview" }));
  const heading = await screen.findByRole("heading", { name: "Preset preview" });
  expect(within(screen.getByRole("region", { name: "Preset preview" })).getByText("Replacement intent")).not.toBeNull();
  await act(async () => { await new Promise(resolve => requestAnimationFrame(resolve)); });
  expect(globalThis.document.activeElement).toBe(heading);
  expect(toast).not.toHaveBeenCalled();
});
