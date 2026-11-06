// @vitest-environment node
// Node supplies a coherent fetch/FormData/File stack (jsdom's File is opaque to undici's Request).
import { getEventListeners } from "node:events";
import { afterEach, describe, expect, it, vi } from "vitest";
import { createRuntimeClient, RuntimeRequestError } from "./client";
import { decodeFrame } from "./framing";
import type { FromWorker, ToWorker, WireRequest } from "./protocol";

/** Mirrors WireFormat.Frame in SaveToolkit.Browser. */
function encodeFrame(status: number, headers: [string, string][], body = new Uint8Array()): Uint8Array {
  const head = new TextEncoder().encode(JSON.stringify({ status, headers }));
  const frame = new Uint8Array(4 + head.byteLength + body.byteLength);
  new DataView(frame.buffer).setUint32(0, head.byteLength, true);
  frame.set(head, 4);
  frame.set(body, 4 + head.byteLength);
  return frame;
}

class FakeWorker {
  readonly sent: ToWorker[] = [];
  onmessage: ((event: MessageEvent<FromWorker>) => void) | null = null;
  onerror: ((event: ErrorEvent) => void) | null = null;
  postMessage(message: ToWorker) { this.sent.push(message); }
  emit(message: FromWorker) { this.onmessage?.({ data: message } as MessageEvent<FromWorker>); }
  requests(): WireRequest[] { return this.sent.flatMap(message => message.type === "request" ? [message.request] : []); }
  reply(request: WireRequest, status: number, body: string, headers: [string, string][] = [["content-type", "application/json"]]) {
    const frame = encodeFrame(status, headers, new TextEncoder().encode(body));
    const { bodyOffset } = decodeFrame(frame);
    this.emit({ type: "response", id: request.id, status, headers, frame: frame.buffer as ArrayBuffer, bodyOffset });
  }
}

const boot = { version: "1.0.0", plugins: ["builtin.raw"], failedAssemblies: [], persistent: true, tabCoordination: true, elapsedMs: 5, initializeMs: 1 };
const flush = () => new Promise(resolve => setTimeout(resolve, 0));

function setup() {
  const worker = new FakeWorker();
  let created = 0;
  const client = createRuntimeClient(() => { created++; return worker as unknown as Worker; }, "https://host/app/_framework/");
  return { worker, client, created: () => created };
}

describe("frames", () => {
  it("decodes status, headers and body offset", () => {
    const frame = encodeFrame(201, [["ETag", "\"r1\""]], new Uint8Array([7, 8]));
    const decoded = decodeFrame(frame);
    expect(decoded).toMatchObject({ status: 201, headers: [["ETag", "\"r1\""]] });
    expect([...frame.subarray(decoded.bodyOffset)]).toEqual([7, 8]);
  });
  it.each([new Uint8Array([1]), new Uint8Array([200, 0, 0, 0, 123]), encodeFrame(200, [["a", 1 as unknown as string]])])(
    "rejects a malformed frame", frame => expect(() => decodeFrame(frame)).toThrow());
});

it("runtime client: boots once with the framework URL and reports progress and readiness", async () => {
  const { worker, client, created } = setup();
  const ready = client.ready();
  void client.ready();
  expect(created()).toBe(1);
  expect(worker.sent[0]).toEqual({ type: "boot", frameworkUrl: "https://host/app/_framework/" });
  worker.emit({ type: "progress", loaded: 3, total: 9 });
  expect(client.state()).toEqual({ status: "loading", loaded: 3, total: 9 });
  worker.emit({ type: "ready", boot });
  await ready;
  expect(client.state()).toEqual({ status: "ready", boot });
});

it("runtime client: returns real Response objects with status, headers and body", async () => {
  const { worker, client } = setup();
  worker.emit({ type: "ready", boot });
  const pending = client.fetch("/api/presets", { method: "post", headers: { "Content-Type": "application/json" }, body: "{\"a\":1}" });
  await flush();
  const [request] = worker.requests();
  expect(request).toMatchObject({ method: "POST", url: "/api/presets" });
  expect(request!.headers).toContainEqual(["content-type", "application/json"]);
  expect(new TextDecoder().decode(request!.body!)).toBe("{\"a\":1}");
  worker.reply(request!, 201, "{\"id\":\"p1\"}", [["content-type", "application/json"], ["etag", "\"7\""]]);
  const response = await pending;
  expect(response).toBeInstanceOf(Response);
  expect(response.status).toBe(201);
  expect(response.headers.get("etag")).toBe("\"7\"");
  expect(await response.json()).toEqual({ id: "p1" });
});

it("runtime client: serializes multipart bodies with their boundary", async () => {
  const { worker, client } = setup();
  worker.emit({ type: "ready", boot });
  const form = new FormData();
  form.append("files", new File(["zip bytes"], "dump.zip"), "PPSA1/dump.zip");
  void client.fetch("/api/workspaces", { method: "POST", body: form });
  await flush();
  const [request] = worker.requests();
  const type = request!.headers.find(([name]) => name === "content-type")?.[1] ?? "";
  expect(type).toMatch(/^multipart\/form-data; boundary=/);
  const text = new TextDecoder().decode(request!.body!);
  expect(text).toContain("filename=\"PPSA1/dump.zip\"");
  expect(text).toContain("zip bytes");
});

it("runtime client: hands requests to the worker in order, matches replies by id and maps 204 to no body", async () => {
  const { worker, client } = setup();
  worker.emit({ type: "ready", boot });
  const first = client.fetch("/api/a");
  const second = client.fetch("/api/b");
  await flush();
  expect(worker.requests().map(request => request.url)).toEqual(["/api/a", "/api/b"]);
  worker.reply(worker.requests()[1]!, 200, "[]");
  worker.reply(worker.requests()[0]!, 204, "");
  expect((await first).status).toBe(204);
  expect(await (await second).json()).toEqual([]);
});

describe("runtime client: requests reach the worker in call order", () => {
  afterEach(() => { vi.restoreAllMocks(); });

  it("posts a body-less request only after a request called before it finished serializing its body", async () => {
    const { worker, client } = setup();
    worker.emit({ type: "ready", boot });
    const finish = slowSerialization("/api/workspaces/w/documents/d/edits");
    void client.fetch("/api/workspaces/w/documents/d/edits", {
      method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ edits: [] }),
    });
    void client.fetch("/api/workspaces/w/documents/d/download");
    void client.fetch("/api/workspaces/w/export", { method: "POST", body: new FormData() });
    void client.fetch("/api/workspaces/w");
    await flush(); await flush();
    expect(worker.requests()).toEqual([]);
    finish();
    await vi.waitFor(() => expect(worker.requests()).toHaveLength(4));
    expect(worker.requests().map(request => `${request.method} ${request.url}`)).toEqual([
      "POST /api/workspaces/w/documents/d/edits", "GET /api/workspaces/w/documents/d/download",
      "POST /api/workspaces/w/export", "GET /api/workspaces/w"]);
    expect(worker.requests().map(request => request.id)).toEqual([1, 2, 3, 4]);
  });

  it("rejects a request whose body cannot be serialized at once, without letting a later one overtake an earlier one", async () => {
    const { worker, client } = setup();
    worker.emit({ type: "ready", boot });
    const finish = slowSerialization("/api/slow");
    void client.fetch("/api/slow", { method: "POST", body: "{\"edits\":[]}" });
    // A stream body without `duplex` is refused while the body is serialized (like a File changed on disk).
    const unreadable = client.fetch("/api/unreadable", { method: "POST", body: new ReadableStream() });
    void client.fetch("/api/download");
    await expect(unreadable).rejects.toThrow();
    await flush(); await flush();
    expect(worker.requests()).toEqual([]);
    finish();
    await vi.waitFor(() => expect(worker.requests()).toHaveLength(2));
    expect(worker.requests().map(request => request.url)).toEqual(["/api/slow", "/api/download"]);
  });
});

describe("runtime client: requests that end before they are posted", () => {
  afterEach(() => { vi.restoreAllMocks(); });

  it("rejects an aborted request at once, while an earlier body still serializes, and never sends it", async () => {
    const { worker, client } = setup();
    worker.emit({ type: "ready", boot });
    const finish = slowSerialization("/api/slow");
    void client.fetch("/api/slow", { method: "POST", body: "{}" });
    const controller = new AbortController();
    const aborted = client.fetch("/api/aborted", { method: "POST", body: "x".repeat(1 << 20), signal: controller.signal });
    void client.fetch("/api/later");
    controller.abort();
    await expect(aborted).rejects.toMatchObject({ name: "AbortError" });
    expect(worker.requests()).toEqual([]);
    finish();
    await vi.waitFor(() => expect(worker.requests()).toHaveLength(2));
    expect(worker.requests().map(request => request.url)).toEqual(["/api/slow", "/api/later"]);
  });

  it("rejects requests waiting behind a body still serializing as soon as the runtime crashes", async () => {
    const { worker, client } = setup();
    worker.emit({ type: "ready", boot });
    const finish = slowSerialization("/api/slow");
    const slow = client.fetch("/api/slow", { method: "POST", body: "{}" });
    const later = client.fetch("/api/later");
    worker.emit({ type: "failed", message: "RuntimeError: unreachable" });
    await expect(later).rejects.toThrow("unreachable");
    await expect(slow).rejects.toThrow("unreachable");
    finish();
    await flush(); await flush();
    expect(worker.requests()).toEqual([]);
  });

  it("does not let a failed or aborted request hold up later ones", async () => {
    const { worker, client } = setup();
    worker.emit({ type: "ready", boot });
    const unreadable = client.fetch("/api/unreadable", { method: "POST", body: new ReadableStream() });
    const controller = new AbortController();
    const aborted = client.fetch("/api/aborted", { method: "POST", body: "x".repeat(1 << 20), signal: controller.signal });
    controller.abort();
    const later = client.fetch("/api/later");
    await expect(unreadable).rejects.toThrow();
    await expect(aborted).rejects.toMatchObject({ name: "AbortError" });
    await vi.waitFor(() => expect(worker.requests()).toHaveLength(1));
    expect(worker.requests()[0]!.url).toBe("/api/later");
    worker.reply(worker.requests()[0]!, 200, "[]");
    expect(await (await later).json()).toEqual([]);
  });
});

/**
 * Makes the bodies of requests to `path` finish serializing only when the test calls the returned
 * function, like a large File (an import zip) that takes a while to read.
 */
function slowSerialization(path: string): () => void {
  let finish!: () => void;
  const gate = new Promise<void>(resolve => { finish = resolve; });
  const original = Request.prototype.arrayBuffer;
  vi.spyOn(Request.prototype, "arrayBuffer").mockImplementation(async function (this: Request) {
    if (new URL(this.url).pathname === path) await gate;
    return original.call(this);
  });
  return finish;
}

it("runtime client: cancels a request the worker has not started and rejects it with AbortError", async () => {
  const { worker, client } = setup();
  worker.emit({ type: "ready", boot });
  const controller = new AbortController();
  const queued = client.fetch("/api/b", { signal: controller.signal });
  await flush();
  controller.abort();
  await expect(queued).rejects.toMatchObject({ name: "AbortError" });
  const id = worker.requests()[0]!.id;
  expect(worker.sent).toContainEqual({ type: "cancel", id });
});

it("runtime client: ignores the late result of an aborted in-flight request", async () => {
  const { worker, client } = setup();
  worker.emit({ type: "ready", boot });
  const controller = new AbortController();
  const inflight = client.fetch("/api/a", { signal: controller.signal });
  const next = client.fetch("/api/b");
  await flush();
  controller.abort();
  await expect(inflight).rejects.toMatchObject({ name: "AbortError" });
  worker.reply(worker.requests()[0]!, 200, "late");
  worker.reply(worker.requests()[1]!, 200, "\"b\"");
  expect(await (await next).json()).toBe("b");
});

it("runtime client: leaves no abort listener on a long-lived signal once its requests settle", async () => {
  const { worker, client } = setup();
  worker.emit({ type: "ready", boot });
  const { signal } = new AbortController();
  for (let i = 0; i < 5; i++) {
    const answered = client.fetch("/api/a", { signal });
    const failed = client.fetch("/api/b", { signal });
    await flush();
    expect(getEventListeners(signal, "abort")).toHaveLength(2);
    const [a, b] = worker.requests().slice(-2);
    worker.reply(a!, 200, "1");
    worker.emit({ type: "error", id: b!.id, message: "interop failure" });
    await answered; await failed.catch(() => undefined);
  }
  const crashed = client.fetch("/api/c", { signal });
  await flush();
  worker.emit({ type: "failed", message: "RuntimeError: unreachable" });
  await expect(crashed).rejects.toThrow("unreachable");
  expect(getEventListeners(signal, "abort")).toHaveLength(0);
});

it("runtime client: never sends a request whose signal is already aborted", async () => {
  const { worker, client } = setup();
  await expect(client.fetch("/api/a", { signal: AbortSignal.abort() })).rejects.toMatchObject({ name: "AbortError" });
  expect(worker.requests()).toHaveLength(0);
});

it("runtime client: fails queued and future requests when the runtime cannot boot", async () => {
  const { worker, client } = setup();
  const ready = client.ready();
  const queued = client.fetch("/api/plugins");
  await flush();
  worker.emit({ type: "failed", message: "WebAssembly is disabled." });
  await expect(ready).rejects.toThrow("WebAssembly is disabled.");
  await expect(queued).rejects.toBeInstanceOf(TypeError);
  await expect(client.fetch("/api/version")).rejects.toThrow("WebAssembly is disabled.");
  expect(client.state()).toEqual({ status: "failed", message: "WebAssembly is disabled." });
});

it("runtime client: rejects a request the worker could not dispatch and continues with the next", async () => {
  const { worker, client } = setup();
  worker.emit({ type: "ready", boot });
  const failed = client.fetch("/api/a");
  const next = client.fetch("/api/b");
  await flush();
  worker.emit({ type: "error", id: worker.requests()[0]!.id, message: "interop failure" });
  await expect(failed).rejects.toThrow("interop failure");
  worker.reply(worker.requests()[1]!, 200, "1");
  expect((await next).ok).toBe(true);
});

it("runtime client: forwards only headers the caller set, never browser-managed ones", async () => {
  const { worker, client } = setup();
  worker.emit({ type: "ready", boot });
  void client.fetch("/api/reference-packs/p1", { method: "DELETE", headers: { "If-Match": "\"3\"", Origin: "https://evil.example", "Sec-Fetch-Site": "cross-site" } });
  await flush();
  expect(worker.requests()[0]!.headers).toEqual([["if-match", "\"3\""]]);
});

it("runtime client: reports when the worker starts a request, only for live requests", async () => {
  const { worker, client } = setup();
  worker.emit({ type: "ready", boot });
  const onStart = vi.fn();
  const pending = client.fetch("/api/a", {}, { onStart });
  await flush();
  expect(onStart).not.toHaveBeenCalled();
  const id = worker.requests()[0]!.id;
  worker.emit({ type: "started", id });
  expect(onStart).toHaveBeenCalledOnce();
  worker.reply(worker.requests()[0]!, 200, "1");
  await pending;
  worker.emit({ type: "started", id });
  expect(onStart).toHaveBeenCalledOnce();
});

it("runtime client: marks runtime failures, including out-of-memory ones", async () => {
  const { worker, client } = setup();
  worker.emit({ type: "ready", boot });
  const exhausted = client.fetch("/api/workspaces", { method: "POST" });
  const crashed = client.fetch("/api/b");
  await flush();
  worker.emit({ type: "error", id: worker.requests()[0]!.id, message: "Out of memory", outOfMemory: true });
  await expect(exhausted).rejects.toMatchObject({ name: "RuntimeRequestError", outOfMemory: true });
  worker.onerror?.({ message: "Uncaught RangeError: Array buffer allocation failed" } as ErrorEvent);
  const error = await crashed.catch((failure: unknown) => failure);
  expect(error).toBeInstanceOf(RuntimeRequestError);
  expect(error).toMatchObject({ outOfMemory: true });
});

it("runtime client: tells a crash after boot apart from a boot failure", () => {
  const booting = setup();
  void booting.client.ready().catch(() => undefined);
  booting.worker.onerror?.({ message: "Script error." } as ErrorEvent);
  expect(booting.client.state()).toEqual({ status: "failed", message: "Script error." });

  const running = setup();
  void running.client.ready();
  running.worker.emit({ type: "ready", boot });
  running.worker.onerror?.({ message: "Aborted(OOM)" } as ErrorEvent);
  expect(running.client.state()).toEqual({ status: "failed", message: "Aborted(OOM)", crashed: true });
  // A later report about the same dead worker keeps it a crash.
  running.worker.onerror?.({ message: "Script error." } as ErrorEvent);
  expect(running.client.state()).toMatchObject({ crashed: true });
});

it("runtime client: a runtime the worker reports stopped after boot is a crash for every pending request", async () => {
  const { worker, client } = setup();
  void client.ready();
  worker.emit({ type: "ready", boot });
  // A late download (a lazily loaded file) does not send the runtime back to loading.
  worker.emit({ type: "progress", loaded: 1, total: 2, received: 4096 });
  expect(client.state().status).toBe("ready");
  const importing = client.fetch("/api/workspaces", { method: "POST" });
  const queued = client.fetch("/api/presets");
  await flush();
  worker.emit({ type: "failed", message: "Aborted(OOM)" });
  await expect(importing).rejects.toMatchObject({ name: "RuntimeRequestError", outOfMemory: true });
  await expect(queued).rejects.toMatchObject({ outOfMemory: true });
  expect(client.state()).toEqual({ status: "failed", message: "Aborted(OOM)", crashed: true });
  await expect(client.fetch("/api/presets")).rejects.toThrow("Aborted(OOM)");
});

it("runtime client: a request that hit a WebAssembly trap crashes the runtime for every pending and later request", async () => {
  const { worker, client } = setup();
  void client.ready();
  worker.emit({ type: "ready", boot });
  const download = client.fetch("/api/workspaces/w/documents/d/download");
  const queued = client.fetch("/api/workspaces/w");
  await flush();
  const sent = worker.requests().length;
  // The trapped runtime may still answer, but its workspace state is suspect: do not wait for it.
  worker.emit({ type: "error", id: worker.requests()[0]!.id, message: "memory access out of bounds" });
  await expect(download).rejects.toThrow("memory access out of bounds");
  await expect(queued).rejects.toThrow("memory access out of bounds");
  expect(client.state()).toEqual({ status: "failed", message: "memory access out of bounds", crashed: true });
  await expect(client.fetch("/api/workspaces/w")).rejects.toThrow("memory access out of bounds");
  expect(worker.requests()).toHaveLength(sent);
  // A late answer from the trapped runtime is ignored.
  worker.reply(worker.requests()[1]!, 200, "{}");
  expect(client.state()).toMatchObject({ status: "failed", crashed: true });
});

it("runtime client: reports downloaded bytes with the boot progress", () => {
  const { worker, client } = setup();
  void client.ready();
  worker.emit({ type: "progress", loaded: 3, total: 9, received: 1_500_000 });
  expect(client.state()).toEqual({ status: "loading", loaded: 3, total: 9, received: 1_500_000, starting: undefined });
});
