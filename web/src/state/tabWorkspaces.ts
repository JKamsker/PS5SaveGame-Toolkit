import { runtime } from "../runtime";

/**
 * Workspaces live only in this tab's in-browser runtime memory (see docs/BROWSER_RUNTIME.md), so a
 * reload or closing the tab discards every import and unsaved edit. While the runtime holds at least one
 * workspace, the browser asks before leaving the page. The listener is only installed while it is
 * needed: an unconditional `beforeunload` handler would also keep the page out of the back/forward cache.
 *
 * The tracked set follows what the runtime reports: imports and successful workspace reads add an id, a
 * close or a "workspace not found" removes it, and every workspace list replaces the set (an import that
 * timed out in the page but finished in the runtime, an idle sweep, a close whose answer was lost).
 */
const open = new Set<string>();
let installed = false;
/** Counts changes; `changedAt` records the step of each id's last change, so a list never undoes a newer one. */
let step = 0;
const changedAt = new Map<string, number>();

function confirmLeave(event: BeforeUnloadEvent) {
  // A crashed runtime has already lost its workspaces; asking would only get in the way of the reload.
  if (open.size === 0 || runtime.state().status === "failed") return;
  event.preventDefault();
  // Legacy browsers only show the prompt when returnValue is set.
  event.returnValue = "";
}

function sync() {
  if (typeof window === "undefined") return;
  const wanted = open.size > 0;
  if (wanted === installed) return;
  installed = wanted;
  if (wanted) window.addEventListener("beforeunload", confirmLeave);
  else window.removeEventListener("beforeunload", confirmLeave);
}

function mark(id: string, present: boolean) {
  changedAt.set(id, ++step);
  if (present) open.add(id);
  else open.delete(id);
}

/** The runtime holds this workspace (it was imported or read successfully). */
export function workspaceOpened(id: string): void {
  mark(id, true);
  sync();
}

/** A workspace was closed, or the runtime reported it no longer exists. */
export function workspaceClosed(id: string): void {
  mark(id, false);
  sync();
}

/**
 * Call when a workspace list request is sent; apply the result to the returned function. The runtime
 * answers in order, so the list is authoritative for every id that did not change after it was sent
 * (an import answered meanwhile may or may not be in it).
 */
export function workspacesListing(): (ids: readonly string[]) => void {
  const sent = step;
  return ids => {
    const listed = new Set(ids);
    const current = (id: string) => (changedAt.get(id) ?? 0) <= sent;
    for (const id of [...open]) if (!listed.has(id) && current(id)) open.delete(id);
    for (const id of listed) if (!open.has(id) && current(id)) open.add(id);
    sync();
  };
}

/** Number of workspaces this tab would lose on reload (for tests and diagnostics). */
export function openWorkspaceCount(): number {
  return open.size;
}
