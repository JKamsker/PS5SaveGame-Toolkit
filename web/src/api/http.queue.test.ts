import { afterEach, expect, it, vi } from "vitest";
import { runtime } from "../runtime";
import { RuntimeRequestError, type RequestEvents } from "../runtime/client";
import { fetchApi, outOfMemoryMessage, QUEUE_WAIT_LIMIT_MS, readJson } from "./http";

afterEach(() => { vi.restoreAllMocks(); vi.useRealTimers(); });

/** A runtime whose worker starts the request only when the test says so. */
function queuedRuntime() {
  let start: (() => void) | undefined;
  let signal: AbortSignal | undefined;
  let answer: ((response: Response) => void) | undefined;
  vi.spyOn(runtime, "fetch").mockImplementation((_url: string, init?: RequestInit, events?: RequestEvents) => {
    start = () => events?.onStart?.();
    signal = init?.signal ?? undefined;
    return new Promise<Response>((resolve, reject) => {
      answer = resolve;
      signal?.addEventListener("abort", () => reject(signal!.reason));
    });
  });
  return {
    start: () => start!(),
    answer: (body: unknown) => answer!(new Response(JSON.stringify(body), { headers: { "content-type": "application/json" } })),
    aborted: () => signal?.aborted ?? false,
  };
}

it("does not count the time a request waits behind earlier ones against its deadline", async () => {
  vi.useFakeTimers();
  const queue = queuedRuntime();
  const outcome = fetchApi("/presets", undefined, readJson).catch((error: unknown) => error);
  await vi.advanceTimersByTimeAsync(4 * 60_000); // an import ahead of it runs for four minutes
  expect(queue.aborted()).toBe(false);
  queue.start();
  await vi.advanceTimersByTimeAsync(59_000);
  queue.answer([1]);
  expect(await outcome).toEqual([1]);
  expect(vi.getTimerCount()).toBe(0);
});

it("times out a started request after its own deadline", async () => {
  vi.useFakeTimers();
  const queue = queuedRuntime();
  const outcome = fetchApi("/presets", undefined, readJson).catch((error: unknown) => error);
  queue.start();
  await vi.advanceTimersByTimeAsync(60_000);
  expect(await outcome).toMatchObject({ status: 0, message: expect.stringMatching(/timed out/i) });
  expect(queue.aborted()).toBe(true);
});

it("still bounds a request that never leaves the queue, and cancels it before it starts", async () => {
  vi.useFakeTimers();
  queuedRuntime();
  const stuck = fetchApi("/presets", undefined, readJson).catch((error: unknown) => error);
  await vi.advanceTimersByTimeAsync(QUEUE_WAIT_LIMIT_MS);
  expect(await stuck).toMatchObject({ message: expect.stringMatching(/timed out/i) });

  const controller = new AbortController();
  const queue = queuedRuntime();
  const cancelled = fetchApi("/presets", { signal: controller.signal }, readJson).catch((error: unknown) => error);
  controller.abort();
  expect(await cancelled).toMatchObject({ message: expect.stringMatching(/cancelled/i) });
  expect(queue.aborted()).toBe(true);
});

it.each([
  [new RuntimeRequestError("System.OutOfMemoryException", true), outOfMemoryMessage],
  [new RuntimeRequestError("interop failure", false), expect.stringMatching(/runtime failed while handling the request \(interop failure\)/)],
])("explains runtime failures honestly: %s", async (error, message) => {
  vi.spyOn(runtime, "fetch").mockRejectedValue(error);
  await expect(fetchApi("/workspaces", { method: "POST" }, readJson)).rejects.toMatchObject({ status: 0, message });
});
