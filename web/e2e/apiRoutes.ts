import type { Page } from "@playwright/test";
import { pageHooks } from "./apiPageHooks";

/**
 * The app answers /api inside a Web Worker (the .NET runtime), so Playwright's network APIs
 * (`page.route`, `waitForResponse`, `page.on("request")`, `page.request`) never see those requests.
 * These helpers observe and intercept the runtime's request/response messages instead, with the same
 * shapes and semantics as Playwright's: `fulfill` and `abort` answer without running the request;
 * `fetch` runs it and lets the test hold or replace the result; `fallback` defers to an earlier route;
 * `continue` runs it normally. Install them before the first navigation (helpers.home does this).
 */

type Pairs = [string, string][];
interface Wire { status: number; headers: Pairs; body: string }
type Decision = { kind: "continue" } | { kind: "fallback" } | { kind: "abort"; errorText: string } | { kind: "fetch" } | ({ kind: "fulfill" } & Wire);
type Message =
  | { phase: "request"; token: string; method: string; url: string; headers: Pairs; body: string | null }
  | { phase: "fetched"; token: string; response: Wire }
  | ({ phase: "response"; token: string } & Wire)
  | { phase: "failed"; token: string; errorText: string };
/** Matches Playwright's loosely typed `json()` / `postDataJSON()`, so specs can keep reading fields directly. */
// eslint-disable-next-line @typescript-eslint/no-explicit-any -- Same contract as Playwright's Response.json().
type Json = any;

export class ApiRequest {
  private failed: string | null = null;
  constructor(private readonly info: { method: string; url: string; headers: Record<string, string>; body: Buffer | null }) {}
  method() { return this.info.method; }
  url() { return this.info.url; }
  headers() { return { ...this.info.headers }; }
  postDataBuffer() { return this.info.body; }
  postData() { return this.info.body?.toString("utf8") ?? null; }
  postDataJSON(): Json { return this.info.body ? JSON.parse(this.info.body.toString("utf8")) : null; }
  failure() { return this.failed === null ? null : { errorText: this.failed }; }
  /** @internal */ fail(errorText: string) { this.failed = errorText; }
}

export class ApiResponse {
  private readonly bytes: Buffer;
  private readonly fields: Record<string, string>;
  constructor(private readonly origin: ApiRequest, private readonly code: number, headers: Pairs, body: string | Buffer) {
    this.bytes = typeof body === "string" ? Buffer.from(body, "base64") : body;
    this.fields = Object.fromEntries(headers.map(([name, value]) => [name.toLowerCase(), value]));
  }
  url() { return this.origin.url(); }
  request() { return this.origin; }
  status() { return this.code; }
  ok() { return this.code >= 200 && this.code < 300; }
  headers() { return { ...this.fields }; }
  async body() { return Buffer.from(this.bytes); }
  async text() { return this.bytes.toString("utf8"); }
  async json(): Promise<Json> { return JSON.parse(this.bytes.toString("utf8")); }
  /** The runtime hands over complete bodies, so a response is always finished. */
  async finished() { return null; }
  /** @internal */ wire(): Wire { return { status: this.code, headers: Object.entries(this.fields), body: this.bytes.toString("base64") }; }
}

interface Fulfill { status?: number; contentType?: string; body?: string | Buffer; json?: unknown; headers?: Record<string, string>; response?: ApiResponse }

export class ApiRoute {
  private decide!: (decision: Decision) => void;
  private decided: Promise<Decision> = this.reset();
  private fetched: ((response: ApiResponse) => void) | null = null;
  constructor(private readonly info: ApiRequest) {}
  request() { return this.info; }
  async fetch(): Promise<ApiResponse> {
    const response = new Promise<ApiResponse>(resolve => { this.fetched = resolve; });
    this.decide({ kind: "fetch" });
    return response;
  }
  async fulfill(options: Fulfill = {}) {
    const { response } = options;
    if (response && options.json === undefined && options.body === undefined && !options.status && !options.headers && !options.contentType)
      return this.decide({ kind: "continue" });
    const base = response?.wire() ?? { status: 200, headers: [], body: "" };
    const replaced = options.json !== undefined || options.body !== undefined;
    const body = options.json !== undefined ? Buffer.from(JSON.stringify(options.json))
      : options.body !== undefined ? Buffer.from(options.body) : Buffer.from(base.body, "base64");
    const type = options.contentType ?? (options.json !== undefined ? "application/json" : replaced || !response ? "text/plain" : undefined);
    const headers = { ...Object.fromEntries(base.headers.filter(([name]) => name !== "content-length")), ...(type ? { "content-type": type } : {}), ...options.headers };
    this.decide({ kind: "fulfill", status: options.status ?? base.status, headers: Object.entries(headers), body: body.toString("base64") });
  }
  /** Playwright error codes: "failed" (default), "aborted", "timedout", ... */
  async abort(errorCode = "failed") { this.decide({ kind: "abort", errorText: `net::ERR_${errorCode.toUpperCase()}` }); }
  async continue() { this.decide({ kind: "continue" }); }
  async fallback() { this.decide({ kind: "fallback" }); }
  /** @internal */ next(): Promise<Decision> { return this.decided; }
  /** @internal */ deliver(response: ApiResponse) {
    this.decided = this.reset();
    this.fetched?.(response);
  }
  private reset() { return new Promise<Decision>(resolve => { this.decide = resolve; }); }
}

type Handler = (route: ApiRoute) => unknown;
interface Rule { pattern: string | RegExp; matches: (url: string) => boolean; handler: Handler; times: number }
export interface ApiEvents { request: ApiRequest; response: ApiResponse; requestfailed: ApiRequest; requestfinished: ApiRequest }
type Listener = (event: keyof ApiEvents, value: ApiEvents[keyof ApiEvents]) => void;
interface Hooks { rules: Rule[]; routes: Map<string, ApiRoute>; requests: Map<string, ApiRequest>; listeners: Set<Listener> }
const installed = new WeakMap<Page, Hooks>();

export async function installApiHooks(page: Page): Promise<void> {
  if (installed.has(page)) return;
  const hooks: Hooks = { rules: [], routes: new Map(), requests: new Map(), listeners: new Set() };
  installed.set(page, hooks);
  const emit = <K extends keyof ApiEvents>(event: K, value: ApiEvents[K]) => { for (const listener of [...hooks.listeners]) listener(event, value); };
  await page.exposeBinding("__savetoolkitApiHook", async ({ page: source }, message: Message) => {
    if (message.phase === "request") {
      const request = new ApiRequest({ method: message.method, url: new URL(source.url()).origin + message.url,
        headers: Object.fromEntries(message.headers.map(([name, value]) => [name.toLowerCase(), value])),
        body: message.body === null ? null : Buffer.from(message.body, "base64") });
      hooks.requests.set(message.token, request);
      emit("request", request);
      return await decide(hooks, message.token, request);
    }
    if (message.phase === "fetched") {
      // The page may have aborted the request meanwhile; the test still receives what it fetched.
      const route = hooks.routes.get(message.token)!;
      route.deliver(new ApiResponse(route.request(), message.response.status, message.response.headers, message.response.body));
      const decision = await route.next();
      hooks.routes.delete(message.token);
      return decision.kind === "fallback" ? { kind: "continue" } : decision;
    }
    const request = hooks.requests.get(message.token);
    if (!request) return null;
    hooks.requests.delete(message.token);
    if (message.phase === "failed") {
      request.fail(message.errorText);
      emit("requestfailed", request);
    } else {
      emit("response", new ApiResponse(request, message.status, message.headers, message.body));
      emit("requestfinished", request);
    }
    return null;
  });
  await page.addInitScript(pageHooks);
}

async function decide(hooks: Hooks, token: string, request: ApiRequest): Promise<Decision> {
  for (const rule of [...hooks.rules].reverse()) {
    if (rule.times === 0 || !rule.matches(request.url())) continue;
    rule.times--;
    const route = new ApiRoute(request);
    hooks.routes.set(token, route);
    void Promise.resolve(rule.handler(route)).catch(() => route.continue());
    const decision = await route.next();
    if (decision.kind === "fallback") continue;
    if (decision.kind !== "fetch") hooks.routes.delete(token);
    return decision;
  }
  return { kind: "continue" };
}

function hooksOf(page: Page) {
  const hooks = installed.get(page);
  if (!hooks) throw new Error("Install the API hooks before navigation (installApiHooks or helpers.home).");
  return hooks;
}

/** page.route for the in-browser API. Glob (`**`, `*`; `?` is literal) or RegExp, matched against the absolute URL. */
export async function routeApi(page: Page, pattern: string | RegExp, handler: Handler, options: { times?: number } = {}) {
  await installApiHooks(page);
  const regex = typeof pattern === "string" ? globToRegex(pattern) : pattern;
  const rule: Rule = { pattern, matches: url => regex.test(url), handler, times: options.times ?? -1 };
  hooksOf(page).rules.push(rule);
  return () => { const rules = hooksOf(page).rules; if (rules.includes(rule)) rules.splice(rules.indexOf(rule), 1); };
}

/** page.unroute: removes every route registered with this exact pattern. */
export function unrouteApi(page: Page, pattern: string | RegExp) {
  const hooks = hooksOf(page);
  const same = (rule: Rule) => typeof pattern === "string" ? rule.pattern === pattern : rule.pattern instanceof RegExp && rule.pattern.source === pattern.source;
  hooks.rules = hooks.rules.filter(rule => !same(rule));
}

export function unrouteAllApi(page: Page) {
  installed.get(page)?.rules.splice(0);
}

/** page.on for the in-browser API's request lifecycle. Returns a function that removes the listener. */
export function onApi<K extends keyof ApiEvents>(page: Page, event: K, listener: (value: ApiEvents[K]) => void): () => void {
  const hooks = hooksOf(page);
  const filtered: Listener = (name, value) => { if (name === event) listener(value as ApiEvents[K]); };
  hooks.listeners.add(filtered);
  return () => { hooks.listeners.delete(filtered); };
}

/** page.waitForEvent for the in-browser API ("request", "response", "requestfailed", "requestfinished"). */
export function waitForApi<K extends keyof ApiEvents>(page: Page, event: K, predicate: (value: ApiEvents[K]) => boolean,
  timeout = 30_000): Promise<ApiEvents[K]> {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => { stop(); reject(new Error(`Timed out waiting for an API ${event} event.`)); }, timeout);
    const stop = onApi(page, event, value => {
      if (!predicate(value)) return;
      clearTimeout(timer);
      stop();
      resolve(value);
    });
  });
}

/** page.waitForResponse for the in-browser API. */
export function waitForApiResponse(page: Page, predicate: (response: ApiResponse) => boolean, timeout?: number) {
  return waitForApi(page, "response", predicate, timeout);
}

/** page.waitForRequest for the in-browser API. */
export function waitForApiRequest(page: Page, predicate: (request: ApiRequest) => boolean, timeout?: number) {
  return waitForApi(page, "request", predicate, timeout);
}

function globToRegex(glob: string): RegExp {
  let source = "";
  for (let i = 0; i < glob.length; i++) {
    const char = glob[i]!;
    if (char === "*" && glob[i + 1] === "*") { source += ".*"; i++; }
    else if (char === "*") source += "[^/]*";
    else source += char.replace(/[.+?^${}()|[\]\\]/g, "\\$&");
  }
  return new RegExp(`^${source}$`);
}
