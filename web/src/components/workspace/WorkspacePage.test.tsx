import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import { api, ApiError } from "../../api/client";
import { WorkspacePage } from "./WorkspacePage";
import type { DocumentSummary } from "../../api/protocol";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });

async function setup(documents: DocumentSummary[] = []) {
  vi.spyOn(api, "workspace").mockResolvedValue({ id: "w", name: "Test dump", createdAt: "2026-10-28", titles: [], documents });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } });
  render(<MemoryRouter initialEntries={["/w/w"]}><QueryClientProvider client={client}>
    <Routes><Route path="/w/:workspaceId" element={<WorkspacePage />} /><Route path="/" element={<h1>Import</h1>} /></Routes>
  </QueryClientProvider></MemoryRouter>);
  await screen.findByText("Test dump");
  return client;
}

describe("closing a dump", () => {
  it("keeps edited sessions when the discard confirmation is cancelled", async () => {
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    const remove = vi.spyOn(api, "deleteWorkspace").mockResolvedValue(undefined);
    const client = await setup([{ id: "d", fileId: "f", filePath: "save.dat", title: "Save", pluginId: "test", isDirty: true }]);
    fireEvent.click(screen.getByRole("button", { name: "Close dump" }));
    expect(confirm).toHaveBeenCalledWith(expect.stringMatching(/discard.*edits/i));
    expect(remove).not.toHaveBeenCalled();
    expect(screen.getByText("Test dump")).toBeTruthy();
    client.clear();
  });

  it("closes an edited dump only after explicitly confirming discard", async () => {
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(true);
    const remove = vi.spyOn(api, "deleteWorkspace").mockResolvedValue(undefined);
    const client = await setup([{ id: "d", fileId: "f", filePath: "save.dat", title: "Save", pluginId: "test", isDirty: true }]);
    fireEvent.click(screen.getByRole("button", { name: "Close dump" }));
    expect(confirm).toHaveBeenCalledOnce();
    await screen.findByRole("heading", { name: "Import" });
    expect(remove).toHaveBeenCalledWith("w");
    client.clear();
  });

  it("reports failures and leaves the workspace available for retry", async () => {
    const confirm = vi.spyOn(window, "confirm");
    vi.spyOn(api, "deleteWorkspace").mockRejectedValueOnce(new ApiError("Dump is busy", 409)).mockResolvedValueOnce(undefined);
    const client = await setup();
    fireEvent.click(screen.getByRole("button", { name: "Close dump" }));
    expect(await screen.findByRole("alert")).toHaveProperty("textContent", "Dump is busy");
    expect(screen.getByText("Test dump")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Close dump" }));
    await screen.findByRole("heading", { name: "Import" });
    expect(api.deleteWorkspace).toHaveBeenCalledTimes(2);
    expect(confirm).not.toHaveBeenCalled();
    client.clear();
  });

  it("disables duplicate submissions and navigates only after a successful close", async () => {
    let finish!: () => void;
    vi.spyOn(api, "deleteWorkspace").mockImplementation(() => new Promise(resolve => { finish = resolve; }));
    const client = await setup();
    fireEvent.click(screen.getByRole("button", { name: "Close dump" }));
    const button = screen.getByRole("button", { name: "Closing…" }) as HTMLButtonElement;
    expect(button.disabled).toBe(true);
    fireEvent.click(button);
    expect(api.deleteWorkspace).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole("heading", { name: "Import" })).toBeNull();
    finish();
    await waitFor(() => expect(screen.getByRole("heading", { name: "Import" })).toBeTruthy());
    client.clear();
  });
});
