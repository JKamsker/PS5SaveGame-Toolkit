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

it("reload recovery retains server-incompatible summaries instead of enabling a preset", async () => {
  const preset = { schemaVersion: "1", id: "p", name: "Later", compatibility: { pluginId: "other", pluginVersion: "1", titleIds: ["OTHER"] },
    actions: [{ actionId: "capture", parameters: {}, scopeKind: "document" }], createdAt: "", updatedAt: "" };
  const fetch = vi.spyOn(api, "documentPresets").mockRejectedValueOnce(new Error("Storage unavailable")).mockResolvedValue([{
    preset, compatibility: { compatible: false, reason: "Server plugin mismatch", code: "pluginUnavailable", readOnly: false,
      presetKind: "toolkit.action-parameters", unsupportedActions: [] },
  }]);
  const reload = vi.spyOn(api, "reloadPresets").mockResolvedValue([preset]);
  renderSpatial(<PresetsPanel document={document} onToast={vi.fn()} />);
  fireEvent.click(screen.getByText("Toolkit presets"));
  fireEvent.click(await screen.findByRole("button", { name: "Retry loading presets" }));
  const preview = await screen.findByRole("button", { name: "Server plugin mismatch" });
  expect(preview.hasAttribute("disabled")).toBe(true);
  expect(reload).toHaveBeenCalledOnce();
  expect(fetch).toHaveBeenNthCalledWith(2, "w1", "d1");
});
