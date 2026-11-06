import { afterEach, expect, it, vi } from "vitest";
import { api } from "../api/client";
import { runtime } from "../runtime";
import { openWorkspaceCount, workspaceClosed, workspaceOpened } from "./tabWorkspaces";

afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); });

/** Dispatches a cancelable beforeunload; true when the page asked the browser to confirm leaving. */
function leaving(): boolean {
  const event = new Event("beforeunload", { cancelable: true });
  window.dispatchEvent(event);
  return event.defaultPrevented;
}

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });
const workspace = (id: string) => ({ id, name: id, createdAt: "2026-11-06", titles: [], documents: [] });

it("asks before leaving only while this tab holds a workspace", () => {
  expect(leaving()).toBe(false);
  workspaceOpened("a");
  workspaceOpened("b");
  expect(leaving()).toBe(true);
  workspaceClosed("a");
  expect(leaving()).toBe(true);
  workspaceClosed("b");
  workspaceClosed("b");
  expect(openWorkspaceCount()).toBe(0);
  expect(leaving()).toBe(false);
});

it("does not ask once the runtime has crashed, since its workspaces are already lost", () => {
  workspaceOpened("c");
  vi.spyOn(runtime, "state").mockReturnValue({ status: "failed", message: "Aborted(OOM)", crashed: true });
  expect(leaving()).toBe(false);
  workspaceClosed("c");
});

it("tracks imports, closes and workspaces the runtime no longer has through the API client", async () => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValueOnce(json(workspace("w1"))).mockResolvedValueOnce(json(workspace("w2"))));
  await api.upload([{ file: new File(["x"], "a.zip"), path: "a.zip" }]);
  await api.upload([{ file: new File(["x"], "b.zip"), path: "b.zip" }]);
  expect(openWorkspaceCount()).toBe(2);
  expect(leaving()).toBe(true);

  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 204 })));
  await api.deleteWorkspace("w1");
  expect(openWorkspaceCount()).toBe(1);

  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json({ error: "Workspace not found." }, 404)));
  await expect(api.workspace("w2")).rejects.toMatchObject({ status: 404 });
  expect(openWorkspaceCount()).toBe(0);
  expect(leaving()).toBe(false);
});

it("keeps asking when closing a workspace failed", async () => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(workspace("w3"))));
  await api.upload([{ file: new File(["x"], "c.zip"), path: "c.zip" }]);
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json({ error: "Busy" }, 503)));
  await expect(api.deleteWorkspace("w3")).rejects.toMatchObject({ status: 503 });
  expect(leaving()).toBe(true);
  workspaceClosed("w3");
});
