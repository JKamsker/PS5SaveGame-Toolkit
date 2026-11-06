import { afterEach, expect, it, vi } from "vitest";
import { api } from "../api/client";
import { openWorkspaceCount } from "./tabWorkspaces";

afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); vi.useRealTimers(); });
const leaving = () => { const e = new Event("beforeunload", { cancelable: true }); window.dispatchEvent(e); return e.defaultPrevented; };
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });

it("upload that timed out client-side but completed in the runtime: workspace listed and opened, but no guard", async () => {
  vi.useFakeTimers();
  vi.stubGlobal("fetch", vi.fn((_u: string, init: RequestInit) => new Promise<Response>((_r, fail) => init.signal?.addEventListener("abort", () => fail(init.signal!.reason)))));
  const upload = api.upload([{ file: new File(["x"], "big.zip"), path: "big.zip" }]).catch((e: unknown) => e);
  await vi.advanceTimersByTimeAsync(5 * 60_000 + 1);
  expect(await upload).toMatchObject({ status: 0, message: expect.stringMatching(/timed out/) });
  vi.useRealTimers();
  // The runtime finished the import anyway; Home lists it and the user opens and edits it.
  vi.stubGlobal("fetch", vi.fn()
    .mockResolvedValueOnce(json([{ id: "orphan", name: "big", createdAt: "x", documentCount: 0 }]))
    .mockResolvedValueOnce(json({ id: "orphan", name: "big", createdAt: "x", titles: [], documents: [] })));
  await api.workspaces();
  await api.workspace("orphan");
  expect(openWorkspaceCount()).toBe(0);
  expect(leaving()).toBe(false);
});

it("workspace swept by the runtime (idle) is not forgotten when the list no longer contains it", async () => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValueOnce(json({ id: "w9", name: "n", createdAt: "x", titles: [], documents: [] })).mockResolvedValueOnce(json([])));
  await api.upload([{ file: new File(["x"], "a.zip"), path: "a.zip" }]);
  expect(await api.workspaces()).toEqual([]);
  expect(openWorkspaceCount()).toBe(1);
  expect(leaving()).toBe(true);
});
