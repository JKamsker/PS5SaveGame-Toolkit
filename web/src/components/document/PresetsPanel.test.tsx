import { cleanup, fireEvent, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { afterEach, expect, it, vi } from "vitest";
import { api } from "../../api/client";
import type { SaveDocument, ViewNode } from "../../api/protocol";
import { PresetsPanel } from "./PresetsPanel";
import { ViewRenderer } from "./ViewRenderer";
import { renderSpatial } from "./spatial/spatialTestUtils";
import { DocumentContext } from "../../state/documentStore";
import { ReviewNavigationProvider } from "./review/navigation";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });

const action = { type: "action" as const, actionId: "capture", label: "Capture", supportsPlanning: true, selectionTableId: "items" };
const view: ViewNode = { type: "section", title: "Root", children: [action, {
  type: "table", title: "Items", selectionId: "items", columns: [{ key: "name", label: "Name" }],
  rows: [{ key: "slot/0", cells: { name: { type: "text", text: "Potion" } } }],
}] };
const document: SaveDocument = {
  id: "d1", fileId: "f1", filePath: "save.dat", title: "Save", titleId: "PPSA00001", canEdit: true,
  plugin: { id: "test", name: "Test", version: "1", maturity: "Experimental", games: [], references: [] },
  state: { canUndo: false, canRedo: false, isDirty: false, revision: 0 }, view,
  presetCapability: { schemaVersion: "1", maxActions: 1, actions: [{ actionId: "capture", label: "Capture", parameterIds: [], scopeKinds: ["document", "selected"] }] },
};

it("captures the current selected table rows through the selected scope", async () => {
  vi.spyOn(api, "presets").mockResolvedValue([]);
  const capture = vi.spyOn(api, "capturePreset").mockResolvedValue({
    schemaVersion: "1", id: "p", name: "Selected", compatibility: { pluginId: "test", pluginVersion: "1", titleIds: ["PPSA00001"] },
    actions: [{ actionId: "capture", parameters: {}, scopeKind: "selected" }], createdAt: "", updatedAt: "",
  });
  renderSpatial(<><ViewRenderer node={view} /><PresetsPanel document={document} onToast={vi.fn()} /></>);
  fireEvent.click(screen.getByText("Toolkit presets"));
  await screen.findByRole("button", { name: "Save preset" });
  fireEvent.click(screen.getByRole("checkbox", { name: "Select all shown rows" }));
  fireEvent.change(screen.getByLabelText("Name"), { target: { value: "Selected" } });
  fireEvent.change(screen.getByLabelText("Scope"), { target: { value: "selected" } });
  fireEvent.click(screen.getByRole("button", { name: "Save preset" }));
  await waitFor(() => expect(capture).toHaveBeenCalledWith("w1", "d1", "Selected", expect.objectContaining({ scopeKind: "selected", selectedRowKeys: ["slot/0"], allMatches: false })));
});

it("shows a truthful no-actions state without a never-ending loading message", async () => {
  const noActions = { ...document, view: { type: "section" as const, title: "Root", children: [] } };
  const presets = vi.spyOn(api, "presets");
  renderSpatial(<PresetsPanel document={noActions} onToast={vi.fn()} />);
  fireEvent.click(screen.getByText("Toolkit presets"));
  expect((await screen.findByText(/no planned preset actions/i)).textContent).toContain("no planned preset actions");
  expect(screen.queryByText("Loading presets…")).toBeNull();
  expect(presets).not.toHaveBeenCalled();
});

it("does not expose generic capture when the server reports malformed capability", async () => {
  vi.spyOn(api, "documentPresets").mockResolvedValue([]);
  renderSpatial(<PresetsPanel document={{ ...document, presetCapabilityStatus: "malformed", presetCapabilityReason: "capabilityMalformed" }} onToast={vi.fn()} />);
  fireEvent.click(screen.getByText("Toolkit presets"));
  expect((await screen.findByRole("alert")).textContent).toContain("capabilityMalformed");
  expect(screen.queryByRole("option", { name: "Capture" })).toBeNull();
  expect(screen.queryByRole("button", { name: "Save preset" })?.hasAttribute("disabled")).toBe(true);
});

it("filters capture actions to published scopes and starts on the first scope", async () => {
  vi.spyOn(api, "documentPresets").mockResolvedValue([]);
  const unpublished = { ...action, actionId: "unpublished", label: "Unpublished" };
  const semanticDocument = { ...document, view: { ...view, children: [action, unpublished, view.children[1]!] },
    presetCapability: { schemaVersion: "1", maxActions: 1, actions: [{ actionId: "capture", label: "Capture", parameterIds: [], scopeKinds: ["allEligible"] }] } };
  renderSpatial(<PresetsPanel document={semanticDocument} onToast={vi.fn()} />);
  fireEvent.click(screen.getByText("Toolkit presets"));
  await screen.findByRole("button", { name: "Save preset" });
  expect((screen.getByLabelText("Action") as HTMLSelectElement).value).toBe("capture");
  expect((screen.getByLabelText("Scope") as HTMLSelectElement).value).toBe("allEligible");
  expect(screen.queryByRole("option", { name: "Unpublished" })).toBeNull();
});

it("deduplicates repeated action nodes and rejects conflicting action contexts", async () => {
  vi.spyOn(api, "documentPresets").mockResolvedValue([]);
  const repeated = { ...action };
  const duplicateDocument = { ...document, view: { ...view, children: [action, repeated, view.children[1]!] } };
  const first = renderSpatial(<PresetsPanel document={duplicateDocument} onToast={vi.fn()} />);
  fireEvent.click(screen.getByText("Toolkit presets"));
  await screen.findByRole("button", { name: "Save preset" });
  expect(screen.getAllByRole("option", { name: "Capture" })).toHaveLength(1);
  first.unmount();

  const conflicting = { ...action, parameters: [{ type: "field" as const, valueId: "other", label: "Other", spec: { kind: "text" as const, maxLength: 4 }, value: "x" }] };
  const conflictingDocument = { ...document, view: { ...view, children: [action, conflicting, view.children[1]!] }, presetCapability: { schemaVersion: "1", maxActions: 1, actions: [{ actionId: "capture", label: "Capture", parameterIds: [], scopeKinds: ["document"] }] } };
  renderSpatial(<PresetsPanel document={conflictingDocument} onToast={vi.fn()} />);
  fireEvent.click(screen.getByText("Toolkit presets"));
  await waitFor(() => expect(screen.queryByRole("option", { name: "Capture" })).toBeNull());
});

it("resets capture state when a live action descriptor changes without remounting", async () => {
  vi.spyOn(api, "documentPresets").mockResolvedValue([]);
  const capture = vi.spyOn(api, "capturePreset").mockResolvedValue({
    schemaVersion: "1", id: "p", name: "Current", compatibility: { pluginId: "test", pluginVersion: "1", titleIds: ["PPSA00001"] },
    actions: [{ actionId: "next", parameters: { fresh: 3 }, scopeKind: "allEligible" }], createdAt: "", updatedAt: "",
  });
  const oldAction = { ...action, actionId: "old", label: "Old", parameters: [{ type: "field" as const, valueId: "stale", label: "Stale", spec: { kind: "integer" as const, min: "0", max: "9" }, value: 1 }] };
  const nextAction = { ...action, actionId: "next", label: "Next", parameters: [{ type: "field" as const, valueId: "fresh", label: "Fresh", spec: { kind: "integer" as const, min: "0", max: "9" }, value: 2 }] };
  const initial = { ...document, view: { ...view, children: [oldAction, view.children[1]!] }, presetCapability: { schemaVersion: "1", maxActions: 1, actions: [{ actionId: "old", label: "Old", parameterIds: ["stale"], parameterSpecs: [{ kind: "integer" as const, min: "0", max: "9" }], scopeKinds: ["document"] }] } };
  const updated = { ...document, view: { ...view, children: [nextAction, view.children[1]!] }, presetCapability: { schemaVersion: "1", maxActions: 1, actions: [{ actionId: "next", label: "Next", parameterIds: ["fresh"], parameterSpecs: [{ kind: "integer" as const, min: "0", max: "9" }], scopeKinds: ["allEligible"] }] } };
  const rendered = renderSpatial(<PresetsPanel document={initial} onToast={vi.fn()} />);
  fireEvent.click(screen.getByText("Toolkit presets"));
  await screen.findByRole("button", { name: "Save preset" });
  fireEvent.change(screen.getByLabelText("Name"), { target: { value: "Current" } });
  rendered.rerender(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><DocumentContext.Provider value={rendered.context}><ReviewNavigationProvider><PresetsPanel document={updated} onToast={vi.fn()} /></ReviewNavigationProvider></DocumentContext.Provider></QueryClientProvider>);
  await waitFor(() => expect((screen.getByLabelText("Action") as HTMLSelectElement).value).toBe("next"));
  expect(screen.queryByLabelText("Stale")).toBeNull();
  expect((screen.getByLabelText("Scope") as HTMLSelectElement).value).toBe("allEligible");
  fireEvent.click(screen.getByRole("button", { name: "Save preset" }));
  await waitFor(() => expect(capture).toHaveBeenCalledWith("w1", "d1", "Current", expect.objectContaining({ actionId: "next", parameters: { fresh: 2 }, scopeKind: "allEligible" })));
});

it("normalizes legacy scope descriptors before live reset", async () => {
  vi.spyOn(api, "documentPresets").mockResolvedValue([]);
  const initial = { ...document, presetCapability: { schemaVersion: "1", maxActions: 1, actions: [{ actionId: "capture", label: "Capture", parameterIds: [], scopeKind: "document" }] } };
  const updated = { ...document, presetCapability: { schemaVersion: "1", maxActions: 1, actions: [{ actionId: "capture", label: "Capture", parameterIds: [], scopeKind: "allEligible" }] } };
  const rendered = renderSpatial(<PresetsPanel document={initial} onToast={vi.fn()} />);
  fireEvent.click(screen.getByText("Toolkit presets"));
  await screen.findByRole("button", { name: "Save preset" });
  rendered.rerender(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><DocumentContext.Provider value={rendered.context}><ReviewNavigationProvider><PresetsPanel document={updated} onToast={vi.fn()} /></ReviewNavigationProvider></DocumentContext.Provider></QueryClientProvider>);
  await waitFor(() => expect((screen.getByLabelText("Scope") as HTMLSelectElement).value).toBe("allEligible"));
});

it("shows server-owned saved and current compatibility boundaries", async () => {
  vi.spyOn(api, "documentPresets").mockResolvedValue([{
    preset: { schemaVersion: "1", id: "p", name: "Boundaries", compatibility: { pluginId: "test", pluginVersion: "1.0", titleIds: ["PPSA00001"] },
      actions: [{ actionId: "capture", parameters: {}, scopeKind: "document" }], createdAt: "", updatedAt: "" },
    compatibility: { compatible: true, readOnly: false, canPreview: true, presetKind: "toolkit.action-parameters", savedPluginVersion: "1.0", currentPluginVersion: "1.1", savedFormatVariant: "remake", currentFormatVariant: "rebirth", savedReferencePackIdentity: "saved-pack", currentReferencePackIdentity: "current-pack", savedSchemaFingerprint: "saved-schema", currentSchemaFingerprint: "current-schema", savedFileRole: "save", currentFileRole: "save", savedCapabilitySchemaVersion: "1", currentCapabilitySchemaVersion: "2", unsupportedActions: [] },
  }]);
  renderSpatial(<PresetsPanel document={document} onToast={vi.fn()} />);
  fireEvent.click(screen.getByText("Toolkit presets"));
  await screen.findByText("Compatibility boundaries");
  expect(screen.getByText("1.0 → 1.1")).not.toBeNull();
  expect(screen.getByText("saved-pack → current-pack")).not.toBeNull();
  expect(screen.getByText("remake → rebirth")).not.toBeNull();
});

it("keeps host rename available when the document is read-only", async () => {
  vi.spyOn(api, "documentPresets").mockResolvedValue([{
    preset: { schemaVersion: "1", id: "p", name: "Saved", compatibility: { pluginId: "test", pluginVersion: "1", titleIds: ["PPSA00001"] },
      actions: [{ actionId: "capture", parameters: {}, scopeKind: "document" }], createdAt: "", updatedAt: "" },
    compatibility: { compatible: true, readOnly: true, canPreview: false, presetKind: "toolkit.action-parameters", savedPluginVersion: "1", currentPluginVersion: "1", unsupportedActions: [] },
  }]);
  const rename = vi.spyOn(api, "renamePreset").mockResolvedValue({
    schemaVersion: "1", id: "p", name: "Renamed", compatibility: { pluginId: "test", pluginVersion: "1", titleIds: ["PPSA00001"] },
    actions: [{ actionId: "capture", parameters: {}, scopeKind: "document" }], createdAt: "", updatedAt: "",
  });
  renderSpatial(<PresetsPanel document={{ ...document, canEdit: false }} onToast={vi.fn()} />);
  fireEvent.click(screen.getByText("Toolkit presets"));
  const button = await screen.findByRole("button", { name: "Rename" });
  expect(button.hasAttribute("disabled")).toBe(false);
  fireEvent.click(button);
  fireEvent.change(screen.getByLabelText("Preset name"), { target: { value: "Renamed" } });
  fireEvent.click(screen.getByRole("button", { name: "Save name" }));
  await waitFor(() => expect(rename).toHaveBeenCalledWith("p", "Renamed"));
});

it("retires a preview and refreshes the document when apply returns an accepted stale result", async () => {
  vi.spyOn(api, "documentPresets").mockResolvedValue([{
    preset: { schemaVersion: "1", id: "p", name: "Saved", compatibility: { pluginId: "test", pluginVersion: "1", titleIds: ["PPSA00001"] },
      actions: [{ actionId: "capture", parameters: {}, scopeKind: "document" }], createdAt: "", updatedAt: "" },
    compatibility: { compatible: true, readOnly: false, canPreview: true, canApply: true, presetKind: "toolkit.action-parameters", savedPluginVersion: "1", currentPluginVersion: "1", unsupportedActions: [] },
  }]);
  vi.spyOn(api, "presetPreview").mockResolvedValue({ preset: {} as never, preview: { planId: "plan", revision: 0, scope: {}, plan: { changes: [], skippedRows: [], warnings: [], errors: [] } } });
  vi.spyOn(api, "presetApply").mockResolvedValue({ ok: false, stale: true, errors: [], changes: [], warnings: [], structureChanged: false, state: { canUndo: false, canRedo: false, isDirty: false, revision: 1 } });
  const toast = vi.fn();
  const rendered = renderSpatial(<PresetsPanel document={document} onToast={toast} />);
  fireEvent.click(screen.getByText("Toolkit presets"));
  fireEvent.click(await screen.findByRole("button", { name: "Preview" }));
  await screen.findByRole("heading", { name: "Preset preview" });
  fireEvent.click(screen.getByRole("button", { name: "Apply preset" }));
  await waitFor(() => expect(screen.getByRole("heading", { name: "Preset preview is stale" })).not.toBeNull());
  expect(screen.getByRole("button", { name: "Apply preset" }).hasAttribute("disabled")).toBe(true);
  expect(rendered.context.refresh).toHaveBeenCalledTimes(1);
  expect(screen.getAllByRole("alert")).toHaveLength(1);
  expect(toast).not.toHaveBeenCalled();
});

it("renders storage failures as one actionable alert", async () => {
  vi.spyOn(api, "documentPresets").mockRejectedValue(new Error("Preset storage is temporarily unavailable. Retry the operation."));
  renderSpatial(<PresetsPanel document={document} onToast={vi.fn()} />);
  fireEvent.click(screen.getByText("Toolkit presets"));
  const alert = await screen.findByRole("alert");
  expect(alert.textContent).toMatch(/Preset storage is temporarily unavailable/i);
  expect(screen.getAllByRole("alert")).toHaveLength(1);
});

it("closes the inline preview on Escape and restores the Preview opener", async () => {
  vi.spyOn(api, "documentPresets").mockResolvedValue([{
    preset: { schemaVersion: "1", id: "p", name: "Saved", compatibility: { pluginId: "test", pluginVersion: "1", titleIds: ["PPSA00001"] },
      actions: [{ actionId: "capture", parameters: {}, scopeKind: "document" }], createdAt: "", updatedAt: "" },
    compatibility: { compatible: true, readOnly: false, canPreview: true, canApply: true, presetKind: "toolkit.action-parameters", savedPluginVersion: "1", currentPluginVersion: "1", unsupportedActions: [] },
  }]);
  vi.spyOn(api, "presetPreview").mockResolvedValue({ preset: {} as never, preview: { planId: "plan", revision: 0, scope: {}, plan: { changes: [], skippedRows: [], warnings: [], errors: [] } } });
  renderSpatial(<PresetsPanel document={document} onToast={vi.fn()} />);
  fireEvent.click(screen.getByText("Toolkit presets"));
  const opener = await screen.findByRole("button", { name: "Preview" });
  opener.focus();
  fireEvent.click(opener);
  const heading = await screen.findByRole("heading", { name: "Preset preview" });
  fireEvent.keyDown(heading, { key: "Escape" });
  await waitFor(() => expect(screen.queryByRole("heading", { name: "Preset preview" })).toBeNull());
  await waitFor(() => expect(globalThis.document.activeElement).toBe(opener));
});
