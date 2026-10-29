import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import { afterEach, expect, it, vi } from "vitest";
import { api } from "../../api/client";
import type { SaveDocument, SaveFileInfo, Workspace } from "../../api/protocol";
import { library } from "../../state/libraryStore";
import { HomePage } from "../home/HomePage";
import { WorkspacePage } from "./WorkspacePage";

afterEach(() => {
  cleanup(); vi.restoreAllMocks();
  Reflect.deleteProperty(HTMLDialogElement.prototype, "showModal");
  Reflect.deleteProperty(HTMLDialogElement.prototype, "close");
});

it("keeps import, file/plugin selection, keyboard editing and export discoverable by role and name", async () => {
  Object.defineProperty(HTMLDialogElement.prototype, "showModal", { configurable: true, value: function (this: HTMLDialogElement) { this.open = true; } });
  Object.defineProperty(HTMLDialogElement.prototype, "close", { configurable: true, value: function (this: HTMLDialogElement) { this.open = false; } });
  vi.spyOn(library, "restore").mockResolvedValue(undefined);
  vi.spyOn(api, "plugins").mockResolvedValue([]);
  vi.spyOn(api, "titles").mockResolvedValue([]);
  vi.spyOn(api, "workspaces").mockResolvedValue([]);
  vi.spyOn(api, "sources").mockResolvedValue({ datasets: [] });
  const file: SaveFileInfo = { id: "f", path: "save.dat", length: 16, matches: ["First", "Alternate"].map(name => ({
    pluginId: name, pluginName: name, score: 10, maturity: "Experimental",
  })) };
  const workspace: Workspace = { id: "w", name: "Imported dump", createdAt: "2026-10-29", documents: [], titles: [{
    titleId: "PPSA99999", directories: [{ id: "slot", name: "Slot 1", rawName: "sdimg_slot", isBackup: false,
      generation: 0, twinIdentical: false, hasIcon: false, files: [file] }],
  }] };
  const createDocument = (pluginId: string): SaveDocument => ({ id: pluginId, fileId: file.id, filePath: file.path, title: `${pluginId} editor`, canEdit: true,
    plugin: { id: pluginId, name: pluginId, version: "1", maturity: "Experimental", games: [], references: [] },
    state: { canUndo: false, canRedo: false, isDirty: false, revision: 0 },
    view: { type: "tabs", tabs: [
      { title: "Overview", children: [{ type: "text", text: "Save overview" }] },
      { title: "Inventory", children: [{ type: "field", valueId: "quantity", label: "Quantity", value: "2", spec: { kind: "integer", min: "0", max: "99" } }] },
    ] },
  });
  vi.spyOn(api, "workspace").mockImplementation(async () => structuredClone(workspace));
  vi.spyOn(api, "upload").mockResolvedValue(workspace);
  vi.spyOn(api, "open").mockImplementation(async (_workspace, _file, pluginId = "First") => {
    const doc = createDocument(pluginId);
    workspace.documents = [{ id: doc.id, fileId: file.id, filePath: file.path, title: doc.title, pluginId, isDirty: false }];
    return doc;
  });
  vi.spyOn(api, "document").mockImplementation(async (_workspace, id) => createDocument(id));
  vi.spyOn(api, "edit").mockResolvedValue({ ok: true, errors: [], warnings: [], structureChanged: false,
    changes: [{ valueId: "quantity", oldValue: "2", newValue: "7" }], state: { canUndo: true, canRedo: false, isDirty: true, revision: 1 } });
  vi.spyOn(api, "exportPlan").mockResolvedValue({ changedCount: 1, files: [{ relativePath: file.path, length: 16, reason: "edited" }] });
  // Model only the native chooser's file selection; all application interactions use named controls.
  vi.spyOn(HTMLInputElement.prototype, "click").mockImplementation(function (this: HTMLInputElement) {
    fireEvent.change(this, { target: { files: [new File(["invented fixture"], "fixture.zip")] } });
  });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } });
  render(<MemoryRouter><QueryClientProvider client={client}><Routes>
    <Route path="/" element={<HomePage />} /><Route path="/w/:workspaceId" element={<WorkspacePage />} />
  </Routes></QueryClientProvider></MemoryRouter>);
  fireEvent.click(screen.getByRole("button", { name: "Open zip" }));
  const picker = await screen.findByRole("heading", { name: "Pick a save file" });
  expect(document.activeElement).toBe(picker);
  const slot = screen.getByRole("button", { name: /Slot 1/ });
  fireEvent.click(slot);
  expect(slot.getAttribute("aria-expanded")).toBe("true");
  fireEvent.click(screen.getByRole("button", { name: /save.dat/ }));
  const firstHeading = await screen.findByRole("heading", { name: "First editor" });
  expect(document.activeElement).toBe(firstHeading);
  fireEvent.change(screen.getByRole("combobox", { name: "Open with" }), { target: { value: "Alternate" } });
  const alternateHeading = await screen.findByRole("heading", { name: "Alternate editor" });
  expect(document.activeElement).toBe(alternateHeading);
  const overview = screen.getByRole("tab", { name: "Overview" });
  overview.focus();
  fireEvent.keyDown(overview, { key: "ArrowRight" });
  expect(document.activeElement).toBe(screen.getByRole("tab", { name: "Inventory" }));
  const input = screen.getByRole("textbox", { name: "Quantity" });
  input.focus();
  fireEvent.change(input, { target: { value: "7" } });
  fireEvent.keyDown(input, { key: "Enter" });
  await waitFor(() => expect(api.edit).toHaveBeenCalledWith("w", "Alternate", [{ valueId: "quantity", value: "7" }], 0));
  fireEvent.click(screen.getByRole("button", { name: "Export" }));
  const dialog = within(await screen.findByRole("dialog", { name: "Export edited saves" }));
  expect(dialog.getByRole("group", { name: "What to include" })).toBeTruthy();
  expect(dialog.getByRole("radio", { name: /^Changed save folders/ })).toBeTruthy();
  await waitFor(() => expect(dialog.getByRole("status").textContent).toContain("Export plan ready"));
  expect((dialog.getByRole("button", { name: "Download zip" }) as HTMLButtonElement).disabled).toBe(false);
  fireEvent.click(dialog.getByRole("button", { name: "Cancel" }));
  await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  client.clear();
}, 10_000); // Import, two document opens, edit and export: 1.6 s locally; allow slower Windows CI.
