import type { ReactNode } from "react";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { api } from "../../api/client";
import { WorkspaceActivity, WorkspaceActivityContext } from "../../state/workspaceActivity";
import { deferred } from "../../test/deferred";
import { DownloadFileButton } from "./DownloadFileButton";

vi.mock("../../lib/saveBlob", () => ({ saveBlob: vi.fn() }));

afterEach(() => { cleanup(); vi.restoreAllMocks(); });

it("keeps keyboard focus on the button while the download is prepared and ignores repeated activation", async () => {
  const pending = deferred<{ blob: Blob; name: string | undefined }>();
  const download = vi.spyOn(api, "download").mockReturnValue(pending.promise);
  render(<DownloadFileButton workspaceId="w" documentId="d" fallbackName="SAVEDATA00/data.bin" />);
  const button = screen.getByRole("button", { name: "Download file" });
  button.focus();
  fireEvent.click(button);
  fireEvent.click(button);
  expect(button.textContent).toBe("Preparing download…");
  expect(button.getAttribute("aria-disabled")).toBe("true");
  expect(button).toHaveProperty("disabled", false);
  expect(document.activeElement).toBe(button);
  fireEvent.click(button);
  expect(download).toHaveBeenCalledOnce();

  await act(async () => { pending.resolve({ blob: new Blob(["x"]), name: "data.bin" }); });
  expect(button.textContent).toBe("Download file");
  expect(button.hasAttribute("aria-disabled")).toBe(false);
  expect(document.activeElement).toBe(button);
  const { saveBlob } = await import("../../lib/saveBlob");
  expect(saveBlob).toHaveBeenCalledWith(expect.any(Blob), "data.bin");
});

function renderWithActivity(activity: WorkspaceActivity, extra?: ReactNode) {
  return render(<WorkspaceActivityContext.Provider value={activity}>
    {extra}
    <DownloadFileButton workspaceId="w" documentId="d" fallbackName="SAVEDATA00/data.bin" />
  </WorkspaceActivityContext.Provider>);
}

it("waits for a field edit queued just before the click, so the downloaded bytes include it", async () => {
  const activity = new WorkspaceActivity();
  const edit = deferred<void>();
  // What useDocumentSession does for a blur commit: the edit is tracked before its request is sent.
  void activity.track(edit.promise);
  const download = vi.spyOn(api, "download").mockResolvedValue({ blob: new Blob(["x"]), name: "data.bin" });
  renderWithActivity(activity);
  fireEvent.click(screen.getByRole("button", { name: "Download file" }));
  await act(async () => { await Promise.resolve(); });
  expect(download).not.toHaveBeenCalled();
  await act(async () => { edit.resolve(); });
  expect(download).toHaveBeenCalledOnce();
});

it("commits a still-focused field draft before waiting for pending edits", async () => {
  const activity = new WorkspaceActivity();
  const edit = deferred<void>();
  const download = vi.spyOn(api, "download").mockResolvedValue({ blob: new Blob(["x"]), name: "data.bin" });
  // A field control commits its draft on blur (TextualControl, EnumControl).
  renderWithActivity(activity, <input aria-label="Gold" onBlur={() => void activity.track(edit.promise)} />);
  const field = screen.getByRole("textbox", { name: "Gold" });
  field.focus();
  // An activation that does not move focus first (programmatic or assistive technology).
  fireEvent.click(screen.getByRole("button", { name: "Download file" }));
  expect(document.activeElement).not.toBe(field);
  await act(async () => { await Promise.resolve(); });
  expect(download).not.toHaveBeenCalled();
  await act(async () => { edit.resolve(); });
  expect(download).toHaveBeenCalledOnce();
});
