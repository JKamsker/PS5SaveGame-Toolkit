import { describe, expect, it, vi } from "vitest";
import { RequestQueue } from "./RequestQueue";
import { deferred } from "../test/deferred";

describe("request queue", () => {
  it("starts waiters in order and releases slots after success, asynchronous failure, and synchronous failure", async () => {
    const queue = new RequestQueue(1);
    const pending = deferred<string>();
    const order: number[] = [];
    const signal = new AbortController().signal;
    const first = queue.run(() => { order.push(1); return pending.promise; }, signal);
    const second = queue.run(() => { order.push(2); throw new Error("Synchronous failure"); }, signal);
    const third = queue.run(async () => { order.push(3); return "done"; }, signal);
    const firstFailure = expect(first).rejects.toThrow("Asynchronous failure");
    const secondFailure = expect(second).rejects.toThrow("Synchronous failure");
    await Promise.resolve();
    expect(order).toEqual([1]);
    pending.reject(new Error("Asynchronous failure"));
    await Promise.all([firstFailure, secondFailure]);
    await expect(third).resolves.toBe("done");
    expect(order).toEqual([1, 2, 3]);
  });

  it("removes cancelled waiters without sending requests and keeps running slots until settled", async () => {
    const queue = new RequestQueue(1);
    const running = new AbortController();
    const waiting = new AbortController();
    const pending = deferred<string>();
    const first = queue.run(() => pending.promise, running.signal);
    const cancelledCall = vi.fn(async () => "cancelled");
    const second = queue.run(cancelledCall, waiting.signal);
    const nextCall = vi.fn(async () => "next");
    const third = queue.run(nextCall, new AbortController().signal);
    await Promise.resolve();
    waiting.abort();
    running.abort();
    await expect(second).rejects.toHaveProperty("name", "AbortError");
    expect(cancelledCall).not.toHaveBeenCalled();
    expect(nextCall).not.toHaveBeenCalled();
    pending.resolve("finished");
    await expect(first).resolves.toBe("finished");
    await expect(third).resolves.toBe("next");
  });

  it("does not dispatch requests aborted before enqueueing or in the same turn", async () => {
    const queue = new RequestQueue(1);
    const controller = new AbortController();
    const request = vi.fn(async () => "unused");
    const first = queue.run(request, controller.signal);
    controller.abort();
    await expect(first).rejects.toHaveProperty("name", "AbortError");
    await expect(queue.run(request, controller.signal)).rejects.toHaveProperty("name", "AbortError");
    expect(request).not.toHaveBeenCalled();
    await expect(queue.run(async () => "recovered", new AbortController().signal)).resolves.toBe("recovered");
  });
});
