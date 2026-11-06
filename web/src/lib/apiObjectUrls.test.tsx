import { act, cleanup, render, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { api } from "../api/client";
import { ApiImage } from "../components/ui/ApiImage";
import { acquireObjectUrl, IMAGE_CONCURRENCY, invalidateObjectUrls, staleAfter } from "./apiObjectUrls";

interface Call { url: string; signal: AbortSignal; answer: (response: Response) => void; fail: (error: unknown) => void }

let calls: Call[];
let sequence = 0;
/** Each test uses its own paths: the object URL cache is module state. */
const unique = (name: string) => `/workspaces/w${++sequence}/directories/${name}/icon`;
const png = () => new Response(new Uint8Array([137, 80, 78, 71]), { headers: { "content-type": "image/png" } });
const flush = () => act(async () => { await new Promise(resolve => setTimeout(resolve, 0)); });

beforeEach(() => {
  calls = [];
  vi.stubGlobal("fetch", vi.fn((url: string, init: RequestInit) => new Promise<Response>((answer, fail) => {
    calls.push({ url, signal: init.signal!, answer, fail });
    init.signal?.addEventListener("abort", () => fail(init.signal!.reason));
  })));
  let next = 0;
  URL.createObjectURL = vi.fn(() => `blob:test/${++next}`);
  URL.revokeObjectURL = vi.fn();
});
// Unmounting releases every lease, which frees the image request slots for the next test.
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.useRealTimers(); });

it("hands at most a few image requests to the runtime, so a user action is not queued behind hundreds", async () => {
  const view = render(<>{Array.from({ length: 300 }, (_, i) => <ApiImage key={i} path={unique(`dir${i}`)} alt="" />)}</>);
  await flush();
  expect(calls).toHaveLength(IMAGE_CONCURRENCY);
  void api.undo("w", "d").catch(() => undefined);
  await flush();
  expect(calls.findIndex(call => call.url.endsWith("/undo"))).toBe(IMAGE_CONCURRENCY);

  // The user scrolled away: in-flight requests are cancelled and the queued ones never start.
  view.unmount();
  await flush();
  expect(calls.slice(0, IMAGE_CONCURRENCY).every(call => call.signal.aborted)).toBe(true);
  expect(calls.filter(call => call.url.endsWith("/icon"))).toHaveLength(IMAGE_CONCURRENCY);
});

it("starts the next image when one finishes and shows the loaded image", async () => {
  const paths = [unique("a"), unique("b"), unique("c")];
  const view = render(<>{paths.map(path => <ApiImage key={path} path={path} alt={path} />)}</>);
  await flush();
  expect(calls.map(call => call.url)).toEqual(paths.slice(0, 2).map(path => `/api${path}`));
  calls[0]!.answer(png());
  await waitFor(() => expect(view.getByAltText(paths[0]!).getAttribute("src")).toMatch(/^blob:test\//));
  expect(calls.map(call => call.url)).toContain(`/api${paths[2]}`);
});

it("reports a failed fetch to the image and retries it for the next user instead of caching the failure", async () => {
  const path = unique("broken");
  const onFailure = vi.fn();
  const first = render(<ApiImage path={path} alt="" onFailure={onFailure} />);
  await flush();
  calls[0]!.answer(new Response(JSON.stringify({ error: "Icon not found" }), { status: 404, headers: { "content-type": "application/json" } }));
  await waitFor(() => expect(onFailure).toHaveBeenCalledOnce());
  expect(first.container.querySelector("img")!.getAttribute("src")).toBeNull();
  first.unmount();

  const transportFailure = vi.fn();
  render(<ApiImage path={path} alt="" onFailure={transportFailure} />);
  await flush();
  expect(calls).toHaveLength(2);
  calls[1]!.fail(new TypeError("Failed to fetch"));
  await waitFor(() => expect(transportFailure).toHaveBeenCalledOnce());
});

it("shares one request between users and keeps it while anyone still waits", async () => {
  const path = unique("shared");
  const one = acquireObjectUrl(path);
  const two = acquireObjectUrl(path);
  await flush();
  expect(calls).toHaveLength(1);
  one.release();
  one.release(); // idempotent
  expect(calls[0]!.signal.aborted).toBe(false);
  calls[0]!.answer(png());
  await expect(two.url).resolves.toBe("blob:test/1");
  await expect(one.url).resolves.toBe("blob:test/1");
});

it("aborts a pending fetch as soon as its last user releases it, without reporting a failure", async () => {
  const path = unique("gone");
  const onFailure = vi.fn();
  const view = render(<ApiImage path={path} alt="" onFailure={onFailure} />);
  await flush();
  view.unmount();
  await flush();
  expect(calls[0]!.signal.aborted).toBe(true);
  expect(onFailure).not.toHaveBeenCalled();
  expect(URL.createObjectURL).not.toHaveBeenCalled();
  // The aborted entry is not reused.
  acquireObjectUrl(path);
  await flush();
  expect(calls).toHaveLength(2);
});

it("keeps a loaded URL briefly after release so a row scrolled back into view does not refetch, then revokes it", async () => {
  vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout"] });
  const path = unique("cached");
  const lease = acquireObjectUrl(path);
  await vi.advanceTimersByTimeAsync(0);
  calls[0]!.answer(png());
  const url = await lease.url;
  lease.release();
  await vi.advanceTimersByTimeAsync(10_000);
  const again = acquireObjectUrl(path);
  await expect(again.url).resolves.toBe(url);
  expect(calls).toHaveLength(1);
  again.release();
  await vi.advanceTimersByTimeAsync(30_000);
  expect(URL.revokeObjectURL).toHaveBeenCalledWith(url);
});

it("lazy images fetch only once they come near the viewport", async () => {
  let notify: ((entries: { isIntersecting: boolean }[]) => void) | undefined;
  vi.stubGlobal("IntersectionObserver", class {
    constructor(callback: (entries: { isIntersecting: boolean }[]) => void) { notify = callback; }
    observe() {}
    disconnect() {}
  });
  const path = unique("lazy");
  render(<ApiImage path={path} alt="" loading="lazy" />);
  await flush();
  expect(calls).toHaveLength(0);
  act(() => notify!([{ isIntersecting: true }]));
  await flush();
  expect(calls.map(call => call.url)).toEqual([`/api${path}`]);
});

const referenceAsset = () => `/workspaces/w${++sequence}/documents/d/reference-assets?id=pack%3Aicon.webp`;
const answerJson = (call: Call, body: unknown) => call.answer(new Response(JSON.stringify(body), { headers: { "content-type": "application/json" } }));

it("fetches reference assets again after a reference pack is re-imported, refreshing images on screen", async () => {
  const path = referenceAsset();
  const view = render(<ApiImage path={path} alt="icon" />);
  await flush();
  calls[0]!.answer(png());
  await waitFor(() => expect(view.getByAltText("icon").getAttribute("src")).toBe("blob:test/1"));
  const kept = acquireObjectUrl(path);
  await expect(kept.url).resolves.toBe("blob:test/1");

  // The same pack id imported again with new pictures.
  const imported = api.importReferencePack(new File(["{}"], "pack.zip"));
  await flush();
  answerJson(calls[1]!, { id: "pack" });
  await imported;
  await flush();
  expect(calls.map(call => call.url).slice(2)).toEqual([`/api${path}`]);
  // The old picture stays until the new one has arrived.
  expect(view.getByAltText("icon").getAttribute("src")).toBe("blob:test/1");
  calls[2]!.answer(png());
  await waitFor(() => expect(view.getByAltText("icon").getAttribute("src")).toBe("blob:test/2"));
  const fresh = acquireObjectUrl(path);
  await expect(fresh.url).resolves.toBe("blob:test/2");
  expect(calls).toHaveLength(3);
  fresh.release();
  kept.release();
});

it("revokes a dropped entry's URL once its last user releases it", async () => {
  vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout"] });
  const path = referenceAsset();
  const lease = acquireObjectUrl(path);
  await vi.advanceTimersByTimeAsync(0);
  calls[0]!.answer(png());
  const url = await lease.url;
  invalidateObjectUrls(cached => cached === path);
  lease.release();
  await vi.advanceTimersByTimeAsync(30_000);
  expect(URL.revokeObjectURL).toHaveBeenCalledWith(url);
});

it("drops only what a change can affect", () => {
  const doc = "/workspaces/w/documents/d";
  const stale = (method: string, path: string, cached: string) => staleAfter(method, path)?.(cached) ?? false;
  for (const path of ["/reference-packs", "/reference-packs/pack"]) {
    expect(stale("POST", path, `${doc}/reference-assets?id=a`)).toBe(true);
    expect(stale("DELETE", path, "/workspaces/w/directories/x/icon")).toBe(false);
  }
  for (const change of ["edits", "actions", "undo", "redo", "presets/p/apply"]) {
    expect(stale("POST", `${doc}/${change}`, `${doc}/assets?id=a`)).toBe(true);
    expect(stale("POST", `${doc}/${change}`, `${doc}/reference-assets?id=a`)).toBe(false);
    expect(stale("POST", `${doc}/${change}`, "/workspaces/w/documents/other/assets?id=a")).toBe(false);
  }
  // Reads sent as POST change nothing.
  expect(staleAfter("POST", `${doc}/reference-fields/records`)).toBeUndefined();
  expect(staleAfter("POST", `${doc}/presets/p/preview`)).toBeUndefined();
  expect(staleAfter("POST", "/workspaces")).toBeUndefined();
  expect(stale("DELETE", doc, `${doc}/reference-assets?id=a`)).toBe(true);
  expect(stale("DELETE", "/workspaces/w", "/workspaces/w/directories/x/icon")).toBe(true);
  expect(stale("DELETE", "/workspaces/w", "/workspaces/w2/directories/x/icon")).toBe(false);
});
