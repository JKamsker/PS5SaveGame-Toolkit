import { fetchBinary, onApiChange } from "../api/client";
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
 *
 * The cache is keyed by path, so a change that can alter the bytes behind a path drops the cached entries
 * it affects (`staleAfter`): an imported or removed reference pack (in this or another tab of the site),
 * an edit to a document (its own assets) and a closed document or workspace. Images showing a dropped
 * entry fetch it again (`objectUrlStamp`) and keep the old picture until the new one arrives.
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
/** Bumped for each path whose cached entry was dropped as stale; images holding it re-acquire. */
const stamps = new Map<string, number>();
let lastStamp = 0;
const listeners = new Set<() => void>();

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
  listenToOtherTabs();
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
        if (held.refs > 0) return;
        // A stale entry was already dropped from the cache; its URL still needs revoking.
        if (entries.get(path) === held) entries.delete(path);
        held.url.then(url => URL.revokeObjectURL(url), () => undefined);
      }, RELEASE_DELAY_MS);
    },
  };
}

/** Changes whenever the cached bytes for `path` were dropped as stale; an image re-acquires on change. */
export function objectUrlStamp(path: string): number {
  return stamps.get(path) ?? 0;
}

export function subscribeObjectUrls(listener: () => void): () => void {
  listeners.add(listener);
  return () => { listeners.delete(listener); };
}

/** Drops the cached entries whose path matches; current users keep their URL until they re-acquire. */
export function invalidateObjectUrls(matches: (path: string) => boolean): void {
  let dropped = false;
  for (const path of [...entries.keys()]) {
    if (!matches(path)) continue;
    entries.delete(path);
    stamps.set(path, ++lastStamp);
    dropped = true;
  }
  if (dropped) for (const listener of [...listeners]) listener();
}

const isReferenceAsset = (path: string) => /\/reference-assets\?/.test(path);

/** Which cached paths a successful request makes stale, if any. Reads (including POST reads) change nothing. */
export function staleAfter(method: string, path: string): ((cached: string) => boolean) | undefined {
  if (path === "/reference-packs" || path.startsWith("/reference-packs/")) return isReferenceAsset;
  const document = /^(\/workspaces\/[^/]+\/documents\/[^/?]+)(\/(?:edits|actions|undo|redo|presets\/[^/?]+\/apply))?$/.exec(path);
  if (document) {
    const [, prefix, change] = document;
    if (method === "DELETE" && !change) return cached => cached.startsWith(`${prefix}/`);
    if (method === "POST" && change) return cached => cached.startsWith(`${prefix}/assets?`);
    return undefined;
  }
  if (method === "DELETE" && /^\/workspaces\/[^/?]+$/.test(path)) return cached => cached.startsWith(`${path}/`);
  return undefined;
}

onApiChange((method, path) => {
  const stale = staleAfter(method, path);
  if (stale) invalidateObjectUrls(stale);
});

/**
 * The runtime worker of every tab announces saved preset and reference-pack changes on this channel
 * (runtime/persistence.ts, CHANNEL_NAME); a pack another tab imported or removed may change any asset.
 */
const PERSISTENT_CHANGES_CHANNEL = "savetoolkit-persistent-storage";
let otherTabs: BroadcastChannel | null | undefined;

function listenToOtherTabs() {
  if (otherTabs !== undefined) return;
  otherTabs = typeof BroadcastChannel === "function" ? new BroadcastChannel(PERSISTENT_CHANGES_CHANNEL) : null;
  if (!otherTabs) return;
  otherTabs.onmessage = () => invalidateObjectUrls(isReferenceAsset);
  // Node (tests) would otherwise keep the process alive for the channel.
  (otherTabs as { unref?: () => void }).unref?.();
}
