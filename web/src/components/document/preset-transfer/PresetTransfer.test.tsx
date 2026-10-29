import { cleanup, fireEvent, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { api } from "../../../api/client";
import { ApiError } from "../../../api/http";
import { presetTransfer, type ImportPreview } from "../../../api/presetTransfer";
import type { SaveDocument } from "../../../api/protocol";
import { renderSpatial } from "../spatial/spatialTestUtils";
import { PresetTransferToolbar } from "./PresetTransferToolbar";
import { installDialogStub } from "../../../test/dialog";

const document: SaveDocument = {
  id: "d", fileId: "f", filePath: "synthetic", title: "Test", titleId: "PPSA00001", canEdit: false,
  plugin: { id: "test", name: "Test", version: "1", maturity: "Experimental", games: [], references: [] },
  state: { canUndo: false, canRedo: false, isDirty: false, revision: 0 }, view: { type: "text", text: "Fixture" },
};
const preview: ImportPreview = {
  token: "initial", schemaVersion: "1", provenance: { producer: "Toolkit", exportedAt: "2026-10-29T00:00:00Z" },
  readyToCommit: false, expiresAt: "2026-10-29T00:10:00Z", items: [{ index: 0, name: "装備", actionCount: 1,
    compatibility: { pluginId: "other", pluginVersion: "1", titleIds: ["OTHER"] }, repeatedPackageName: false,
    conflicts: [{ id: "existing", name: "装備" }] }],
};
let restoreDialog = () => {};
beforeEach(() => {
  restoreDialog = installDialogStub({ dispatchCloseEvent: true });
  vi.spyOn(presetTransfer, "cancel").mockResolvedValue(undefined);
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); restoreDialog(); });

async function chooseFile() {
  fireEvent.change(await screen.findByLabelText("Preset package"), { target: { files: [new File(["exact original bytes"], "presets.json")] } });
}

it("requires explicit conflicts and resulting-name review before a readonly host import; cancellation restores focus", async () => {
  vi.spyOn(presetTransfer, "preview").mockResolvedValue(preview);
  vi.spyOn(presetTransfer, "resolve").mockResolvedValue({ ...preview, token: "reviewed", readyToCommit: true,
    items: [{ ...preview.items[0]!, resultingName: "装備 (2)" }] });
  const commit = vi.spyOn(presetTransfer, "commit").mockResolvedValue({ imported: [{ id: "new", name: "装備 (2)" }] });
  renderSpatial(<PresetTransferToolbar document={document} />);
  const trigger = screen.getByRole("button", { name: "Import presets" });
  fireEvent.click(trigger); await chooseFile();
  expect((await screen.findByRole("button", { name: "Review resulting names" })).hasAttribute("disabled")).toBe(true);
  expect(screen.getByText(/Stored for later/).textContent).toContain("Different plugin version");
  fireEvent.click(screen.getByRole("radio", { name: /Keep both/ }));
  fireEvent.click(screen.getByRole("button", { name: "Review resulting names" }));
  await screen.findByText("装備 (2)"); expect(commit).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "Confirm import" }));
  await waitFor(() => expect(commit).toHaveBeenCalledWith("reviewed"));
  await waitFor(() => expect(globalThis.document.activeElement).toBe(trigger));
  expect(screen.getByRole("status").textContent).toContain("Save data and history are unchanged");
});

it("shows a single stale alert and preserves a file for validation retry, with no commit bypass", async () => {
  vi.spyOn(presetTransfer, "preview").mockResolvedValue(preview);
  vi.spyOn(presetTransfer, "resolve").mockRejectedValue(new ApiError("Library changed. Preview again.", 409));
  const commit = vi.spyOn(presetTransfer, "commit");
  renderSpatial(<PresetTransferToolbar document={document} />);
  fireEvent.click(screen.getByRole("button", { name: "Import presets" })); await chooseFile();
  fireEvent.click(await screen.findByRole("radio", { name: /Keep both/ }));
  fireEvent.click(screen.getByRole("button", { name: "Review resulting names" }));
  await screen.findByRole("alert"); expect(screen.getAllByRole("alert")).toHaveLength(1);
  expect(screen.getByRole("button", { name: "Review resulting names" }).hasAttribute("disabled")).toBe(true);
  fireEvent.click(screen.getByRole("button", { name: "Preview file again" }));
  await waitFor(() => expect(presetTransfer.preview).toHaveBeenCalledTimes(2));
  expect(commit).not.toHaveBeenCalled();
});

it("requires an explicit export selection and retains it for a failed-download retry", async () => {
  vi.spyOn(api, "presets").mockResolvedValue([{ schemaVersion: "1", id: "one", name: "装備", compatibility: preview.items[0]!.compatibility,
    actions: [], createdAt: "", updatedAt: "" }]);
  const download = vi.spyOn(presetTransfer, "download").mockRejectedValueOnce(new ApiError("Storage unavailable", 503)).mockResolvedValue(undefined);
  renderSpatial(<PresetTransferToolbar document={document} />);
  const trigger = screen.getByRole("button", { name: "Export presets" }); fireEvent.click(trigger);
  expect((await screen.findByRole("button", { name: "Download 0 presets" })).hasAttribute("disabled")).toBe(true);
  fireEvent.click(await screen.findByRole("checkbox", { name: /Select all/ }));
  fireEvent.click(screen.getByRole("button", { name: "Download 1 preset" }));
  expect((await screen.findByRole("alert")).textContent).toContain("Storage unavailable");
  fireEvent.click(screen.getByRole("button", { name: "Retry download" }));
  await waitFor(() => expect(download).toHaveBeenNthCalledWith(2, ["one"]));
  await waitFor(() => expect(globalThis.document.activeElement).toBe(trigger));
});
