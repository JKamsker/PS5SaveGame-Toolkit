/**
 * Runs in the page before any app code (page.addInitScript), so it must stay self-contained: it wraps
 * `Worker` to route the .NET runtime's `/api` request/response messages through the
 * `__savetoolkitApiHook` binding (see apiRoutes.ts), and exposes `__savetoolkitApiDispatch` so tests can
 * send their own requests to the same runtime, in the same queue, without routing (see apiRequest.ts).
 */
// eslint-disable-next-line max-lines-per-function -- Serialized into the page; it cannot import helpers.
export function pageHooks() {
  type Pairs = [string, string][];
  type Decision = { kind: "continue" | "fetch" | "abort" | "fulfill" | "hold"; errorText?: string; status?: number; headers?: Pairs; body?: string };
  type Reply = { type: "response"; id: number; status: number; headers: Pairs; frame: ArrayBuffer; bodyOffset: number } | { type: "error"; id: number; message: string };
  interface Wire { id: number; method: string; url: string; headers: Pairs; body: ArrayBuffer | null }
  interface Entry {
    token: string; label: string; cancelled: boolean; forwarded: boolean; started: boolean; fetched?: (reply: Reply) => void;
    /**
     * Settles once the app has its answer (or will never get one: cancelled, or the runtime stopped), or a
     * test holds the request deliberately (see `idle`).
     */
    done: boolean; finished: Promise<void>; release: () => void;
  }
  /** A direct request waits for the app to stay idle this long, and at least this many event-loop turns. */
  const QUIET_MS = 50;
  const QUIET_TURNS = 3;
  /** A direct request gives up (instead of hanging until the test's timeout) when the app is never idle. */
  const IDLE_LIMIT_MS = 20_000;
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
    /** Set once the runtime reported that it stopped (or the worker died): it answers nothing more. */
    private dead: string | null = null;
    /** How many requests the app has sent; tells `idle` whether the app sent another one meanwhile. */
    private issued = 0;
    constructor(url: string | URL, options?: WorkerOptions) {
      super(url, options);
      super.onmessage = event => this.receive(event);
      // A worker that fails to load or throws at top level: runtime/client.ts fails its pending requests.
      this.addEventListener("error", event => this.stopped(event.message || "The runtime worker failed."));
      current = { dispatch: request => this.dispatch(request) };
    }
    override set onmessage(listener: ((event: MessageEvent) => void) | null) { this.listener = listener; }
    override get onmessage() { return this.listener; }

    /**
     * Test-owned request: negative ids never collide with the app's, and nothing observes or routes it.
     * It is sent once the app is idle (see `idle`), so it joins the worker's queue behind every request the
     * app already sent or queued, and a test's read observes the app's earlier writes.
     */
    async dispatch(request: Omit<Wire, "id">) {
      await this.idle();
      return this.post(request);
    }

    /**
     * Waits until the app is idle: every request it sent has its answer, and it sent nothing new for
     * QUIET_TURNS event-loop turns and QUIET_MS. Waiting only for the requests already handed to the
     * worker would not do: the app queues its own writes (a document's edits run one after another, each
     * sent once the previous answer was handled, web/src/components/document/useDocumentSession.ts), and
     * serializes a request body before posting it, so a read sent then would overtake those writes and
     * observe stale state. The app sends its next queued request within microtasks of handling an answer,
     * which a quiet period of several turns outlasts. A request a route handler holds without deciding (or
     * holds after route.fetch()) delays the read, like it would delay the app.
     */
    private async idle() {
      const limit = performance.now() + IDLE_LIMIT_MS;
      let seen = -1;
      let since = 0;
      let turns = 0;
      for (;;) {
        const busy = [...this.requests.values()].filter(entry => !entry.done);
        if (busy.length) {
          const left = limit - performance.now();
          if (left <= 0) {
            throw new Error(`The app never became idle, so this test request was not sent; still waiting for: ${
              busy.map(entry => entry.label).join(", ")}`);
          }
          await Promise.race([Promise.all(busy.map(entry => entry.finished)), new Promise(resolve => setTimeout(resolve, left))]);
          seen = -1;
          continue;
        }
        if (seen !== this.issued) {
          seen = this.issued;
          since = performance.now();
          turns = 0;
        } else if (turns >= QUIET_TURNS && performance.now() - since >= QUIET_MS) return;
        await new Promise(resolve => setTimeout(resolve, 0));
        turns++;
      }
    }

    private post(request: Omit<Wire, "id">) {
      const id = -++directIds;
      return new Promise<Reply>(resolve => {
        if (this.dead) return resolve({ type: "error", id, message: this.dead });
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

    /**
     * The app aborted the request: report it like a browser would, and forward the cancel exactly as the app
     * sent it, so the worker drops the request if it has not started it (the real app's semantics). A test
     * holding the request with route.fetch() then learns that it never ran (the fetch rejects).
     */
    private cancel(id: number, message: unknown) {
      const entry = this.requests.get(id)!;
      entry.cancelled = true;
      entry.release();
      void hook({ phase: "failed", token: entry.token, errorText: "net::ERR_ABORTED" });
      if (!entry.forwarded) return;
      if (!entry.fetched) this.requests.delete(id);
      super.postMessage(message);
      if (entry.fetched && !entry.started) void this.whenDropped(id, entry);
    }

    /**
     * The worker drops a cancelled request silently when its turn comes. A probe queued behind the cancel
     * answers after that turn: if the request had not started by then, it never will.
     */
    private async whenDropped(id: number, entry: Entry) {
      await this.post({ method: "GET", url: "/api/version", headers: [], body: null });
      if (!entry.started) entry.fetched?.({ type: "error", id, message: "The request was cancelled before the runtime started it." });
    }

    private async route(request: Wire, message: unknown, transfer: Transferable[]) {
      let resolve!: () => void;
      const finished = new Promise<void>(settled => { resolve = settled; });
      const entry: Entry = { token: `${realm}:${++tokens}`, label: `${request.method} ${request.url}`, cancelled: false,
        forwarded: false, started: false, done: false, finished, release: () => { entry.done = true; resolve(); } };
      this.issued++;
      this.requests.set(request.id, entry);
      const decision = await hook({ phase: "request", token: entry.token, method: request.method, url: request.url,
        headers: request.headers, body: encode(request.body) });
      if (this.dead) return this.drop(request.id, entry);
      if (decision?.kind === "fetch") return this.fetch(request.id, entry, message, transfer);
      if (entry.cancelled) return this.requests.delete(request.id);
      // The test stopped the runtime for this request (apiRoutes' crash): it stays pending until the
      // runtime's failure reaches runtime/client.ts, which rejects it with every other pending request.
      if (decision?.kind === "hold") return entry.release();
      if (!decision || decision.kind === "continue") {
        entry.forwarded = true;
        return super.postMessage(message, transfer);
      }
      this.settle(request.id, decision);
    }

    private async fetch(id: number, entry: Entry, message: unknown, transfer: Transferable[]) {
      const real = new Promise<Reply>(resolve => { entry.fetched = resolve; });
      entry.forwarded = true;
      // Cancelled while the test decided: the real app's request would have been dropped from the queue.
      if (entry.cancelled) entry.fetched!({ type: "error", id, message: "The request was cancelled before the runtime started it." });
      else super.postMessage(message, transfer);
      const reply = await real;
      // Answered: from here on this request is no longer waiting for the worker.
      entry.fetched = undefined;
      const response = reply.type === "response"
        ? { status: reply.status, headers: reply.headers, body: bodyOf(reply) } : { status: 0, headers: [], body: "", error: reply.message };
      const decision = await hook({ phase: "fetched", token: entry.token, response });
      if (this.dead) return this.drop(id, entry);
      if (entry.cancelled) return this.requests.delete(id);
      if (decision?.kind === "hold") return entry.release();
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
      entry?.release();
      if (entry?.cancelled) return;
      if (entry) {
        void hook(reply.type === "error" ? { phase: "failed", token: entry.token, errorText }
          : { phase: "response", token: entry.token, status: reply.status, headers: reply.headers, body: bodyOf(reply) });
      }
      this.listener?.(new MessageEvent("message", { data: reply }));
    }

    /** A request the stopped runtime will never answer: runtime/client.ts rejects it, so report it failed. */
    private drop(id: number, entry: Entry) {
      entry.release();
      if (!this.requests.delete(id)) return;
      if (!entry.cancelled) void hook({ phase: "failed", token: entry.token, errorText: `net::ERR_FAILED (runtime stopped: ${this.dead})` });
    }

    /** The runtime stopped: every request it had not answered fails (runtime/client.ts `fail`). */
    private stopped(reason: string) {
      if (this.dead) return;
      this.dead = reason;
      for (const [id, entry] of [...this.requests]) {
        // A held fetch learns that the request failed (route.fetch rejects), then reports it as dropped.
        if (entry.fetched) entry.fetched({ type: "error", id, message: reason });
        else this.drop(id, entry);
      }
      for (const [id, resolve] of [...this.direct]) resolve({ type: "error", id, message: reason });
      this.direct.clear();
    }

    private receive(event: MessageEvent) {
      const data = event.data as Reply | { type: string; id?: number; message?: string };
      if (data.type === "started" && data.id !== undefined) {
        const entry = this.requests.get(data.id);
        if (entry) entry.started = true;
      }
      // The worker's genuine failure message: before boot a failed start, after it a crash.
      if (data.type === "failed") this.stopped(String(data.message));
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
