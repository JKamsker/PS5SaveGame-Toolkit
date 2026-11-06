import { fetchBinary } from "../api/client";
import { RequestQueue } from "./RequestQueue";

/**
 * Shared object URLs for binary API resources (icons, assets). Without a server, an `<img src>`
 * cannot point at `/api/...`, so resources are fetched through the runtime and turned into blob URLs.
 * URLs are reference-counted and revoked shortly after the last user unmounts, so virtualized rows
 * that scroll back into view do not refetch.
 *
 * The runtime answers one request at a time, so images must not crowd out user actions: at most
 * IMAGE_CONCURRENCY image requests are handed to it at once (a user action waits behind at most that
 * many), and a fetch whose last user goes away before it finished is aborted. An aborted request that
 * the worker has not started yet is dropped without running.
 */
interface Entry {
  url: Promise<string>;
  refs: number;
  timer: ReturnType<typeof setTimeout> | undefined;
  controller: AbortController;
  settled: boolean;
}

const RELEASE_DELAY_MS = 30_000;
export const IMAGE_CONCURRENCY = 2;
const entries = new Map<string, Entry>();
const queue = new RequestQueue(IMAGE_CONCURRENCY);

export interface ObjectUrlLease { url: Promise<string>; release: () => void }

function load(path: string): Entry {
  const controller = new AbortController();
  const { signal } = controller;
  const created: Entry = {
    refs: 0, timer: undefined, controller, settled: false,
    url: queue.run(() => fetchBinary(path, signal), signal).then(({ blob }) => {
      // Released while the body was read: nobody would ever revoke a URL created now.
      signal.throwIfAborted();
      return URL.createObjectURL(blob);
    }),
  };
  created.url.then(() => { created.settled = true; }, () => {
    created.settled = true;
    // A failed or aborted fetch is retried by the next user rather than cached.
    if (entries.get(path) === created) entries.delete(path);
  });
  return created;
}

export function acquireObjectUrl(path: string): ObjectUrlLease {
  let entry = entries.get(path);
  if (!entry) {
    entry = load(path);
    entries.set(path, entry);
  }
  const held = entry;
  clearTimeout(held.timer);
  held.refs++;
  let released = false;
  return {
    url: held.url,
    release: () => {
      if (released) return;
      released = true;
      if (--held.refs > 0) return;
      if (!held.settled) {
        // Nobody is waiting for it any more: free the runtime for requests someone is waiting for.
        if (entries.get(path) === held) entries.delete(path);
        held.controller.abort();
        return;
      }
      held.timer = setTimeout(() => {
        if (held.refs > 0 || entries.get(path) !== held) return;
        entries.delete(path);
        held.url.then(url => URL.revokeObjectURL(url), () => undefined);
      }, RELEASE_DELAY_MS);
    },
  };
}
