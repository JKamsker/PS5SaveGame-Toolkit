/**
 * Module worker hosting the .NET runtime (SaveToolkit.Browser). It boots once, then handles one
 * request at a time, in arrival order; a cancel message drops a request that has not started.
 */
import { bootDotnet, type BootedRuntime } from "./dotnetBoot";
import { isOutOfMemory } from "./failures";
import { decodeFrame } from "./framing";
import type { FromWorker, ToWorker, WireRequest } from "./protocol";

let runtime: Promise<BootedRuntime> | null = null;
/** Serializes requests and the IndexedDB flushes that follow mutations. */
let queue: Promise<void> = Promise.resolve();
/** Requests cancelled by their caller before they started. */
const cancelled = new Set<number>();

const post = (message: FromWorker, transfer: Transferable[] = []) => self.postMessage(message, { transfer });

self.onmessage = (event: MessageEvent<ToWorker>) => {
  const message = event.data;
  if (message.type === "boot") boot(message.frameworkUrl);
  else if (message.type === "cancel") cancelled.add(message.id);
  else queue = queue.then(() => handle(message.request));
};

function boot(frameworkUrl: string) {
  if (runtime) return;
  let files = { loaded: 0, total: 0 };
  runtime = bootDotnet(frameworkUrl, {
    onDownload: (loaded, total) => { files = { loaded, total }; post({ type: "progress", loaded, total }); },
    onStarting: () => post({ type: "progress", ...files, starting: true }),
  });
  runtime.then(
    booted => post({ type: "ready", boot: booted.boot }),
    (error: unknown) => post({ type: "failed", message: error instanceof Error ? error.message : String(error) }));
}

async function handle(request: WireRequest) {
  if (cancelled.delete(request.id)) return;
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
    post({ type: "error", id: request.id, message: error instanceof Error ? error.message : String(error), outOfMemory: isOutOfMemory(error) });
  } finally {
    // A cancel that arrived while the request ran has nothing left to drop.
    cancelled.delete(request.id);
  }
}
