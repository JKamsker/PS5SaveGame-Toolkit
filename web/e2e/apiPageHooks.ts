/**
 * Runs in the page before any app code (page.addInitScript), so it must stay self-contained: it wraps
 * `Worker` to route the .NET runtime's `/api` request/response messages through the
 * `__savetoolkitApiHook` binding (see apiRoutes.ts), and exposes `__savetoolkitApiDispatch` so tests can
 * send their own requests to the same runtime, in the same queue, without routing (see apiRequest.ts).
 */
// eslint-disable-next-line max-lines-per-function -- Serialized into the page; it cannot import helpers.
export function pageHooks() {
  type Pairs = [string, string][];
  type Decision = { kind: "continue" | "fetch" | "abort" | "fulfill"; errorText?: string; status?: number; headers?: Pairs; body?: string };
  type Reply = { type: "response"; id: number; status: number; headers: Pairs; frame: ArrayBuffer; bodyOffset: number } | { type: "error"; id: number; message: string };
  interface Wire { id: number; method: string; url: string; headers: Pairs; body: ArrayBuffer | null }
  interface Entry { token: string; cancelled: boolean; forwarded: boolean; fetched?: (reply: Reply) => void }
  const scope = window as unknown as {
    __savetoolkitApiHook: (message: unknown) => Promise<Decision | null>;
    __savetoolkitApiDispatch: (method: string, url: string, headers: Pairs, body: string | null) => Promise<{ status: number; headers: Pairs; body: string }>;
  };
  const hook = (message: unknown) => scope.__savetoolkitApiHook(message);
  const encode = (buffer: ArrayBuffer | null) => {
    if (!buffer) return null;
    let text = "";
    const bytes = new Uint8Array(buffer);
    for (let i = 0; i < bytes.length; i += 0x8000) text += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
    return btoa(text);
  };
  const decode = (text: string) => Uint8Array.from(atob(text), char => char.charCodeAt(0)).buffer;
  const bodyOf = (reply: Reply & { type: "response" }) => encode(reply.frame.slice(reply.bodyOffset)) ?? "";
  // Tokens stay unique across reloads, which start a new realm with its own request ids.
  const realm = Math.random().toString(36).slice(2);
  const Native = window.Worker;
  let tokens = 0;
  let directIds = 0;
  let current: { dispatch: (request: Omit<Wire, "id">) => Promise<Reply> } | null = null;
  scope.__savetoolkitApiDispatch = async (method, url, headers, body) => {
    if (!current) throw new Error("The app has not started its runtime worker yet.");
    const reply = await current.dispatch({ method, url, headers, body: body === null ? null : decode(body) });
    if (reply.type === "error") throw new Error(`The runtime failed ${method} ${url}: ${reply.message}`);
    return { status: reply.status, headers: reply.headers, body: bodyOf(reply) };
  };

  window.Worker = class extends Native {
    private listener: ((event: MessageEvent) => void) | null = null;
    private readonly requests = new Map<number, Entry>();
    private readonly direct = new Map<number, (reply: Reply) => void>();
    constructor(url: string | URL, options?: WorkerOptions) {
      super(url, options);
      super.onmessage = event => this.receive(event);
      current = { dispatch: request => this.dispatch(request) };
    }
    override set onmessage(listener: ((event: MessageEvent) => void) | null) { this.listener = listener; }
    override get onmessage() { return this.listener; }

    /** Test-owned request: negative ids never collide with the app's, and nothing observes or routes it. */
    dispatch(request: Omit<Wire, "id">) {
      const id = -++directIds;
      return new Promise<Reply>(resolve => {
        this.direct.set(id, resolve);
        super.postMessage({ type: "request", request: { ...request, id } }, request.body ? [request.body] : []);
      });
    }

    override postMessage(message: unknown, transfer?: Transferable[] | StructuredSerializeOptions) {
      const data = message as { type?: string; id?: number; request?: Wire };
      if (data.type === "cancel" && data.id !== undefined && this.requests.has(data.id)) return this.cancel(data.id, message);
      if (data.type !== "request" || !data.request) return super.postMessage(message, transfer as Transferable[]);
      void this.route(data.request, message, transfer as Transferable[]);
    }

    /** The app aborted the request: report it like a browser would, and keep a test-held fetch alive. */
    private cancel(id: number, message: unknown) {
      const entry = this.requests.get(id)!;
      entry.cancelled = true;
      void hook({ phase: "failed", token: entry.token, errorText: "net::ERR_ABORTED" });
      if (!entry.forwarded || entry.fetched) return;
      this.requests.delete(id);
      super.postMessage(message);
    }

    private async route(request: Wire, message: unknown, transfer: Transferable[]) {
      const entry: Entry = { token: `${realm}:${++tokens}`, cancelled: false, forwarded: false };
      this.requests.set(request.id, entry);
      const decision = await hook({ phase: "request", token: entry.token, method: request.method, url: request.url,
        headers: request.headers, body: encode(request.body) });
      if (decision?.kind === "fetch") return this.fetch(request.id, entry, message, transfer);
      if (entry.cancelled) return this.requests.delete(request.id);
      if (!decision || decision.kind === "continue") {
        entry.forwarded = true;
        return super.postMessage(message, transfer);
      }
      this.settle(request.id, decision);
    }

    private async fetch(id: number, entry: Entry, message: unknown, transfer: Transferable[]) {
      const real = new Promise<Reply>(resolve => { entry.fetched = resolve; });
      entry.forwarded = true;
      super.postMessage(message, transfer);
      const reply = await real;
      const response = reply.type === "response"
        ? { status: reply.status, headers: reply.headers, body: bodyOf(reply) } : { status: 0, headers: [], body: "" };
      const decision = await hook({ phase: "fetched", token: entry.token, response });
      if (entry.cancelled) return this.requests.delete(id);
      if (!decision || decision.kind === "continue") return this.deliver(reply);
      this.settle(id, decision);
    }

    private settle(id: number, decision: Decision) {
      if (decision.kind === "abort") return this.deliver({ type: "error", id, message: "Failed to fetch" }, decision.errorText);
      this.deliver({ type: "response", id, status: decision.status!, headers: decision.headers!, frame: decode(decision.body ?? ""), bodyOffset: 0 });
    }

    private deliver(reply: Reply, errorText = "net::ERR_FAILED") {
      const entry = this.requests.get(reply.id);
      this.requests.delete(reply.id);
      if (entry?.cancelled) return;
      if (entry) {
        void hook(reply.type === "error" ? { phase: "failed", token: entry.token, errorText }
          : { phase: "response", token: entry.token, status: reply.status, headers: reply.headers, body: bodyOf(reply) });
      }
      this.listener?.(new MessageEvent("message", { data: reply }));
    }

    private receive(event: MessageEvent) {
      const data = event.data as Reply | { type: string; id?: undefined };
      if (data.type !== "response" && data.type !== "error") return this.listener?.(event);
      const reply = data as Reply;
      const direct = this.direct.get(reply.id);
      if (direct) {
        this.direct.delete(reply.id);
        return direct(reply);
      }
      const entry = this.requests.get(reply.id);
      if (entry?.fetched) return entry.fetched(reply);
      if (entry) return this.deliver(reply);
      this.listener?.(event);
    }
  };
}
