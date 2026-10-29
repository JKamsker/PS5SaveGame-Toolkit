import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { afterEach, expect, it, vi } from "vitest";
import { api } from "../../api/client";
import * as libraryStore from "../../state/libraryStore";
import type { LibraryGame } from "../../lib/library";
import { GameLibrary } from "./GameLibrary";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const game: LibraryGame = { key: "fixture", titleId: "PPSA00001", fallbackName: "Fixture game", kind: "zip",
  sources: [], saveDirs: 1, totalBytes: 10, icon: null };

function setup() {
  const state: libraryStore.LibraryState = { status: "empty", folderName: "Saves", games: [], icons: {}, skipped: 0, error: null, persistent: false };
  const snapshot = vi.spyOn(libraryStore, "useLibrary").mockReturnValue(state);
  vi.spyOn(api, "titles").mockResolvedValue([]);
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } });
  const view = <MemoryRouter><QueryClientProvider client={client}><GameLibrary onChooseFolder={vi.fn()} /></QueryClientProvider></MemoryRouter>;
  const { rerender } = render(view);
  return { client, update: (next: Partial<libraryStore.LibraryState>) => {
    snapshot.mockReturnValue({ ...state, ...next });
    rerender(<MemoryRouter><QueryClientProvider client={client}><GameLibrary onChooseFolder={vi.fn()} /></QueryClientProvider></MemoryRouter>);
  } };
}

it("keeps a live scan status mounted before scanning and announces completion and errors", () => {
  const { update, client } = setup();
  const status = screen.getByRole("status", { name: "Folder scan status" });
  expect(status.textContent).toBe("");
  update({ status: "scanning" });
  expect(screen.getByRole("status", { name: "Folder scan status" })).toBe(status);
  expect(status.textContent).toContain("Looking for save dumps");
  update({ status: "ready", games: [game] });
  expect(status.textContent).toContain("1 game");
  update({ status: "error", error: "Folder unavailable" });
  expect(screen.getByRole("alert").textContent).toBe("Folder unavailable");
  client.clear();
});

it("names and announces game import progress, then reports failures", async () => {
  const { update, client } = setup();
  update({ status: "ready", games: [game] });
  vi.spyOn(libraryStore.library, "importedWorkspace").mockReturnValue(undefined);
  let fail!: (error: Error) => void;
  vi.spyOn(api, "upload").mockImplementation((_files, _name, progress) => {
    progress?.(0.5);
    return new Promise((_resolve, reject) => { fail = reject; });
  });
  fireEvent.click(screen.getByRole("button", { name: /Fixture game/ }));
  expect(screen.getByRole("progressbar", { name: "Importing Fixture game" }).getAttribute("aria-valuenow")).toBe("50");
  expect(screen.getByRole("status", { name: "Fixture game import status" }).textContent).toContain("50 %");
  await act(async () => { fail(new Error("Unavailable")); });
  expect(screen.getByRole("alert").textContent).toBe("The import failed.");
  client.clear();
});

it("imports a game again after its remembered workspace disappears", async () => {
  const { update, client } = setup();
  update({ status: "ready", games: [game] });
  vi.spyOn(libraryStore.library, "importedWorkspace").mockReturnValue("gone");
  vi.spyOn(api, "workspace").mockRejectedValue(new Error("Gone"));
  const upload = vi.spyOn(api, "upload").mockResolvedValue({ id: "fresh", name: "Dump", titles: [], documents: [], createdAt: "2026-10-29" });
  const remember = vi.spyOn(libraryStore.library, "rememberImport").mockImplementation(() => {});
  await act(async () => { fireEvent.click(screen.getByRole("button", { name: /Fixture game/ })); });
  expect(upload).toHaveBeenCalledWith(game.sources, "Fixture game", expect.any(Function));
  expect(remember).toHaveBeenCalledExactlyOnceWith("fixture", "fresh");
  client.clear();
});
