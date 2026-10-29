import { defaultScheduler, notifyManager, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { ExportDialog } from "./ExportDialog";

const clients: QueryClient[] = [];
const faults = [400, 404, 409, 422, 500, 503, "html502", "network"] as const;
type Fault = typeof faults[number];
beforeEach(() => { notifyManager.setScheduler(queueMicrotask); });
afterEach(() => {
  cleanup(); clients.splice(0).forEach(client => client.clear());
  vi.restoreAllMocks(); vi.unstubAllGlobals(); notifyManager.setScheduler(defaultScheduler);
});
const plan = { changedCount: 1, files: [{ relativePath: "save.json", length: 12, reason: "edited" }] };
const json = (body: unknown) => new Response(JSON.stringify(body), { headers: { "content-type": "application/json" } });
function failure(fault: Fault) {
  if (fault === "network") throw new TypeError("Failed to fetch");
  if (fault === "html502") return new Response("<html>Proxy unavailable</html>", { status: 502, headers: { "content-type": "text/html" } });
  return new Response(JSON.stringify({ title: "Export unavailable", detail: "Try the export again." }), {
    status: fault, headers: { "content-type": "application/problem+json" },
  });
}
const message = (fault: Fault) => fault === "network" ? /server.*try again/i : fault === "html502" ? /502.*try again/i : /Export unavailable: Try the export again/;

function mount() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } });
  clients.push(client);
  const close = vi.fn(); const toast = vi.fn();
  const click = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});
  const create = vi.fn(() => "blob:export"); const revoke = vi.fn();
  vi.stubGlobal("URL", class extends URL { static createObjectURL = create; static revokeObjectURL = revoke; });
  render(<QueryClientProvider client={client}><ExportDialog workspaceId="w" open
    ref={element => { if (element) element.open = true; }} onClose={close} onToast={toast} /></QueryClientProvider>);
  return { close, toast, click, create, revoke };
}

it.each(faults)("blocks packing after export-plan %s until an explicit retry succeeds", async fault => {
  let failed = false;
  const fetch = vi.fn<typeof globalThis.fetch>(async () => {
    if (!failed) { failed = true; return failure(fault); }
    return json(plan);
  });
  vi.stubGlobal("fetch", fetch);
  const { close, click } = mount();
  expect((await screen.findByRole("alert")).textContent).toMatch(message(fault));
  const download = screen.getByRole("button", { name: "Download zip" });
  expect(download).toHaveProperty("disabled", true);
  expect(fetch).toHaveBeenCalledOnce();
  const retry = screen.getByRole("button", { name: "Retry export plan" });
  retry.focus();
  fireEvent.click(retry);
  await screen.findByText("save.json");
  expect(download).toHaveProperty("disabled", false);
  expect(screen.queryByRole("alert")).toBeNull();
  expect(document.activeElement).toBe(screen.getByRole("heading", { name: "Export edited saves" }));
  expect(fetch).toHaveBeenCalledTimes(2);
  const bodies = fetch.mock.calls.map(([, init]) => init?.body);
  expect(bodies[0]).toBe(bodies[1]);
  expect(close).not.toHaveBeenCalled(); expect(click).not.toHaveBeenCalled();
});

it.each(faults)("keeps export choices and avoids phantom downloads after packing %s", async fault => {
  let attempts = 0;
  const fetch = vi.fn(async (url: string) => {
    if (url.endsWith("/plan")) return json(plan);
    if (++attempts === 1) return failure(fault);
    return new Response(new Blob(["synthetic archive"]), { headers: { "content-disposition": 'attachment; filename="retry.zip"' } });
  });
  vi.stubGlobal("fetch", fetch);
  const { close, click, create, revoke } = mount();
  await screen.findByText("save.json");
  fireEvent.click(screen.getByRole("radio", { name: /Whole dump/ }));
  fireEvent.click(screen.getByRole("checkbox", { name: /Also update/ }));
  const download = screen.getByRole("button", { name: "Download zip" });
  await waitFor(() => expect(download).toHaveProperty("disabled", false));
  download.focus();
  fireEvent.click(download);
  expect((await screen.findByRole("alert")).textContent).toMatch(message(fault));
  expect(download).toHaveProperty("disabled", false);
  expect(document.activeElement).toBe(download);
  expect(screen.getByRole("radio", { name: /Whole dump/ })).toHaveProperty("checked", true);
  expect(screen.getByRole("checkbox", { name: /Also update/ })).toHaveProperty("checked", false);
  expect(attempts).toBe(1); expect(close).not.toHaveBeenCalled(); expect(create).not.toHaveBeenCalled();
  expect(click).not.toHaveBeenCalled();
  await act(async () => { fireEvent.click(download); });
  expect(attempts).toBe(2); expect(click).toHaveBeenCalledOnce(); expect(close).toHaveBeenCalledOnce();
  expect(revoke).toHaveBeenCalledWith("blob:export");
  const packing = fetch.mock.calls.filter(([url]) => url.endsWith("/export"));
  expect(packing).toHaveLength(2);
  for (const [url] of packing) expect(url).toBe("/api/workspaces/w/export");
  expect(screen.queryByRole("alert")).toBeNull();
});
