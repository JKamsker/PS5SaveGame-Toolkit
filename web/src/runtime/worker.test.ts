// @vitest-environment node
import { expect, it, vi } from "vitest";
import { encodeFrame } from "./framing";
import type { FromWorker, ToWorker, WireRequest } from "./protocol";

const run = vi.hoisted(() => vi.fn<(method: string, url: string, dispatch: () => Promise<Uint8Array>) => Promise<Uint8Array>>());
vi.mock("./dotnetBoot", () => ({
  bootDotnet: async () => ({ boot: { version: "1" }, interop: {}, persistence: { coordinated: false, run, stop: () => {} } }),
}));

/** The worker's global scope: what it posts, and a way to send it messages. */
const posted: FromWorker[] = [];
const scope = {
  onmessage: null as ((event: { data: ToWorker }) => void) | null,
  postMessage: (message: FromWorker) => { posted.push(message); },
  addEventListener: () => {},
};
const send = (data: ToWorker) => scope.onmessage!({ data });
const settle = () => new Promise(resolve => setTimeout(resolve, 0));
const request = (id: number): WireRequest => ({ id, method: "GET", url: `/api/r${id}`, headers: [], body: null });

it("runtime worker: answers later requests after one fails with a value nothing can describe", async () => {
  vi.stubGlobal("self", scope);
  vi.spyOn(console, "error").mockImplementation(() => {});
  await import("./worker");
  send({ type: "boot", frameworkUrl: "x" });
  await settle();
  expect(posted.map(message => message.type)).toEqual(["ready"]);

  // String() throws for a null-prototype object; neither the crash check nor the error reply may.
  run.mockRejectedValueOnce(Object.create(null));
  run.mockResolvedValueOnce(encodeFrame(200, [], new Uint8Array(0)));
  send({ type: "request", request: request(1) });
  send({ type: "request", request: request(2) });
  await settle(); await settle();
  expect(posted.filter(message => message.type !== "started")).toEqual([
    { type: "ready", boot: { version: "1" } },
    expect.objectContaining({ type: "error", id: 1 }),
    expect.objectContaining({ type: "response", id: 2, status: 200 }),
  ]);
});
