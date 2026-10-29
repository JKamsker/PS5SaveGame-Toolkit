import { afterEach, describe, expect, it, vi } from "vitest";
import { api, ApiError, request } from "./client";
import { retryRead } from "./http";

afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });

const problem = { title: "Export unavailable", detail: "The dump is busy. Wait for the current operation to finish." };
const response = (body: unknown, status = 503) => new Response(JSON.stringify(body), {
  status, headers: { "content-type": "application/problem+json" },
});

it("bounds read retries and skips permanent responses, cancellations and ambiguous transport failures", () => {
  for (const status of [0, 400, 404, 409, 422]) expect(retryRead(0, new ApiError("Rejected", status))).toBe(false);
  expect(retryRead(0, new ApiError("Unavailable", 503))).toBe(true);
  expect(retryRead(1, new ApiError("Unavailable", 503))).toBe(true);
  expect(retryRead(2, new ApiError("Unavailable", 503))).toBe(false);
});

describe("HTTP error diagnostics", () => {
  it.each([400, 404, 409, 422, 500, 503])("uses ProblemDetails title and detail for HTTP %s", async status => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response(problem, status)));
    await expect(request("/test")).rejects.toMatchObject({ status, body: problem,
      message: `${problem.title}: ${problem.detail}` });
  });

  it.each([api.exportZip.bind(null, "w", "Everything", true), api.blob.bind(null, "w", "d", "b", 0, 16)])(
    "uses the same ProblemDetails diagnostics for binary responses", async call => {
      vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response(problem)));
      await expect(call()).rejects.toMatchObject({ status: 503, message: `${problem.title}: ${problem.detail}` });
    });

  it.each(["{bad json", "<html>System.Exception: stack trace</html>"])("keeps HTTP status for invalid JSON %s", async body => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(body, { status: 502, headers: { "content-type": "application/json" } })));
    await expect(request("/test")).rejects.toMatchObject({ status: 502, message: expect.stringContaining("502") });
  });

  it("does not expose a non-JSON stack trace", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("System.Exception: private stack", { status: 500 })));
    await expect(request("/test")).rejects.toMatchObject({ status: 500, message: expect.not.stringContaining("System.Exception") });
  });

  it.each([
    [{ error: "Invalid data: CRC mismatch" }, "Invalid data: CRC mismatch"],
    [{ title: "Dump busy" }, "Dump busy"],
    [{ detail: "Try again after packing finishes." }, "Try again after packing finishes."],
    [{ title: "Dump busy", detail: "Dump busy" }, "Dump busy"],
    [{ error: { stack: "private stack" } }, "Request failed (400). Please try again."],
  ])("supports existing envelopes and optional ProblemDetails fields %j", async (body, message) => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response(body, 400)));
    await expect(request("/test")).rejects.toMatchObject({ status: 400, message });
  });

  it("normalizes a network failure and never replays a mutation", async () => {
    const fetch = vi.fn().mockRejectedValue(new TypeError("Failed to fetch"));
    vi.stubGlobal("fetch", fetch);
    await expect(api.undo("w", "d")).rejects.toMatchObject({ status: 0, message: expect.stringMatching(/server.*running/i) });
    expect(fetch).toHaveBeenCalledOnce();
  });

  it("reports malformed successful JSON without exposing parser details", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("{", { headers: { "content-type": "application/json" } })));
    await expect(request("/test")).rejects.toBeInstanceOf(ApiError);
  });

  it("rejects an unexpected successful HTML page as unreadable", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("<html>System.Exception</html>")));
    await expect(request("/test")).rejects.toMatchObject({ status: 200, message: expect.stringContaining("unreadable response") });
  });

  it("normalizes interrupted response bodies for JSON, bytes and downloads", async () => {
    const bodyFailure = () => Promise.reject(new TypeError("connection lost"));
    const fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, headers: new Headers({ "content-type": "application/json" }),
      json: bodyFailure, arrayBuffer: bodyFailure, blob: bodyFailure });
    vi.stubGlobal("fetch", fetch);
    for (const call of [() => request("/test"), () => api.blob("w", "d", "b", 0, 16), () => api.exportZip("w", "Everything", true)]) {
      await expect(call()).rejects.toMatchObject({ status: 0, message: expect.stringMatching(/server.*running/i) });
    }
    expect(fetch).toHaveBeenCalledTimes(3);
  });

  it("includes reading the response body in its deadline", async () => {
    vi.useFakeTimers();
    vi.stubGlobal("fetch", vi.fn(async (_url: string, init: RequestInit) => ({ ok: true, status: 200,
      headers: new Headers({ "content-type": "application/json" }),
      json: () => new Promise((_resolve, reject) => init.signal!.addEventListener("abort", () => reject(init.signal!.reason))) })));
    const outcome = request("/test").catch(error => error);
    await vi.advanceTimersByTimeAsync(60_000);
    expect(await outcome).toMatchObject({ status: 0, message: expect.stringMatching(/timed out/i) });
    expect(vi.getTimerCount()).toBe(0);
  });

  it("preserves structured validation, stale-edit and plan responses", async () => {
    const edit = { ok: false, stale: true, errors: [], changes: [], warnings: [], state: { revision: 2 } };
    const plan = { revision: 2, stale: true, scope: {}, plan: { errors: [], changes: [], skippedRows: [], warnings: [] } };
    vi.stubGlobal("fetch", vi.fn().mockResolvedValueOnce(response(edit, 409)).mockResolvedValueOnce(response(plan, 422)));
    expect(await request("/edits")).toEqual(edit);
    expect(await request("/plan")).toEqual(plan);
  });

  it("does not treat ProblemDetails validation extensions as an EditResult", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response({ ...problem, errors: { name: ["Required"] } }, 422)));
    await expect(request("/test")).rejects.toMatchObject({ status: 422, message: `${problem.title}: ${problem.detail}` });
  });

  it("settles a timed-out request and never retries it", async () => {
    vi.useFakeTimers();
    const fetch = vi.fn((_url: string, init: RequestInit) => new Promise<Response>((_resolve, reject) => {
      init.signal!.addEventListener("abort", () => reject(init.signal!.reason));
    }));
    vi.stubGlobal("fetch", fetch);
    const outcome = request("/test").catch(error => error);
    await vi.advanceTimersByTimeAsync(60_000);
    expect(await Promise.race([outcome, Promise.resolve("pending")])).toMatchObject({ status: 0, message: expect.stringMatching(/timed out.*retry/i) });
    expect(fetch).toHaveBeenCalledOnce();
  });

  it("normalizes explicit cancellation", async () => {
    const controller = new AbortController();
    vi.stubGlobal("fetch", vi.fn((_url: string, init: RequestInit) => new Promise<Response>((_resolve, reject) => {
      init.signal!.addEventListener("abort", () => reject(init.signal!.reason));
    })));
    const outcome = request("/test", { signal: controller.signal }).catch(error => error);
    controller.abort();
    expect(await outcome).toMatchObject({ status: 0, message: expect.stringMatching(/cancelled/i) });
  });
});

describe("upload error lifecycle", () => {
  function upload() {
    const xhr = { open: vi.fn(), send: vi.fn(), upload: {}, responseType: "", timeout: 0,
      status: 503, response: problem, onload: null as (() => void) | null,
      onerror: null as (() => void) | null, onabort: null as (() => void) | null, ontimeout: null as (() => void) | null };
    vi.stubGlobal("XMLHttpRequest", class { constructor() { return xhr; } });
    const failure = vi.fn();
    const settled = api.upload([{ file: new File(["data"], "save.zip"), path: "save.zip" }]).catch(failure);
    return { xhr, failure, settled };
  }

  it("uses ProblemDetails on upload failure", async () => {
    const { xhr, failure, settled } = upload();
    xhr.onload!();
    await settled;
    expect(failure).toHaveBeenCalledWith(expect.objectContaining({ status: 503, message: `${problem.title}: ${problem.detail}` }));
  });

  it.each(["onabort", "ontimeout", "onerror"] as const)("settles %s and permits a fresh upload", async event => {
    const { xhr, failure } = upload();
    xhr[event]?.();
    await Promise.resolve();
    expect(failure).toHaveBeenCalledWith(expect.objectContaining({ status: 0, message: expect.stringMatching(
      event === "onabort" ? /cancelled/i : event === "ontimeout" ? /timed out/i : /server/i) }));
    expect(xhr.timeout).toBeGreaterThan(0);
    const next = upload();
    next.xhr.status = 201;
    next.xhr.onload!();
    await next.settled;
    expect(next.failure).not.toHaveBeenCalled();
  });
});
