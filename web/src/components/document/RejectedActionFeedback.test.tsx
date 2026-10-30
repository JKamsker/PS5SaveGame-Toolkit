import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { actionReview, type ActionPreview } from "../../api/actionReview";
import { api } from "../../api/client";
import type { EditResult, SaveDocument, ToolkitPreset } from "../../api/protocol";
import { installDialogStub } from "../../test/dialog";
import { DocumentEditor } from "./DocumentEditor";

const clients: QueryClient[] = [];
let restoreDialog: () => void;
beforeEach(() => { restoreDialog = installDialogStub({ dispatchCloseEvent: true }); });
afterEach(() => {
  cleanup(); clients.splice(0).forEach(client => client.clear());
  vi.restoreAllMocks(); vi.unstubAllGlobals(); restoreDialog();
});

const state = { revision: 1, isDirty: true, canUndo: true, canRedo: false };
const snapshot: SaveDocument = {
  id: "d", fileId: "f", filePath: "save.dat", title: "Save", canEdit: true, state,
  plugin: { id: "test", name: "Test", version: "1", maturity: "Experimental", games: [], references: [] },
  view: { type: "action", actionId: "refill", label: "Refill", supportsPlanning: true },
};
const preset: ToolkitPreset = {
  schemaVersion: "1", id: "p", name: "Saved refill", createdAt: "", updatedAt: "",
  compatibility: { pluginId: "test", pluginVersion: "1", titleIds: [] },
  actions: [{ actionId: "refill", parameters: {}, scopeKind: "document" }],
};
const preview: ActionPreview = {
  planId: "plan", revision: 1, scope: {}, plan: { errors: [], warnings: [], skippedRows: [], changes: [
    { valueId: "quantity", label: "Quantity", oldValue: "2", newValue: "9", oldExists: true, newExists: true, derived: false },
  ] },
};

async function mount() {
  vi.spyOn(api, "document").mockResolvedValue(snapshot);
  vi.spyOn(api, "sources").mockResolvedValue({ datasets: [] });
  vi.spyOn(api, "documentPresets").mockResolvedValue([{ preset, compatibility: {
    compatible: true, canPreview: true, canApply: true, readOnly: false, presetKind: "toolkit.action-parameters",
    savedPluginVersion: "1", currentPluginVersion: "1", unsupportedActions: [],
  } }]);
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } });
  clients.push(client);
  const toast = vi.fn();
  render(<QueryClientProvider client={client}><DocumentEditor workspaceId="w" documentId="d"
    file={undefined} onSwitchPlugin={vi.fn()} onToast={toast} /></QueryClientProvider>);
  await screen.findByRole("button", { name: "Refill" });
  return toast;
}

it.each(["planned action", "preset"])("shows rollback and outcome diagnostics from a rejected %s", async kind => {
  const warning = "The failed operation could not be fully reverted; undo history was cleared.";
  const message = "Some earlier changes remain applied.";
  const rejected: EditResult = {
    ok: false, errors: [{ valueId: "quantity", message: "Quantity could not be changed." }],
    warnings: [warning], message, changes: [], structureChanged: false,
    state: { ...state, canUndo: false },
  };
  // Exercise the typed error response through the real API client and document mutation owner.
  const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify(rejected), {
    status: 422, headers: { "content-type": "application/json" },
  }));
  vi.stubGlobal("fetch", fetch);
  vi.spyOn(actionReview, "plan").mockResolvedValue(preview);
  vi.spyOn(api, "presetPreview").mockResolvedValue({ preset, preview });
  const toast = await mount();
  if (kind === "planned action") fireEvent.click(screen.getByRole("button", { name: "Refill" }));
  else {
    fireEvent.click(screen.getByText("Toolkit presets"));
    fireEvent.click(await screen.findByRole("button", { name: "Preview" }));
  }
  fireEvent.click(await screen.findByRole("button", { name: kind === "planned action" ? "Apply" : "Apply preset" }));
  const alert = await screen.findByRole("alert");
  expect(alert.textContent).toContain("Quantity could not be changed.");
  expect(alert.textContent).toContain(warning);
  expect(alert.textContent).toContain(message);
  expect(fetch).toHaveBeenCalledOnce();
  expect(toast).not.toHaveBeenCalled();
  await waitFor(() => expect(screen.getByRole("button", { name: "Undo" })).toHaveProperty("disabled", true));
  if (kind === "planned action") expect(within(screen.getByRole("dialog")).getByRole("alert")).toBe(alert);
});
