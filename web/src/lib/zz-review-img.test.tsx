import { act, cleanup, render, waitFor } from "@testing-library/react";
import { StrictMode } from "react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { ApiImage } from "../components/ui/ApiImage";
import { acquireObjectUrl } from "./apiObjectUrls";

interface Call { url: string; signal: AbortSignal; answer: (r: Response) => void }
let calls: Call[];
const png = () => new Response(new Uint8Array([1, 2]), { headers: { "content-type": "image/png" } });
const flush = () => act(async () => { await new Promise(r => setTimeout(r, 0)); });
beforeEach(() => {
  calls = [];
  vi.stubGlobal("fetch", vi.fn((url: string, init: RequestInit) => new Promise<Response>((answer, fail) => {
    calls.push({ url, signal: init.signal!, answer });
    init.signal?.addEventListener("abort", () => fail(init.signal!.reason));
  })));
  let n = 0; URL.createObjectURL = vi.fn(() => `blob:t/${++n}`); URL.revokeObjectURL = vi.fn();
});
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

it("StrictMode: 2 images + both slots; nothing deadlocks", async () => {
  const v = render(<StrictMode><ApiImage path="/s/a" alt="a" /><ApiImage path="/s/b" alt="b" /><ApiImage path="/s/c" alt="c" /></StrictMode>);
  await flush();
  console.log("calls", calls.map(c => `${c.url} aborted=${c.signal.aborted}`));
  for (const c of calls.filter(c => !c.signal.aborted)) c.answer(png());
  await flush();
  for (const c of calls.filter(c => !c.signal.aborted)) c.answer(png());
  await waitFor(() => expect(v.getByAltText("c").getAttribute("src")).toMatch(/^blob:/));
});

it("same path re-acquired after an asset changes keeps serving the old bytes (no revision in key)", async () => {
  const a = acquireObjectUrl("/ws/docs/d/reference-assets?id=pack:x.webp");
  await flush(); calls[0]!.answer(png());
  const first = await a.url;
  // pack re-imported with new bytes; a new component mounts for the same asset id
  const b = acquireObjectUrl("/ws/docs/d/reference-assets?id=pack:x.webp");
  expect(await b.url).toBe(first);
  expect(calls).toHaveLength(1);
  a.release(); b.release();
});
