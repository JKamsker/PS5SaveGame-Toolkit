import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { api } from "../../api/client";
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
