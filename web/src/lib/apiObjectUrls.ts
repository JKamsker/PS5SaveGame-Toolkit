import { fetchBinary, onApiChange } from "../api/client";
import { PERSISTENT_CHANGES_CHANNEL, pageTab, parsePersistentChange } from "../runtime/persistentChanges";
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
 * a structural change to a document (its own assets) and a closed document or workspace. Images showing a dropped
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
const below = (prefix: string) => (cached: string) => cached.startsWith(`${prefix}/`);

/**
 * Document POSTs that only read (plans and previews), or write the preset library and not the document
 * (capture runs the plugin's pure capture adapter). Every other document mutation may change its assets.
 * From the LocalApi route table (src/SaveToolkit.LocalApi/Endpoints): DocumentRoutes, PresetRoutes and
 * ReferenceRoutes.
 */
const DOCUMENT_READS = /^\/(?:actions\/plan|presets\/[^/]+\/preview|presets\/transfer\/compatibility|presets\/capture|reference-fields\/records)$/;
/** Document mutations whose answer is an edit result; they can change the document's own assets only. */
const DOCUMENT_EDITS = /^\/(?:edits|actions|actions\/apply|changes\/revert|undo|redo|presets\/[^/]+\/apply)$/;
/** Workspace POSTs that change no existing document: opening one (always a new document id) and exports. */
const WORKSPACE_READS = /^\/(?:files\/[^/]+\/open|document-groups|export\/plan|export)$/;

/**
 * An edit result that kept the document's structure. Asset bytes only change with structure: the
 * session serves only assets of the published view (DocumentSession.GetAsset checks the view index),
 * that view and index are rebuilt only for a structural outcome (DocumentSession.AfterChange), and the
 * SDK requires `StructureChanged` whenever published nodes or view metadata change (an Atomic Heart icon
 * edit reports it). Every current plugin serves bytes fixed when the document was opened: its pinned
 * reference packs, or (Baldur's Gate 3) the package entries read at open. A rejected or stale edit
 * (`ok: false`) also reports `structureChanged: false`: nothing changed. Anything else (another body, no
 * body, a failed request the runtime may have carried out) counts as possibly structural.
 */
function keptStructure(result: unknown): boolean {
  return typeof result === "object" && result !== null && "structureChanged" in result && result.structureChanged === false
    && "state" in result && typeof result.state === "object" && result.state !== null;
}

/**
 * Which cached paths a request that may have changed something makes stale, if any; `result` is its
 * parsed answer (undefined when unknown). Reads sent as POST change nothing; any other mutation does,
 * unless its answer says otherwise: an unknown route drops everything it could affect.
 */
export function staleAfter(method: string, path: string, result?: unknown): ((cached: string) => boolean) | undefined {
  const verb = method.toUpperCase();
  if (verb === "GET" || verb === "HEAD") return undefined;
  path = path.split("?", 1)[0]!;
  if (path === "/reference-packs" || path.startsWith("/reference-packs/")) return isReferenceAsset;
  // The preset library has no images; a new workspace has no cached entries yet.
  if (path === "/presets" || path.startsWith("/presets/") || (verb === "POST" && path === "/workspaces")) return undefined;
  const document = /^(\/workspaces\/[^/?]+\/documents\/[^/?]+)(\/.*)?$/.exec(path);
  if (document) {
    const prefix = document[1]!, route = document[2] ?? "";
    if (verb === "DELETE" && !route) return below(prefix);
    if (verb === "POST" && DOCUMENT_READS.test(route)) return undefined;
    if (keptStructure(result)) return undefined;
    // Pinned reference packs do not change while the document is open, so its reference assets stay.
    if (verb === "POST" && DOCUMENT_EDITS.test(route)) return cached => cached.startsWith(`${prefix}/assets?`);
    return below(prefix);
  }
  const workspace = /^(\/workspaces\/[^/?]+)(\/.*)?$/.exec(path);
  if (workspace) {
    const prefix = workspace[1]!, route = workspace[2] ?? "";
    if (verb === "POST" && WORKSPACE_READS.test(route)) return undefined;
    return below(prefix);
  }
  return () => true;
}

onApiChange((method, path, result) => {
  const stale = staleAfter(method, path, result);
  if (stale) invalidateObjectUrls(stale);
});

/**
 * The runtime worker of every tab announces saved preset and reference-pack changes on this channel
 * (runtime/persistence.ts). A pack another tab imported or removed may change any reference asset;
 * presets have no images. The announcement of this tab's own change (BroadcastChannel delivers it to
 * this page too) is ignored: the answer to the request already dropped what it made stale.
 */
let otherTabs: BroadcastChannel | null | undefined;

function listenToOtherTabs() {
  if (otherTabs !== undefined) return;
  otherTabs = typeof BroadcastChannel === "function" ? new BroadcastChannel(PERSISTENT_CHANGES_CHANNEL) : null;
  if (!otherTabs) return;
  otherTabs.onmessage = (event: MessageEvent<unknown>) => {
    const { tab, changed } = parsePersistentChange(event.data);
    if (tab !== pageTab() && changed.includes("referencePacks")) invalidateObjectUrls(isReferenceAsset);
  };
  // Node (tests) would otherwise keep the process alive for the channel.
  (otherTabs as { unref?: () => void }).unref?.();
}
