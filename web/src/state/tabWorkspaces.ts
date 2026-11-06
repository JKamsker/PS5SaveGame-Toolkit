import { runtime } from "../runtime";

/**
 * Workspaces live only in this tab's in-browser runtime memory (see docs/BROWSER_RUNTIME.md), so a
 * reload or closing the tab discards every import and unsaved edit. While at least one workspace this
 * tab created is still open, the browser asks before leaving the page. The listener is only installed
 * while it is needed: an unconditional `beforeunload` handler would also keep the page out of the
 * back/forward cache.
 */
const open = new Set<string>();
let installed = false;

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

/** A workspace was imported into this tab's runtime. */
export function workspaceOpened(id: string): void {
  open.add(id);
  sync();
}

/** A workspace was closed, or the runtime reported it no longer exists. */
export function workspaceClosed(id: string): void {
  open.delete(id);
  sync();
}

/** Number of workspaces this tab would lose on reload (for tests and diagnostics). */
export function openWorkspaceCount(): number {
  return open.size;
}
