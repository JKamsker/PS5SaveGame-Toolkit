import type { Page } from "@playwright/test";
import { ApiRequest, ApiResponse, installApiHooks } from "./apiRoutes";

/**
 * page.request for the in-browser API: sends a request straight to the page's .NET runtime worker
 * (same queue as the app's own requests). Like page.request it bypasses routes and request events.
 * It is sent once the app is idle, behind every request the app already sent or queued (apiPageHooks'
 * `idle`), so a read observes the app's earlier writes. The app must have started its runtime (any page
 * that rendered the UI has).
 */

type FilePart = { name: string; mimeType: string; buffer: Buffer };
export interface ApiRequestOptions {
  /** Objects are sent as JSON; strings and buffers as-is (Playwright's `data`). */
  data?: unknown;
  params?: Record<string, string | number | boolean>;
  headers?: Record<string, string>;
  multipart?: Record<string, string | FilePart>;
}
type Method = "GET" | "POST" | "PUT" | "PATCH" | "DELETE";
export interface ApiClient {
  get(url: string, options?: ApiRequestOptions): Promise<ApiResponse>;
  post(url: string, options?: ApiRequestOptions): Promise<ApiResponse>;
  put(url: string, options?: ApiRequestOptions): Promise<ApiResponse>;
  patch(url: string, options?: ApiRequestOptions): Promise<ApiResponse>;
  delete(url: string, options?: ApiRequestOptions): Promise<ApiResponse>;
  fetch(url: string, options?: ApiRequestOptions & { method?: Method }): Promise<ApiResponse>;
}

export function apiRequest(page: Page): ApiClient {
  const send = (method: Method) => (url: string, options?: ApiRequestOptions) => dispatch(page, method, url, options);
  return { get: send("GET"), post: send("POST"), put: send("PUT"), patch: send("PATCH"), delete: send("DELETE"),
    fetch: (url, options) => dispatch(page, options?.method ?? "GET", url, options) };
}

async function dispatch(page: Page, method: Method, url: string, options: ApiRequestOptions = {}): Promise<ApiResponse> {
  await installApiHooks(page);
  const target = new URL(url, "http://savetoolkit.local");
  for (const [name, value] of Object.entries(options.params ?? {})) target.searchParams.set(name, String(value));
  const headers = Object.fromEntries(Object.entries(options.headers ?? {}).map(([name, value]) => [name.toLowerCase(), value]));
  const body = await serialize(options, headers);
  const path = target.pathname + target.search;
  const reply = await page.evaluate(async ([verb, address, pairs, payload]) => {
    const scope = window as unknown as { __savetoolkitApiDispatch?: (...args: unknown[]) => Promise<{ status: number; headers: [string, string][]; body: string }> };
    if (!scope.__savetoolkitApiDispatch) throw new Error("The API hooks were installed after this page loaded.");
    return await scope.__savetoolkitApiDispatch(verb, address, pairs, payload);
  }, [method, path, Object.entries(headers), body?.toString("base64") ?? null] as const);
  const request = new ApiRequest({ method, url: new URL(page.url()).origin + path, headers, body });
  return new ApiResponse(request, reply.status, reply.headers, reply.body);
}

async function serialize(options: ApiRequestOptions, headers: Record<string, string>): Promise<Buffer | null> {
  if (options.multipart) {
    const form = new FormData();
    for (const [name, value] of Object.entries(options.multipart)) {
      if (typeof value === "string") form.append(name, value);
      else form.append(name, new Blob([new Uint8Array(value.buffer)], { type: value.mimeType }), value.name);
    }
    const encoded = new Request("http://savetoolkit.local/", { method: "POST", body: form });
    headers["content-type"] = encoded.headers.get("content-type")!;
    return Buffer.from(await encoded.arrayBuffer());
  }
  if (options.data === undefined) return null;
  if (typeof options.data === "string" || Buffer.isBuffer(options.data)) {
    headers["content-type"] ??= "application/octet-stream";
    return Buffer.from(options.data);
  }
  headers["content-type"] ??= "application/json";
  return Buffer.from(JSON.stringify(options.data));
}
