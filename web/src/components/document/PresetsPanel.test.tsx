import { cleanup, fireEvent, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { api } from "../../api/client";
import type { SaveDocument, ViewNode } from "../../api/protocol";
import { PresetsPanel } from "./PresetsPanel";
import { ViewRenderer } from "./ViewRenderer";
import { renderSpatial } from "./spatial/spatialTestUtils";

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
