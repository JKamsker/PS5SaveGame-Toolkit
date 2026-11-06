/**
 * Page-side client for the in-browser .NET runtime. Every `/api` request is answered by the worker
 * instead of a server, and comes back as a real `Response`.
 *
 * The worker runs requests one at a time, in order. A request aborted before the worker starts it is
 * dropped (a cancel message removes it from the worker's queue); one already running completes in the
 * worker, but its caller sees an AbortError immediately and the late result is discarded (the same
 * observable semantics as an aborted network fetch). `events.onStart` reports when the worker begins a
 * request, so callers can exclude the time spent queued behind earlier requests from their deadlines.
 */
import { isOutOfMemory } from "./failures";
import type { BootInfo, FromWorker, HeaderPairs, ToWorker, WireRequest } from "./protocol";

export type RuntimeState =
  | { status: "loading"; loaded: number; total: number; received?: number; starting?: boolean }
  | { status: "ready"; boot: BootInfo }
  /** `crashed`: the runtime had booted and then stopped (for example out of memory), losing this tab's workspaces. */
  | { status: "failed"; message: string; crashed?: boolean };

/** Lifecycle callbacks for one request. */
export interface RequestEvents {
  /** The worker began executing the request, after any requests queued ahead of it finished. */
  onStart?(): void;
}

/** A failure the runtime reported for one request (not a lost or unresponsive worker). */
export class RuntimeRequestError extends TypeError {
  constructor(message: string, readonly outOfMemory: boolean) {
    super(message);
    this.name = "RuntimeRequestError";
  }
}

export interface RuntimeClient {
  fetch(url: string, init?: RequestInit, events?: RequestEvents): Promise<Response>;
  state(): RuntimeState;
  subscribe(listener: () => void): () => void;
  /** Starts the runtime if needed and resolves once it has booted. */
  ready(signal?: AbortSignal | null): Promise<void>;
}

interface Pending {
  id: number;
  resolve: (response: Response) => void;
  reject: (error: unknown) => void;
  settled: boolean;
  onStart?: () => void;
}

const abortError = () => new DOMException("The request was aborted.", "AbortError");
const nullBodyStatus = new Set([101, 103, 204, 205, 304]);

/** Testable core; `createWorker` is only called once, on first use. */
export function createRuntimeClient(createWorker: () => Worker, frameworkUrl: string): RuntimeClient {
  const client = new WorkerRuntimeClient(createWorker, frameworkUrl);
  return {
    fetch: (url, init, events) => client.fetch(url, init, events),
    state: () => client.current,
    subscribe: listener => client.subscribe(listener),
    ready: signal => client.ready(signal),
  };
}

class WorkerRuntimeClient {
  current: RuntimeState = { status: "loading", loaded: 0, total: 0 };
  private worker: Worker | null = null;
  private readonly listeners = new Set<() => void>();
  private readonly pending = new Map<number, Pending>();
  private nextId = 1;

  constructor(private readonly createWorker: () => Worker, private readonly frameworkUrl: string) {}

  async fetch(url: string, init: RequestInit = {}, events: RequestEvents = {}): Promise<Response> {
    const signal = init.signal;
    if (signal?.aborted) throw abortError();
    this.start();
    if (this.current.status === "failed") throw new TypeError(this.current.message);
    const { headers, body } = await serialize(url, init);
    if (signal?.aborted) throw abortError();
    const state = this.current as RuntimeState; // may have failed while the body was serialized
    if (state.status === "failed") throw new TypeError(state.message);
    const request: WireRequest = { id: this.nextId++, method: (init.method ?? "GET").toUpperCase(), url, headers, body };
    return new Promise<Response>((resolve, reject) => {
      const pending: Pending = { id: request.id, resolve, reject, settled: false, onStart: events.onStart };
      this.pending.set(request.id, pending);
      signal?.addEventListener("abort", () => {
        if (!this.pending.delete(request.id)) return;
        this.worker?.postMessage({ type: "cancel", id: request.id } satisfies ToWorker);
        settle(pending, () => reject(abortError()));
      }, { once: true });
      this.start().postMessage({ type: "request", request } satisfies ToWorker, body ? [body] : []);
    });
  }

  ready(signal?: AbortSignal | null): Promise<void> {
    if (this.current.status === "ready") return Promise.resolve();
    return new Promise<void>((resolve, reject) => {
      const check = () => {
        if (signal?.aborted) reject(abortError());
        else if (this.current.status === "ready") resolve();
        else if (this.current.status === "failed") reject(new TypeError(this.current.message));
        else return;
        unsubscribe();
        signal?.removeEventListener("abort", check);
      };
      const unsubscribe = this.subscribe(check);
      signal?.addEventListener("abort", check, { once: true });
      try { this.start(); } catch { /* reported through the failed state */ }
      check();
    });
  }

  subscribe(listener: () => void): () => void {
    this.listeners.add(listener);
    return () => { this.listeners.delete(listener); };
  }

  private start(): Worker {
    if (this.worker) return this.worker;
    try {
      this.worker = this.createWorker();
    } catch (error) {
      this.setState({ status: "failed", message: error instanceof Error ? error.message : String(error) });
      throw error;
    }
    this.worker.onmessage = (event: MessageEvent<FromWorker>) => this.receive(event.data);
    this.worker.onerror = event => this.fail(event.message || "The runtime worker could not be started.");
    this.worker.postMessage({ type: "boot", frameworkUrl: this.frameworkUrl } satisfies ToWorker);
    return this.worker;
  }

  private setState(next: RuntimeState) {
    this.current = next;
    for (const listener of [...this.listeners]) listener();
  }

  private fail(message: string) {
    const crashed = this.current.status === "ready" || (this.current.status === "failed" && this.current.crashed === true);
    this.setState(crashed ? { status: "failed", message, crashed } : { status: "failed", message });
    const pending = [...this.pending.values()];
    this.pending.clear();
    // A worker killed by an exhausted heap: say so for the request that triggered it.
    const error = () => isOutOfMemory(message) ? new RuntimeRequestError(message, true) : new TypeError(message);
    for (const request of pending) settle(request, () => request.reject(error()));
  }

  private receive(message: FromWorker) {
    if (message.type === "progress") {
      // A file loaded after boot (the runtime loads some lazily) does not make the runtime "loading" again.
      if (this.current.status !== "loading") return;
      const { loaded, total, received, starting } = message;
      this.setState({ status: "loading", loaded, total, received, starting });
    } else if (message.type === "ready") this.setState({ status: "ready", boot: message.boot });
    else if (message.type === "failed") this.fail(message.message);
    else if (message.type === "started") this.pending.get(message.id)?.onStart?.();
    else {
      // A missing entry was aborted by its caller; its late result is ignored.
      const finished = this.pending.get(message.id);
      if (!finished) return;
      this.pending.delete(message.id);
      if (message.type === "error") settle(finished, () => finished.reject(new RuntimeRequestError(message.message, message.outOfMemory === true)));
      else settle(finished, () => finished.resolve(toResponse(message.status, message.headers, message.frame, message.bodyOffset)));
    }
  }
}

function settle(pending: Pending, outcome: () => void) {
  if (pending.settled) return;
  pending.settled = true;
  outcome();
}

/**
 * Only headers the UI set (Content-Type, If-Match, Accept, ...) reach the runtime. Browser-managed
 * ones would make the router's same-origin checks (e.g. reference-pack import/delete) see a mismatch.
 */
const browserManaged = /^(origin|host|cookie|referer|sec-.*)$/i;
const forwarded = (headers: Headers): HeaderPairs => [...headers].filter(([name]) => !browserManaged.test(name));

/** Serializes any fetch body exactly like the browser would (multipart boundaries included). */
async function serialize(url: string, init: RequestInit): Promise<{ headers: HeaderPairs; body: ArrayBuffer | null }> {
  if (init.body === undefined || init.body === null) return { headers: forwarded(new Headers(init.headers)), body: null };
  const request = new Request(new URL(url, "http://savetoolkit.local/"), { method: init.method ?? "POST", headers: init.headers, body: init.body });
  return { headers: forwarded(request.headers), body: await request.arrayBuffer() };
}

function toResponse(status: number, headers: HeaderPairs, frame: ArrayBuffer, bodyOffset: number): Response {
  const body = nullBodyStatus.has(status) ? null : new Uint8Array(frame, bodyOffset);
  return new Response(body, { status, headers });
}
