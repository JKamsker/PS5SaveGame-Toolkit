import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import { afterEach, expect, it, vi } from "vitest";
import { api, ApiError } from "../../api/client";
import { library } from "../../state/libraryStore";
import { HomePage } from "./HomePage";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });

it("focuses the import heading on entry and provides a main landmark", () => {
  vi.spyOn(api, "plugins").mockResolvedValue([]);
  vi.spyOn(api, "workspaces").mockResolvedValue([]);
  vi.spyOn(api, "titles").mockResolvedValue([]);
  vi.spyOn(library, "restore").mockResolvedValue(undefined);
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } });
  render(<MemoryRouter><QueryClientProvider client={client}><HomePage /></QueryClientProvider></MemoryRouter>);
  expect(screen.getByRole("main")).toBeTruthy();
  expect(document.activeElement).toBe(screen.getByRole("heading", { name: "Open a PS5 save dump" }));
  client.clear();
});

function mount() {
  vi.spyOn(library, "restore").mockResolvedValue(undefined);
  vi.spyOn(api, "titles").mockResolvedValue([]);
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } });
  render(<MemoryRouter><QueryClientProvider client={client}><Routes>
    <Route path="/" element={<HomePage />} /><Route path="/w/:workspaceId" element={<h1>Opened dump</h1>} />
  </Routes></QueryClientProvider></MemoryRouter>);
  return client;
}

it.each(["plugins", "workspaces"] as const)("shows a failed %s read with an independent retry", async failed => {
  const plugins = vi.spyOn(api, "plugins").mockResolvedValue([]);
  const workspaces = vi.spyOn(api, "workspaces").mockResolvedValue([{ id: "w", name: "Existing dump", titleIds: [], directoryCount: 1, fileCount: 1, createdAt: "2026-10-29" }]);
  if (failed === "plugins") plugins.mockRejectedValueOnce(new ApiError("Plugins unavailable", 503));
  else workspaces.mockRejectedValueOnce(new ApiError("Dumps unavailable", 503));
  const client = mount();
  expect((await screen.findByRole("alert")).textContent).toContain("unavailable");
  if (failed === "plugins") expect(await screen.findByRole("link", { name: /Existing dump/ })).toBeTruthy();
  else expect(await screen.findByRole("heading", { name: "Supported games" })).toBeTruthy();
  fireEvent.click(screen.getByRole("button", { name: failed === "plugins" ? "Retry supported games" : "Retry open dumps" }));
  await screen.findByRole("link", { name: /Existing dump/ });
  await screen.findByRole("heading", { name: "Supported games" });
  expect(screen.queryByRole("alert")).toBeNull();
  expect(plugins).toHaveBeenCalledTimes(failed === "plugins" ? 2 : 1);
  expect(workspaces).toHaveBeenCalledTimes(failed === "workspaces" ? 2 : 1);
  client.clear();
});

it("retains failed import files for retry and resets the chooser so the same file can be selected again", async () => {
  vi.spyOn(api, "plugins").mockResolvedValue([]);
  vi.spyOn(api, "workspaces").mockResolvedValue([]);
  const upload = vi.spyOn(api, "upload").mockRejectedValueOnce(new ApiError("Import unavailable", 503))
    .mockResolvedValue({ id: "w", name: "Dump", titles: [], documents: [], createdAt: "2026-10-29" });
  const client = mount();
  const input = document.querySelector<HTMLInputElement>('input[accept=".zip"]')!;
  const reset = vi.spyOn(input, "value", "set");
  const file = new File(["invented fixture"], "save.zip");
  await act(async () => { fireEvent.change(input, { target: { files: [file] } }); });
  expect(screen.getByRole("alert").textContent).toBe("Import unavailable");
  expect(reset).toHaveBeenCalledWith("");
  fireEvent.click(screen.getByRole("button", { name: "Retry import" }));
  await screen.findByRole("heading", { name: "Opened dump" });
  expect(upload).toHaveBeenCalledTimes(2);
  expect(upload.mock.calls.map(call => call[0])).toEqual([[{ file, path: "save.zip" }], [{ file, path: "save.zip" }]]);
  client.clear();
});
