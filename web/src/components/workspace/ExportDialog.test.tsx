import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { api, ApiError } from "../../api/client";
import type { ExportPlan } from "../../api/protocol";
import { ExportDialog } from "./ExportDialog";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });

const empty: ExportPlan = { changedCount: 0, files: [] };
const changed: ExportPlan = { changedCount: 1, files: [{ relativePath: "save.dat", length: 1, reason: "edited" }] };

function setup() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 30_000, gcTime: 0 } } });
  const toast = vi.fn();
  const view = (open: boolean) => <QueryClientProvider client={client}>
    <ExportDialog ref={element => { if (element) element.open = open; }} workspaceId="w" open={open} onClose={vi.fn()} onToast={toast} />
  </QueryClientProvider>;
  const { rerender } = render(view(true));
  return { client, toast, show: (open: boolean) => rerender(view(open)) };
}

describe("export plan freshness", () => {
  it("retries a failed export plan without changing scope or mirror settings", async () => {
    vi.spyOn(api, "exportPlan").mockRejectedValueOnce(new ApiError("Plan unavailable", 503)).mockResolvedValue(changed);
    const { client } = setup();
    await screen.findByRole("alert");
    const download = screen.getByRole<HTMLButtonElement>("button", { name: "Download zip" });
    expect(download.disabled).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: "Retry export plan" }));
    await screen.findByText("save.dat");
    expect(download.disabled).toBe(false);
    expect(screen.queryByRole("alert")).toBeNull();
    expect(vi.mocked(api.exportPlan).mock.calls).toEqual([["w", "ChangedDirectories", true], ["w", "ChangedDirectories", true]]);
    client.clear();
  });

  it("announces packing and download failures inside the modal and allows retry", async () => {
    vi.spyOn(api, "exportPlan").mockResolvedValue(changed);
    let fail!: (error: Error) => void;
    vi.spyOn(api, "exportZip").mockImplementation(() => new Promise((_resolve, reject) => { fail = reject; }));
    const { client, toast } = setup();
    const dialog = within(screen.getByRole("dialog", { name: "Export edited saves" }));
    await dialog.findByText("save.dat");
    fireEvent.click(dialog.getByRole("button", { name: "Download zip" }));
    const packingText = dialog.queryByRole("status")?.textContent;
    await act(async () => { fail(new ApiError("Save failed integrity validation", 422)); });
    expect(packingText).toBe("Packing edited saves…");
    expect(dialog.getByRole("alert").textContent).toBe("Save failed integrity validation");
    expect(toast).toHaveBeenCalledWith("Save failed integrity validation", true);
    expect((dialog.getByRole("button", { name: "Download zip" }) as HTMLButtonElement).disabled).toBe(false);
    client.clear();
  });

  it("reloads the plan on each opening and disables download until the current plan arrives", async () => {
    let finish!: (value: ExportPlan) => void;
    vi.spyOn(api, "exportPlan").mockResolvedValueOnce(empty)
      .mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
    const { show, client } = setup();
    await screen.findByText("Nothing has been changed yet.");
    show(false);
    show(true);
    await waitFor(() => expect(api.exportPlan).toHaveBeenCalledTimes(2));
    expect((screen.getByRole("button", { name: "Download zip" }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.queryByText("Nothing has been changed yet.")).toBeNull();
    await act(async () => { finish(changed); });
    await waitFor(() => expect((screen.getByRole("button", { name: "Download zip" }) as HTMLButtonElement).disabled).toBe(false));
    client.clear();
  });

  it("does not download using a cached plan when the new plan fails", async () => {
    vi.spyOn(api, "exportPlan").mockResolvedValueOnce(changed).mockRejectedValueOnce(new ApiError("Plan unavailable", 503));
    const { show, client } = setup();
    await screen.findByText("save.dat");
    show(false);
    show(true);
    expect(await screen.findByRole("alert")).toHaveProperty("textContent", "Plan unavailable");
    expect((screen.getByRole("button", { name: "Download zip" }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.queryByText("save.dat")).toBeNull();
    client.clear();
  });
});

it("preserves scope and mirror choices across openings and permits an unchanged whole dump", async () => {
  vi.spyOn(api, "exportPlan").mockResolvedValue(empty);
  const { client, show } = setup();
  await screen.findByText("Nothing has been changed yet.");
  const download = screen.getByRole<HTMLButtonElement>("button", { name: "Download zip" });
  expect(download.disabled).toBe(true);
  fireEvent.click(screen.getByRole("radio", { name: /Whole dump/ }));
  fireEvent.click(screen.getByRole("checkbox", { name: /Also update/ }));
  await waitFor(() => expect(download.disabled).toBe(false));
  show(false);
  show(true);
  await waitFor(() => expect(vi.mocked(api.exportPlan).mock.lastCall).toEqual(["w", "Everything", false]));
  expect(screen.getByRole<HTMLInputElement>("radio", { name: /Whole dump/ }).checked).toBe(true);
  expect(screen.getByRole<HTMLInputElement>("checkbox", { name: /Also update/ }).checked).toBe(false);
  client.clear();
});
