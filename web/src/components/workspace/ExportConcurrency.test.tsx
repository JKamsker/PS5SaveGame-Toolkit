import { defaultScheduler, notifyManager, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { api, ApiError } from "../../api/client";
import { deferred } from "../../test/deferred";
import { ExportDialog } from "./ExportDialog";
import { WorkspaceActivity, WorkspaceActivityContext } from "../../state/workspaceActivity";

const clients: QueryClient[] = [];
beforeEach(() => { notifyManager.setScheduler(queueMicrotask); });
afterEach(() => { cleanup(); clients.splice(0).forEach(client => client.clear()); vi.restoreAllMocks(); notifyManager.setScheduler(defaultScheduler); });
async function mount(activity: WorkspaceActivity | null = null) {
  vi.spyOn(api, "exportPlan").mockResolvedValue({ changedCount: 1, files: [] });
  const click = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});
  Object.defineProperty(URL, "createObjectURL", { configurable: true, value: vi.fn(() => "blob:fixture") });
  Object.defineProperty(URL, "revokeObjectURL", { configurable: true, value: vi.fn() });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } }); clients.push(client);
  const close = vi.fn(); const toast = vi.fn();
  const view = (open: boolean) => <QueryClientProvider client={client}><WorkspaceActivityContext.Provider value={activity}><ExportDialog workspaceId="w" open={open} ref={element => { if (element) element.open = open; }} onClose={close} onToast={toast} /></WorkspaceActivityContext.Provider></QueryClientProvider>;
  let rendered!: ReturnType<typeof render>;
  await act(async () => { rendered = render(view(true)); });
  return { ...rendered, close, toast, click, show: async (open: boolean) => { await act(async () => { rendered.rerender(view(open)); }); } };
}

it("submits packing once before React commits busy state", async () => {
  const pending = deferred<Awaited<ReturnType<typeof api.exportZip>>>();
  const pack = vi.spyOn(api, "exportZip").mockReturnValue(pending.promise);
  const { click, close } = await mount();
  act(() => { const button = screen.getByRole("button", { name: "Download zip" }); fireEvent.click(button); fireEvent.click(button); });
  expect(pack).toHaveBeenCalledOnce();
  await act(async () => { pending.resolve({ blob: new Blob(), name: "save.zip" }); await pending.promise; });
  expect(click).toHaveBeenCalledOnce(); expect(close).toHaveBeenCalledOnce();
});

it("waits for pending workspace edits before reading the export plan", async () => {
  const pending = deferred<void>();
  const activity = new WorkspaceActivity(); activity.track(pending.promise);
  await mount(activity);
  expect(api.exportPlan).not.toHaveBeenCalled();
  expect(screen.getByRole("button", { name: "Download zip" })).toHaveProperty("disabled", true);
  await act(async () => { pending.resolve(); await pending.promise; });
  expect(api.exportPlan).toHaveBeenCalledOnce();
});

it("waits for pending edits before packing and retires a cancelled waiter", async () => {
  const activity = new WorkspaceActivity();
  const { show, click } = await mount(activity);
  const pending = deferred<void>(); activity.track(pending.promise);
  const pack = vi.spyOn(api, "exportZip").mockResolvedValue({ blob: new Blob(), name: "save.zip" });
  fireEvent.click(screen.getByRole("button", { name: "Download zip" }));
  expect(pack).not.toHaveBeenCalled();
  await show(false);
  await act(async () => { pending.resolve(); await pending.promise; });
  expect(pack).not.toHaveBeenCalled(); expect(click).not.toHaveBeenCalled();
});

it.each(["resolve", "reject"] as const)("ignores old packing %s after cancelling and reopening the dialog", async outcome => {
  const pending = deferred<Awaited<ReturnType<typeof api.exportZip>>>();
  vi.spyOn(api, "exportZip").mockReturnValue(pending.promise);
  const { show, click, close, toast } = await mount();
  fireEvent.click(screen.getByRole("button", { name: "Download zip" }));
  await show(false); await show(true);
  await act(async () => { if (outcome === "resolve") pending.resolve({ blob: new Blob(), name: "old.zip" }); else pending.reject(new ApiError("Old failure", 503)); await pending.promise.catch(() => {}); });
  expect(click).not.toHaveBeenCalled(); expect(close).not.toHaveBeenCalled(); expect(toast).not.toHaveBeenCalled();
  expect(screen.queryByRole("alert")).toBeNull();
});
