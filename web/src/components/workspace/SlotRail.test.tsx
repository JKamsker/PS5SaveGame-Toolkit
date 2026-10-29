import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import type { SaveDirectory, SaveFileInfo, Workspace } from "../../api/protocol";
import { SlotRail } from "./SlotRail";

afterEach(cleanup);

const file: SaveFileInfo = { id: "f", path: "save.dat", length: 16, matches: [
  { pluginId: "game", pluginName: "Game", score: 2, label: "Chapter save", maturity: "Experimental" },
] };
const directory: SaveDirectory = { id: "slot", name: "Slot one", rawName: "sdimg_ONE", generation: 0,
  isBackup: false, twinIdentical: false, hasIcon: false, files: [file] };
const workspace = (directories = [directory]): Workspace => ({ id: "w", name: "Dump", createdAt: "2026-10-29",
  titles: [{ titleId: "PPSA00001", directories }], documents: [] });

it("uses plugin labels for empty slot metadata and opens the exact selected file", () => {
  const open = vi.fn();
  render(<SlotRail workspace={workspace()} activeFileId="f" onOpen={open} />);
  expect(screen.getByRole("button", { name: /Slot one/ }).getAttribute("aria-expanded")).toBe("true");
  expect(screen.getAllByText("Chapter save")).toHaveLength(2);
  const button = screen.getByRole("button", { name: /save.dat Chapter save/ });
  expect(button.classList.contains("file--open")).toBe(true);
  fireEvent.click(button);
  expect(open).toHaveBeenCalledExactlyOnceWith(file);
});

it("hides identical backup slots until requested and preserves slot metadata when toggled", () => {
  const backup = { ...directory, id: "backup", name: "Console backup", isBackup: true, twinIdentical: true,
    sfo: { subTitle: "Saved subtitle" }, files: [{ ...file, id: "backup-file", matches: [] }] };
  render(<SlotRail workspace={workspace([directory, backup])} activeFileId={undefined} onOpen={vi.fn()} />);
  expect(screen.queryByRole("button", { name: /Console backup/ })).toBeNull();
  fireEvent.click(screen.getByRole("checkbox", { name: "Show 1 identical console backups" }));
  const slot = screen.getByRole("button", { name: /Console backup/ });
  expect(slot.textContent).toContain("Saved subtitle");
  expect(slot.textContent).toContain("console backup, identical");
  fireEvent.click(slot);
  expect(screen.getByRole("button", { name: /save.dat No plugin/ })).toBeTruthy();
});
