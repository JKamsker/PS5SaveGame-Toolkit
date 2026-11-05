// @vitest-environment node
// Node supplies a coherent fetch/FormData/File stack (jsdom's File is opaque to undici's Request).
import { describe, expect, it } from "vitest";
import { createRuntimeClient } from "./client";
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

const boot = { version: "1.0.0", plugins: ["builtin.raw"], failedAssemblies: [], persistent: true, elapsedMs: 5, initializeMs: 1 };
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
