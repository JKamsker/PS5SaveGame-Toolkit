import type { Page } from "@playwright/test";

/**
 * The app answers /api inside a Web Worker (the .NET runtime), so Playwright's network APIs
 * (`page.route`, `waitForResponse`) never see those requests. These helpers observe and intercept the
 * runtime's request/response messages instead, with page.route-like semantics:
 * `fulfill` and `abort` answer without running the request; `fetch` runs it and lets the test hold
 * or replace the result; `fallback` defers to an earlier route; `continue` runs it normally.
 * Install them before the first navigation (helpers.home does this).
 */

export interface ApiRequest { method: string; url: string; headers: Record<string, string>; postData: string | null }
export interface ApiResponse { status: number; url: string; method: string; headers: Record<string, string>; body: Buffer; text(): string; json(): unknown }
interface WireResponse { status: number; headers: [string, string][]; body: string }
type Action = { kind: "continue" } | { kind: "fallback" } | { kind: "abort" } | { kind: "fetch" } | ({ kind: "fulfill" } & WireResponse);
interface Fulfill { status?: number; contentType?: string; body?: string | Buffer; json?: unknown; headers?: Record<string, string>; response?: ApiResponse }
type Message =
  | { phase: "request"; token: number; method: string; url: string; headers: [string, string][]; body: string | null }
  | { phase: "fetched"; token: number; response: WireResponse }
  | { phase: "response"; method: string; url: string; status: number };

export class ApiRoute {
  private decide!: (action: Action) => void;
  private decided: Promise<Action> = this.reset();
  private fetched: ((response: ApiResponse) => void) | null = null;
  constructor(private readonly info: ApiRequest) {}
  request() { return { ...this.info, method: () => this.info.method, url: () => this.info.url, postDataJSON: () => JSON.parse(this.info.postData ?? "null") as unknown }; }
  async fetch(): Promise<ApiResponse> {
    const response = new Promise<ApiResponse>(resolve => { this.fetched = resolve; });
    this.settle({ kind: "fetch" });
    return response;
  }
  async fulfill(options: Fulfill = {}) {
    if (options.response) return this.settle({ kind: "continue" });
    const body = options.json !== undefined ? JSON.stringify(options.json) : options.body ?? "";
    const type = options.contentType ?? (options.json !== undefined ? "application/json" : "text/plain");
    const headers = Object.entries({ "content-type": type, ...options.headers });
    this.settle({ kind: "fulfill", status: options.status ?? 200, headers, body: Buffer.from(body).toString("base64") });
  }
  async abort() { this.settle({ kind: "abort" }); }
  async continue() { this.settle({ kind: "continue" }); }
  async fallback() { this.settle({ kind: "fallback" }); }
  /** @internal */ next(): Promise<Action> { return this.decided; }
  /** @internal */ deliver(response: WireResponse, method: string) {
    const body = Buffer.from(response.body, "base64");
    const headers = Object.fromEntries(response.headers.map(([name, value]) => [name.toLowerCase(), value]));
    this.decided = this.reset();
    this.fetched?.({ status: response.status, url: this.info.url, method, headers, body, text: () => body.toString("utf8"), json: () => JSON.parse(body.toString("utf8")) as unknown });
  }
  private reset() { return new Promise<Action>(resolve => { this.decide = resolve; }); }
  private settle(action: Action) { this.decide(action); }
}

type Handler = (route: ApiRoute) => unknown;
interface Rule { matches: (url: string) => boolean; handler: Handler; times: number }
interface Hooks { rules: Rule[]; routes: Map<number, ApiRoute>; listeners: Set<(response: { method: string; url: string; status: number }) => void> }
const installed = new WeakMap<Page, Hooks>();

export async function installApiHooks(page: Page): Promise<void> {
  if (installed.has(page)) return;
  const hooks: Hooks = { rules: [], routes: new Map(), listeners: new Set() };
  installed.set(page, hooks);
  await page.exposeBinding("__savetoolkitApiHook", async ({ page: source }, message: Message) => {
    const origin = new URL(source.url()).origin;
    if (message.phase === "response") {
      for (const listener of [...hooks.listeners]) listener({ ...message, url: origin + message.url });
      return null;
    }
    if (message.phase === "fetched") {
      const route = hooks.routes.get(message.token)!;
      route.deliver(message.response, route.request().method());
      return await finish(hooks, message.token, route);
    }
    const info: ApiRequest = { method: message.method, url: origin + message.url, postData: message.body === null ? null : Buffer.from(message.body, "base64").toString("utf8"),
      headers: Object.fromEntries(message.headers) };
    for (const rule of [...hooks.rules].reverse()) {
      if (rule.times === 0 || !rule.matches(info.url)) continue;
      rule.times--;
      const route = new ApiRoute(info);
      hooks.routes.set(message.token, route);
      void Promise.resolve(rule.handler(route)).catch(() => route.continue());
      const action = await route.next();
      if (action.kind === "fallback") continue;
      if (action.kind !== "fetch") hooks.routes.delete(message.token);
      return action;
    }
    return { kind: "continue" } satisfies Action;
  });
  await page.addInitScript(pageHooks);
}

async function finish(hooks: Hooks, token: number, route: ApiRoute): Promise<Action> {
  const action = await route.next();
  hooks.routes.delete(token);
  return action.kind === "fallback" ? { kind: "continue" } : action;
}

/** page.route for the in-browser API. Glob (`**`, `*`) or RegExp, matched against the absolute URL. */
export async function routeApi(page: Page, pattern: string | RegExp, handler: Handler, options: { times?: number } = {}) {
  await installApiHooks(page);
  const regex = typeof pattern === "string" ? globToRegex(pattern) : pattern;
  const rule: Rule = { matches: url => regex.test(url), handler, times: options.times ?? -1 };
  installed.get(page)!.rules.push(rule);
  return () => { const rules = installed.get(page)!.rules; rules.splice(rules.indexOf(rule), 1); };
}

export function unrouteAllApi(page: Page) {
  installed.get(page)?.rules.splice(0);
}

/** Resolves with the next completed API response matching `predicate` (cf. page.waitForResponse). */
export function waitForApiResponse(page: Page, predicate: (response: { method: string; url: string; status: number }) => boolean,
  timeout = 30_000): Promise<{ method: string; url: string; status: number }> {
  const hooks = installed.get(page);
  if (!hooks) throw new Error("Install the API hooks before navigation (helpers.home).");
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => { hooks.listeners.delete(listener); reject(new Error("Timed out waiting for an API response.")); }, timeout);
    const listener = (response: { method: string; url: string; status: number }) => {
      if (!predicate(response)) return;
      clearTimeout(timer);
      hooks.listeners.delete(listener);
      resolve(response);
    };
    hooks.listeners.add(listener);
  });
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

/** Runs in the page before any app code: wraps Worker to route the runtime's messages through the hook binding. */
// eslint-disable-next-line max-lines-per-function -- Serialized into the page by addInitScript, so it must stay self-contained.
function pageHooks() {
  type Hook = (message: unknown) => Promise<{ kind: string; status?: number; headers?: [string, string][]; body?: string } | null>;
  const hook = (message: unknown) => (window as unknown as { __savetoolkitApiHook: Hook }).__savetoolkitApiHook(message);
  const encode = (buffer: ArrayBuffer | null) => {
    if (!buffer) return null;
    let text = "";
    const bytes = new Uint8Array(buffer);
    for (let i = 0; i < bytes.length; i += 0x8000) text += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
    return btoa(text);
  };
  const decode = (text: string) => Uint8Array.from(atob(text), char => char.charCodeAt(0)).buffer;
  const Native = window.Worker;
  let tokens = 0;
  window.Worker = class extends Native {
    private listener: ((event: MessageEvent) => void) | null = null;
    private readonly requests = new Map<number, { method: string; url: string; fetched?: (data: { status: number; headers: [string, string][]; frame: ArrayBuffer; bodyOffset: number } | null) => void }>();
    constructor(url: string | URL, options?: WorkerOptions) {
      super(url, options);
      super.onmessage = event => this.receive(event);
    }
    override set onmessage(listener: ((event: MessageEvent) => void) | null) { this.listener = listener; }
    override get onmessage() { return this.listener; }
    override postMessage(message: unknown, transfer?: Transferable[] | StructuredSerializeOptions) {
      const request = (message as { type?: string; request?: { id: number; method: string; url: string; headers: [string, string][]; body: ArrayBuffer | null } });
      if (request.type !== "request" || !request.request) return super.postMessage(message, transfer as Transferable[]);
      void this.route(request.request, message, transfer as Transferable[]);
    }
    private async route(request: { id: number; method: string; url: string; headers: [string, string][]; body: ArrayBuffer | null }, message: unknown, transfer: Transferable[]) {
      const token = ++tokens;
      this.requests.set(request.id, { method: request.method, url: request.url });
      const action = await hook({ phase: "request", token, method: request.method, url: request.url, headers: request.headers, body: encode(request.body) });
      if (!action || action.kind === "continue") return super.postMessage(message, transfer);
      if (action.kind === "fetch") {
        const entry = this.requests.get(request.id)!;
        const fetched = new Promise<{ status: number; headers: [string, string][]; frame: ArrayBuffer; bodyOffset: number } | null>(resolve => { entry.fetched = resolve; });
        super.postMessage(message, transfer);
        const real = await fetched;
        if (!real) {
          // The runtime itself failed: report it to the handler, then surface the failure unchanged.
          await hook({ phase: "fetched", token, response: { status: 0, headers: [], body: "" } });
          return this.deliver({ type: "error", id: request.id, message: "Failed to fetch" });
        }
        const body = encode(real.frame.slice(real.bodyOffset));
        const final = await hook({ phase: "fetched", token, response: { status: real.status, headers: real.headers, body } });
        if (!final || final.kind === "continue") return this.deliver({ type: "response", id: request.id, ...real });
        return this.synthetic(request.id, final);
      }
      return this.synthetic(request.id, action);
    }
    private synthetic(id: number, action: { kind: string; status?: number; headers?: [string, string][]; body?: string }) {
      if (action.kind === "abort") return this.deliver({ type: "error", id, message: "Failed to fetch" });
      this.deliver({ type: "response", id, status: action.status, headers: action.headers, frame: decode(action.body ?? ""), bodyOffset: 0 });
    }
    private deliver(data: { type: string; id: number; status?: number } & Record<string, unknown>) {
      const request = this.requests.get(data.id);
      this.requests.delete(data.id);
      if (request) void hook({ phase: "response", method: request.method, url: request.url, status: data.type === "error" ? 0 : data.status });
      this.listener?.(new MessageEvent("message", { data }));
    }
    private receive(event: MessageEvent) {
      const data = event.data as { type: string; id: number; status: number; headers: [string, string][]; frame: ArrayBuffer; bodyOffset: number };
      const request = (data.type === "response" || data.type === "error") ? this.requests.get(data.id) : undefined;
      if (request?.fetched) return request.fetched(data.type === "response" ? data : null);
      // Keep the runtime's own error text for unrouted failures.
      if (request) return this.deliver(data);
      this.listener?.(event);
    }
  };
}
