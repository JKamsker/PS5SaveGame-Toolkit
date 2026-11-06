// @vitest-environment node
// Randomized interleavings of slow bodies, aborts and a runtime crash against the runtime client's
// posting order. The seeds are fixed, so a failure names a seed that reproduces it.
import { getEventListeners } from "node:events";
import { afterEach, describe, expect, it, vi } from "vitest";
import { createRuntimeClient } from "./client";
import type { FromWorker, ToWorker, WireRequest } from "./protocol";

class FakeWorker {
  readonly sent: ToWorker[] = [];
  onmessage: ((event: MessageEvent<FromWorker>) => void) | null = null;
  onerror: ((event: ErrorEvent) => void) | null = null;
  postMessage(message: ToWorker) { this.sent.push(message); }
  emit(message: FromWorker) { this.onmessage?.({ data: message } as MessageEvent<FromWorker>); }
  requests(): WireRequest[] { return this.sent.flatMap(message => message.type === "request" ? [message.request] : []); }
}

const boot = { version: "1", plugins: [], failedAssemblies: [], persistent: true, tabCoordination: true, elapsedMs: 1, initializeMs: 1 };
// setImmediate, not setTimeout: a full macrotask turn without the 1 ms timer clamp keeps 400 seeds fast.
const flush = () => new Promise(resolve => setImmediate(resolve));
const crash = { type: "failed", message: "RuntimeError: unreachable" } as const;

type Gate = { open: () => void; fail: (error: unknown) => void };
type Call = { url: string; outcome?: string; controller: AbortController; gate?: Gate };

function setup() {
  const worker = new FakeWorker();
  const client = createRuntimeClient(() => worker as unknown as Worker, "x");
  worker.emit({ type: "ready", boot });
  return { worker, client };
}

/** Holds the body serialization of requests to a path until the test opens (or fails) its gate. */
function gates(): (path: string) => Gate {
  const held = new Map<string, Promise<void>>();
  const original = Request.prototype.arrayBuffer;
  vi.spyOn(Request.prototype, "arrayBuffer").mockImplementation(async function (this: Request) {
    await held.get(new URL(this.url).pathname);
    return original.call(this);
  });
  return path => {
    let open!: () => void;
    let fail!: (error: unknown) => void;
    held.set(path, new Promise<void>((resolve, reject) => { open = resolve; fail = reject; }));
    return { open, fail };
  };
}

function lcg(seed: number) {
  return () => { seed = (seed * 1103515245 + 12345) & 0x7fffffff; return seed / 0x7fffffff; };
}

/** Issues 8 requests (no body, body, slow body, slow body that fails) and returns them with the shuffled actions. */
function issue(client: ReturnType<typeof setup>["client"], random: () => number, gate: (path: string) => Gate) {
  const calls: Call[] = [];
  const actions: (() => void)[] = [];
  for (let i = 0; i < 8; i++) {
    const url = `/api/r${i}`;
    const controller = new AbortController();
    const kind = Math.floor(random() * 4);
    const call: Call = { url, controller, gate: kind >= 2 ? gate(url) : undefined };
    const init: RequestInit = kind === 0 ? { signal: controller.signal } : { method: "POST", body: `b${i}`, signal: controller.signal };
    void client.fetch(url, init).then(() => "ok", (error: unknown) => (error as Error).name).then(outcome => { call.outcome = outcome; });
    calls.push(call);
    if (call.gate) actions.push(kind === 3 ? () => call.gate!.fail(new TypeError("read failed")) : () => call.gate!.open());
    if (random() < 0.3) actions.push(() => controller.abort());
  }
  return { calls, actions };
}

function shuffle<T>(items: T[], random: () => number) {
  for (let i = items.length - 1; i > 0; i--) {
    const j = Math.floor(random() * (i + 1));
    [items[i], items[j]] = [items[j]!, items[i]!];
  }
}

describe("runtime client: posting order under random interleavings", () => {
  afterEach(() => { vi.restoreAllMocks(); });

  it.each(Array.from({ length: 400 }, (_, i) => i + 1))("seed %i", async seed => {
    const random = lcg(seed);
    const { worker, client } = setup();
    const { calls, actions } = issue(client, random, gates());
    let sentAtCrash = -1;
    if (random() < 0.25) actions.push(() => { sentAtCrash = worker.requests().length; worker.emit(crash); });
    shuffle(actions, random);
    for (const action of actions) { action(); await (random() < 0.5 ? flush() : Promise.resolve()); }
    for (const call of calls) call.gate?.open();
    await flush(); await flush(); await flush();
    for (const request of worker.requests()) worker.emit({ type: "error", id: request.id, message: "x" });
    await flush(); await flush();

    for (const call of calls) expect(call.outcome, `${call.url} settled`).toBeDefined();
    const indexes = worker.requests().map(request => Number(request.url.slice("/api/r".length)));
    expect(indexes, "posted in call order, each once").toEqual([...new Set(indexes)].sort((a, b) => a - b));
    if (sentAtCrash >= 0) expect(worker.requests(), "nothing posted after the crash").toHaveLength(sentAtCrash);
    for (const call of calls) expect(getEventListeners(call.controller.signal, "abort"), `${call.url} listeners`).toHaveLength(0);
  });
});

it("runtime client: cancels a posted request exactly once when it is aborted", async () => {
  const { worker, client } = setup();
  const controller = new AbortController();
  const request = client.fetch("/api/a", { method: "POST", body: "x", signal: controller.signal });
  await flush();
  expect(worker.requests()).toHaveLength(1);
  controller.abort();
  await expect(request).rejects.toMatchObject({ name: "AbortError" });
  expect(worker.sent.filter(message => message.type === "cancel")).toHaveLength(1);
  expect(getEventListeners(controller.signal, "abort")).toHaveLength(0);
});

it("runtime client: never posts a request whose turn opens in the tick the runtime fails", async () => {
  const { worker, client } = setup();
  const gate = gates()("/api/slow");
  const slow = client.fetch("/api/slow", { method: "POST", body: "x" });
  const later = client.fetch("/api/later");
  await flush();
  gate.open();
  worker.emit(crash);
  await expect(slow).rejects.toThrow("unreachable");
  await expect(later).rejects.toThrow("unreachable");
  await flush();
  expect(worker.requests()).toEqual([]);
});
