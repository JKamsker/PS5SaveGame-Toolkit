import { afterEach, expect, it, vi } from "vitest";
import { runtime, type RuntimeState } from "../runtime";
import { RuntimeRequestError } from "../runtime/client";
import {
  cancelledMessage, fetchApi, readJson, RUNTIME_STALL_LIMIT_MS, runtimeCrashedMessage, runtimeCrashedOutOfMemoryMessage,
  runtimeFailedMessage, runtimeStalledMessage,
} from "./http";

afterEach(() => { vi.restoreAllMocks(); vi.useRealTimers(); });

const failed = (crashed: boolean): RuntimeState => crashed ? { status: "failed", message: "gone", crashed } : { status: "failed", message: "gone" };

/** A runtime still booting: `ready` settles only through its signal; `progress()` notifies subscribers. */
function bootingRuntime() {
  const listeners = new Set<() => void>();
  let readySignal: AbortSignal | null | undefined;
  vi.spyOn(runtime, "state").mockReturnValue({ status: "loading", loaded: 1, total: 10 });
  vi.spyOn(runtime, "subscribe").mockImplementation(listener => { listeners.add(listener); return () => listeners.delete(listener); });
  vi.spyOn(runtime, "ready").mockImplementation(signal => new Promise<void>((_resolve, reject) => {
    readySignal = signal;
    signal?.addEventListener("abort", () => reject(new DOMException("aborted", "AbortError")));
  }));
  return { progress: () => listeners.forEach(listener => listener()), listeners, readySignal: () => readySignal };
}

it.each([[false, runtimeFailedMessage], [true, runtimeCrashedMessage]])(
  "says whether the runtime never booted or stopped after booting (crashed: %s)", async (crashed, message) => {
    vi.spyOn(runtime, "state").mockReturnValue(failed(crashed));
    vi.spyOn(runtime, "ready").mockRejectedValue(new TypeError("gone"));
    await expect(fetchApi("/presets", undefined, readJson)).rejects.toMatchObject({ status: 0, message });
  });

it("reports requests lost in a crash as a crash, out of memory when that was the cause", async () => {
  const state = vi.spyOn(runtime, "state").mockReturnValue({ status: "ready", boot: {} as never });
  vi.spyOn(runtime, "fetch").mockImplementation(() => {
    state.mockReturnValue(failed(true));
    return Promise.reject(new TypeError("Script error."));
  });
  await expect(fetchApi("/presets", undefined, readJson)).rejects.toMatchObject({ message: runtimeCrashedMessage });
  vi.mocked(runtime.fetch).mockImplementation(() => {
    state.mockReturnValue(failed(true));
    return Promise.reject(new RuntimeRequestError("Aborted(OOM)", true));
  });
  await expect(fetchApi("/workspaces", { method: "POST" }, readJson)).rejects.toMatchObject({ message: runtimeCrashedOutOfMemoryMessage });
  // A request sent after the runtime stopped out of memory says so too.
  state.mockReturnValue({ status: "failed", message: "Aborted(OOM)", crashed: true });
  vi.mocked(runtime.fetch).mockRejectedValue(new TypeError("Aborted(OOM)"));
  await expect(fetchApi("/presets", undefined, readJson)).rejects.toMatchObject({ message: runtimeCrashedOutOfMemoryMessage });
});

it("fails a request waiting for a boot that stopped making progress instead of waiting forever", async () => {
  vi.useFakeTimers();
  const boot = bootingRuntime();
  const fetch = vi.spyOn(runtime, "fetch");
  const outcome = fetchApi("/workspaces", { method: "POST" }, readJson).catch((error: unknown) => error);
  await vi.advanceTimersByTimeAsync(RUNTIME_STALL_LIMIT_MS - 1_000);
  boot.progress(); // a slow download that still advances keeps waiting
  await vi.advanceTimersByTimeAsync(RUNTIME_STALL_LIMIT_MS - 1_000);
  expect(boot.readySignal()?.aborted).toBe(false);
  await vi.advanceTimersByTimeAsync(1_000);
  expect(await outcome).toMatchObject({ status: 0, message: runtimeStalledMessage });
  expect(fetch).not.toHaveBeenCalled();
  expect(boot.listeners.size).toBe(0);
  expect(vi.getTimerCount()).toBe(0);
});

it("cancels a request waiting for the boot when its caller aborts", async () => {
  vi.useFakeTimers();
  const boot = bootingRuntime();
  const controller = new AbortController();
  const outcome = fetchApi("/presets", { signal: controller.signal }, readJson).catch((error: unknown) => error);
  await vi.advanceTimersByTimeAsync(10);
  controller.abort();
  expect(await outcome).toMatchObject({ message: cancelledMessage });
  expect(boot.listeners.size).toBe(0);
  expect(vi.getTimerCount()).toBe(0);
});
