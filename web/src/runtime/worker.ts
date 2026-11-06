/**
 * Module worker hosting the .NET runtime (SaveToolkit.Browser). It boots once, then handles one
 * request at a time, in arrival order; a cancel message drops a request that has not started.
 */
import { bootDotnet, type BootedRuntime } from "./dotnetBoot";
import { describeError as describe, isOutOfMemory, isRuntimeStopped } from "./failures";
import { decodeFrame } from "./framing";
import { RuntimeHungError } from "./persistence";
import type { FromWorker, ToWorker, WireRequest } from "./protocol";

let runtime: Promise<BootedRuntime> | null = null;
/** Serializes requests and the IndexedDB flushes that follow mutations. */
let queue: Promise<void> = Promise.resolve();
/** Requests waiting in the queue; a cancel removes its id, so the request is dropped when its turn comes. */
const queued = new Set<number>();

const post = (message: FromWorker, transfer: Transferable[] = []) => self.postMessage(message, { transfer });

self.onmessage = (event: MessageEvent<ToWorker>) => {
  const message = event.data;
  if (message.type === "boot") boot(message.frameworkUrl);
  // A cancel for a request that already started or finished has nothing left to drop.
  else if (message.type === "cancel") queued.delete(message.id);
  else {
    const { request } = message;
    queued.add(request.id);
    // Whatever escapes a request must not reject the chain: every later request would wait forever.
    queue = queue.then(() => handle(request)).catch((error: unknown) => {
      // eslint-disable-next-line no-console -- the browser runtime has no server log; the console is where details go.
      console.error(`Runtime request ${request.method} ${request.url} could not be answered:`, error);
      try { post({ type: "error", id: request.id, message: describe(error) }); } catch { /* the page gives up on its deadline */ }
    });
  }
};

/** Set once the runtime answered `ready`; a later stop is a crash that lost this tab's workspaces. */
let running = false;
let stopped: string | null = null;

/** The booted runtime aborted, exited or trapped: the page fails every pending request and stops guarding workspaces. */
function stop(reason: string) {
  if (!running || stopped !== null) return;
  stopped = reason;
  // A stopped runtime never settles the request in flight. If that is a change to presets or packs, it
  // holds the cross-tab storage lock: release it now, so other tabs need not wait for the watchdog.
  void runtime?.then(booted => booted.persistence.stop(reason), () => undefined);
  post({ type: "failed", message: reason });
}

// An uncaught error the runtime survives must not reach the page, which would treat the worker as dead.
self.addEventListener("error", event => {
  const error: unknown = event.error ?? event.message;
  if (isRuntimeStopped(error)) {
    // Before `ready`, the page reports it as a failed boot.
    if (!running) return;
    stop(describe(error));
  } else {
    // eslint-disable-next-line no-console -- the browser runtime has no server log; the console is where details go.
    console.error("Uncaught error in the editor runtime worker:", error);
  }
  event.preventDefault();
});
self.addEventListener("unhandledrejection", event => {
  if (isRuntimeStopped(event.reason)) stop(describe(event.reason));
});

/** Byte progress is posted at most this often; any message counts as progress for waiting requests. */
const BYTE_PROGRESS_INTERVAL_MS = 1_000;

function boot(frameworkUrl: string) {
  if (runtime) return;
  let files = { loaded: 0, total: 0 };
  let received = 0;
  let reported = 0;
  runtime = bootDotnet(frameworkUrl, {
    onDownload: (loaded, total) => { files = { loaded, total }; post({ type: "progress", loaded, total, received }); },
    onStarting: () => post({ type: "progress", ...files, received, starting: true }),
    onBytes: bytes => {
      received += bytes;
      if (running || Date.now() - reported < BYTE_PROGRESS_INTERVAL_MS) return;
      reported = Date.now();
      post({ type: "progress", ...files, received });
    },
    onStopped: reason => stop(reason),
  });
  runtime.then(
    ready => { running = true; post({ type: "ready", boot: ready.boot }); },
    (error: unknown) => post({ type: "failed", message: describe(error) }));
}

async function handle(request: WireRequest) {
  if (!queued.delete(request.id)) return;
  // Requests queued before the page learned of the stop never run: a trapped runtime's memory may be
  // corrupt, and a durable request would flush it to IndexedDB. The page has already failed them.
  if (stopped !== null) return post({ type: "error", id: request.id, message: stopped });
  // Leaving the queue: the page starts the request's deadline now, not while it waited behind others.
  post({ type: "started", id: request.id });
  try {
    if (!runtime) throw new Error("The runtime has not been started.");
    const booted = await runtime;
    // Presets and reference packs live on IDBFS, shared by every tab of the site. Durable mutations run
    // under a cross-tab lock on freshly loaded data and are flushed before answering; a failed flush
    // becomes an error response, so nothing is reported as saved that a reload would lose.
    const frame = await booted.persistence.run(request.method, request.url, async () => {
      const handle = await booted.interop.DispatchAsync(request.method, request.url, JSON.stringify(request.headers),
        new Uint8Array(request.body ?? new ArrayBuffer(0)));
      return booted.interop.TakeResponse(handle);
    });
    const { status, headers, bodyOffset } = decodeFrame(frame);
    // The interop result is a JS-owned copy; transfer it rather than copying again.
    const owned = frame.byteOffset === 0 && frame.byteLength === frame.buffer.byteLength ? frame.buffer as ArrayBuffer : frame.slice().buffer;
    post({ type: "response", id: request.id, status, headers, frame: owned, bodyOffset: bodyOffset - frame.byteOffset }, [owned]);
  } catch (error) {
    // eslint-disable-next-line no-console -- the browser runtime has no server log; the console is where details go.
    console.error(`Runtime request ${request.method} ${request.url} failed:`, error);
    // A change that never finished: the runtime is unusable (it released the storage lock); the page
    // reports a crash for every pending request and offers a reload.
    if (error instanceof RuntimeHungError) return stop(error.message);
    // The .NET runtime aborted (an exhausted heap) or exited during the request: report a crash, not a
    // failed request, since the workspaces in its memory are gone and no later request can succeed.
    if (running && isRuntimeStopped(error)) return stop(describe(error));
    post({ type: "error", id: request.id, message: describe(error), outOfMemory: isOutOfMemory(error) });
  }
}
