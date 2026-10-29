import { defaultScheduler, notifyManager, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { Link, MemoryRouter, Route, Routes } from "react-router";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { api } from "../../api/client";
import { library } from "../../state/libraryStore";
import { HomePage } from "./HomePage";

const clients: QueryClient[] = [];
beforeEach(() => { notifyManager.setScheduler(queueMicrotask); });
afterEach(() => {
  cleanup(); clients.splice(0).forEach(client => client.clear());
  vi.restoreAllMocks(); vi.unstubAllGlobals(); notifyManager.setScheduler(defaultScheduler);
});

// Exercise the upload adapter's XHR response handling, including HTML parsed as null.
class UploadRequest {
  open = vi.fn(); send = vi.fn<(body: FormData) => void>(); upload = {}; responseType = ""; timeout = 0;
  status = 0; response: unknown = null;
  onload: (() => void) | null = null;
  onerror: (() => void) | null = null;
  onabort: (() => void) | null = null;
  ontimeout: (() => void) | null = null;
}

function mount() {
  vi.spyOn(api, "plugins").mockResolvedValue([]);
  vi.spyOn(api, "workspaces").mockResolvedValue([]);
  vi.spyOn(api, "titles").mockResolvedValue([]);
  vi.spyOn(library, "restore").mockResolvedValue(undefined);
  const uploads: UploadRequest[] = [];
  vi.stubGlobal("XMLHttpRequest", class extends UploadRequest {
    constructor() { super(); uploads.push(this); }
  });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } });
  clients.push(client);
  render(<MemoryRouter><QueryClientProvider client={client}><Routes>
    <Route path="/" element={<><Link to="/away">Leave</Link><HomePage /></>} />
    <Route path="/away" element={<h1>Away</h1>} />
    <Route path="/w/:workspaceId" element={<h1>Recovered import</h1>} />
  </Routes></QueryClientProvider></MemoryRouter>);
  const file = new File(["synthetic save"], "save.zip");
  const choose = () => fireEvent.change(document.querySelector('input[accept=".zip"]')!, { target: { files: [file] } });
  return { uploads, choose, file };
}

const faults = [400, 404, 409, 422, 500, 503, "html502", "network", "abort", "timeout"] as const;
it.each(faults)("retains the original upload after %s and explicitly retries once", async fault => {
  const { uploads, choose, file } = mount();
  const opener = screen.getByRole("button", { name: "Open zip" });
  opener.focus();
  await act(async () => { choose(); });
  expect(uploads).toHaveLength(1);
  expect(opener).toHaveProperty("disabled", true);
  const failed = uploads[0]!;
  await act(async () => {
    if (fault === "network") failed.onerror!();
    else if (fault === "abort") failed.onabort!();
    else if (fault === "timeout") failed.ontimeout!();
    else {
      failed.status = fault === "html502" ? 502 : fault;
      failed.response = fault === "html502" ? null : { error: "Import refused. Try the same ZIP again." };
      failed.onload!();
    }
  });
  expect(screen.getByRole("alert").textContent).toMatch(fault === "html502" ? /502.*try again/i :
    typeof fault === "number" ? /Import refused/ : fault === "network" ? /server.*try again/i : /current state.*retry/i);
  expect(opener).toHaveProperty("disabled", false);
  expect(document.activeElement).toBe(opener);
  expect(screen.queryByRole("heading", { name: "Recovered import" })).toBeNull();
  await act(async () => { fireEvent.click(screen.getByRole("button", { name: "Retry import" })); });
  expect(uploads).toHaveLength(2);
  const sent = uploads[1]!.send.mock.calls[0]![0];
  expect(sent.getAll("files")).toHaveLength(1);
  const retryFile = sent.get("files") as File;
  expect(retryFile.name).toBe(file.name);
  expect(retryFile.size).toBe(file.size);
  const read = (blob: Blob) => new Promise<string>((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result)); reader.onerror = reject;
    reader.readAsText(blob);
  });
  expect(await read(retryFile)).toBe(await read(file));
  await act(async () => {
    uploads[1]!.status = 201;
    uploads[1]!.response = { id: "recovered" };
    uploads[1]!.onload!();
  });
  expect(screen.getByRole("heading", { name: "Recovered import" })).toBeTruthy();
  expect(screen.queryByRole("alert")).toBeNull();
});

it.each(["error", "abort", "timeout"] as const)("ignores upload %s after navigating away", async event => {
  const { choose, uploads } = mount();
  await act(async () => { choose(); });
  fireEvent.click(screen.getByRole("link", { name: "Leave" }));
  await act(async () => {
    if (event === "error") uploads[0]!.onerror!();
    else if (event === "abort") uploads[0]!.onabort!();
    else uploads[0]!.ontimeout!();
  });
  expect(screen.getByRole("heading", { name: "Away" })).toBeTruthy();
  expect(screen.queryByRole("alert")).toBeNull();
});

it.each([false, true])("restores lost import focus while respecting an intentional focus change (%s)", async movedFocus => {
  const { choose, uploads } = mount();
  const opener = screen.getByRole("button", { name: "Open zip" });
  opener.focus();
  await act(async () => { choose(); });
  // Native Chromium blurs a control when it becomes disabled; jsdom needs this step.
  const leave = screen.getByRole("link", { name: "Leave" });
  leave.focus();
  if (!movedFocus) leave.blur();
  await act(async () => { uploads[0]!.onerror!(); });
  expect(document.activeElement).toBe(movedFocus ? leave : screen.getByRole("button", { name: "Retry import" }));
});
