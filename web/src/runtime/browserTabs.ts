/** The browser's side of runtime/persistence.ts: the site-wide Web Lock and the change announcements. */
import { LOCK_WAIT_MS, type TabCoordination } from "./persistence";
import { newTabNonce, PERSISTENT_CHANGES_CHANNEL, tabOfWorkerName, type PersistentChangeMessage } from "./persistentChanges";

const LOCK_NAME = "savetoolkit-persistent-storage";

/**
 * Web Locks and a BroadcastChannel, where the browser has them. Announcements carry `tab` (the page's
 * nonce, from this worker's name) so that page can ignore the echo of its own change.
 */
export function browserTabs(tab = tabOfWorkerName((globalThis as { name?: unknown }).name) ?? newTabNonce()): TabCoordination {
  const locks = typeof navigator === "object" ? navigator.locks : undefined;
  const channel = typeof BroadcastChannel === "function" ? new BroadcastChannel(PERSISTENT_CHANGES_CHANNEL) : null;
  return {
    lock: locks
      ? (mode, task, waitMs = LOCK_WAIT_MS) => waitMs <= 0
        ? locks.request(LOCK_NAME, { mode, ifAvailable: true }, lock => lock ? task() : Promise.reject(new DOMException("The storage lock is held by another tab.", "AbortError")))
        : locks.request(LOCK_NAME, { mode, signal: AbortSignal.timeout(waitMs) }, task)
      : null,
    announce: changed => channel?.postMessage({ type: "persistentChange", tab, changed: [...changed] } satisfies PersistentChangeMessage),
    listen: onChange => { if (channel) channel.onmessage = () => onChange(); },
  };
}
