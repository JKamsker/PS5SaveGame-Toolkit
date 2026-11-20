/**
 * The message every tab's runtime worker posts on a BroadcastChannel after it saved presets or reference
 * packs (runtime/persistence.ts). It says what changed and which tab saved it: BroadcastChannel delivers
 * a message to every other channel object of the origin, including the page of the tab whose worker
 * posted it, and that page already handled its own change when the request was answered.
 *
 * The page and its worker share the tab nonce through the worker's name (runtime/index.ts), so no
 * worker message is needed for it.
 */

export const PERSISTENT_CHANGES_CHANNEL = "savetoolkit-persistent-storage";

export type PersistentData = "presets" | "referencePacks";
const ALL: readonly PersistentData[] = ["presets", "referencePacks"];

export interface PersistentChangeMessage {
  type: "persistentChange";
  /** The nonce of the tab whose worker saved the change. */
  tab: string;
  changed: PersistentData[];
}

/**
 * What a durable mutation may have changed, from its route. Only a route known to touch one kind names
 * that kind; anything else names both, so a receiver never ignores a change it should have seen.
 */
export function changedData(url: string): PersistentData[] {
  const path = (url.split(/[?#]/, 1)[0] ?? "").toLowerCase();
  if (/^\/api\/reference-packs(\/|$)/.test(path)) return ["referencePacks"];
  if (/^\/api\/presets(\/|$)/.test(path) || /^\/api\/workspaces\/[^/]+\/documents\/[^/]+\/presets(\/|$)/.test(path))
    return ["presets"];
  return [...ALL];
}

/**
 * Reads a channel message. Anything that is not a well-formed change message (for example the plain
 * `"changed"` an older version of the site posts) counts as a change of everything by another tab.
 */
export function parsePersistentChange(data: unknown): { tab: string | undefined; changed: readonly PersistentData[] } {
  if (typeof data === "object" && data !== null) {
    const { type, tab, changed } = data as Partial<Record<keyof PersistentChangeMessage, unknown>>;
    if (type === "persistentChange" && typeof tab === "string" && Array.isArray(changed)) {
      const known = changed.filter((kind): kind is PersistentData => ALL.includes(kind as PersistentData));
      // An unknown kind (a newer version of the site) may affect anything.
      return { tab, changed: known.length === changed.length ? known : [...ALL] };
    }
  }
  return { tab: undefined, changed: [...ALL] };
}

export function newTabNonce(): string {
  return typeof crypto === "object" && typeof crypto.randomUUID === "function"
    ? crypto.randomUUID() : `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}

let page: string | undefined;
/** This page's tab nonce (page realm only). */
export function pageTab(): string {
  return page ??= newTabNonce();
}

const WORKER_NAME = "savetoolkit-runtime";
/** The runtime worker's name: readable in devtools, and it hands the page's tab nonce to the worker. */
export const runtimeWorkerName = (tab: string) => `${WORKER_NAME} ${tab}`;
/** The tab nonce in a runtime worker's name, if it has one. */
export function tabOfWorkerName(name: unknown): string | undefined {
  if (typeof name !== "string" || !name.startsWith(`${WORKER_NAME} `)) return undefined;
  return name.slice(WORKER_NAME.length + 1) || undefined;
}
