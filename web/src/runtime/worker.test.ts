// @vitest-environment node
import { beforeAll, beforeEach, expect, it, vi } from "vitest";
import { encodeFrame } from "./framing";
import type { FromWorker, ToWorker, WireRequest } from "./protocol";

const run = vi.hoisted(() => vi.fn<(method: string, url: string, dispatch: () => Promise<Uint8Array>) => Promise<Uint8Array>>());
vi.mock("./dotnetBoot", () => ({
  bootDotnet: async () => ({ boot: { version: "1" }, interop: {}, persistence: { coordinated: false, run, stop: () => {} } }),
}));

/** The worker's global scope: what it posts, and a way to send it messages. */
const posted: FromWorker[] = [];
/** Makes `postMessage` throw for the messages it matches, as a closing worker scope may. */
let refuse: (message: FromWorker) => boolean = () => false;
const scope = {
  onmessage: null as ((event: { data: ToWorker }) => void) | null,
  postMessage: (message: FromWorker) => {
    if (refuse(message)) throw new DOMException("The worker is closing.", "InvalidStateError");
    posted.push(message);
  },
  addEventListener: () => {},
};
const send = (data: ToWorker) => scope.onmessage!({ data });
const settle = () => new Promise(resolve => setTimeout(resolve, 0));
const request = (id: number): WireRequest => ({ id, method: "GET", url: `/api/r${id}`, headers: [], body: null });
/** What the worker answered, leaving out the `started` notices. */
const answers = () => posted.filter(message => message.type !== "started");
const ok = () => encodeFrame(200, [], new Uint8Array(0));

beforeAll(async () => {
  vi.stubGlobal("self", scope);
  await import("./worker");
  send({ type: "boot", frameworkUrl: "x" });
  await settle();
  expect(posted).toEqual([{ type: "ready", boot: { version: "1" } }]);
});

beforeEach(() => {
  posted.length = 0;
  refuse = () => false;
  vi.spyOn(console, "error").mockImplementation(() => {});
});

it("runtime worker: answers later requests after one fails with a value nothing can describe", async () => {
  // String() throws for a null-prototype object; neither the crash check nor the error reply may.
  run.mockRejectedValueOnce(Object.create(null));
  run.mockResolvedValueOnce(ok());
  send({ type: "request", request: request(1) });
  send({ type: "request", request: request(2) });
  await settle(); await settle();
  expect(answers()).toEqual([
    expect.objectContaining({ type: "error", id: 1 }),
    expect.objectContaining({ type: "response", id: 2, status: 200 }),
  ]);
});

it("runtime worker: answers a failure whose `instanceof` check throws as a failed request", async () => {
  // `instanceof RuntimeHungError` runs this proxy's getPrototypeOf trap, which throws.
  const hostile = new Proxy({}, { getPrototypeOf: () => { throw new Error("trap"); }, has: () => { throw new Error("trap"); } });
  run.mockRejectedValueOnce(hostile);
  run.mockResolvedValueOnce(ok());
  send({ type: "request", request: request(3) });
  send({ type: "request", request: request(4) });
  await settle(); await settle();
  expect(answers()).toEqual([
    { type: "error", id: 3, message: "The editor runtime failed with an error that cannot be described.", outOfMemory: false, stopped: false },
    expect.objectContaining({ type: "response", id: 4, status: 200 }),
  ]);
});

it("runtime worker: tells the page it kept a runtime whose failure merely reads like an abort", async () => {
  // A TypeError is no abort, whatever its text; the page must not judge "Aborted(1)" alone (runtime/client.ts).
  run.mockRejectedValueOnce(new TypeError("Aborted(1)"));
  send({ type: "request", request: request(8) });
  await settle(); await settle();
  expect(answers()).toEqual([{ type: "error", id: 8, message: "Aborted(1)", outOfMemory: false, stopped: false }]);
});

it("runtime worker: a value that makes handle() itself reject still lets later requests run", async () => {
  // Posting request 5's error answer throws, so handle() rejects; the console throws as well.
  refuse = message => message.type === "error" && message.id === 5;
  vi.spyOn(console, "error").mockImplementation(() => { throw new Error("console"); });
  run.mockRejectedValueOnce(new Error("dispatch failed"));
  run.mockResolvedValueOnce(ok());
  send({ type: "request", request: request(5) });
  send({ type: "request", request: request(6) });
  await settle(); await settle(); await settle();
  expect(answers()).toEqual([expect.objectContaining({ type: "response", id: 6, status: 200 })]);
});

// Last: the runtime stays stopped afterwards.
it("runtime worker: reports a managed exit (an ExitStatus out of the dispatch) as a crash with a readable reason", async () => {
  // Emscripten's `new ExitStatus(code)` (dotnet.native.js) has no toString: String() gives "[object Object]".
  function ExitStatus(this: { name: string; message: string; status: number }, status: number) {
    this.name = "ExitStatus"; this.message = `Program terminated with exit(${status})`; this.status = status;
  }
  run.mockRejectedValueOnce(new (ExitStatus as unknown as new (status: number) => object)(1));
  send({ type: "request", request: request(7) });
  await settle(); await settle();
  expect(answers()).toEqual([{ type: "failed", message: "Program terminated with exit(1)" }]);
  // A request after the stop never runs; its answer says the runtime stopped.
  posted.length = 0;
  send({ type: "request", request: request(9) });
  await settle();
  expect(answers()).toEqual([{ type: "error", id: 9, message: "Program terminated with exit(1)", stopped: true }]);
});
