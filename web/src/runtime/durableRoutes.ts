/**
 * Which `/api` requests touch the durable data of runtime/persistence.ts (presets and reference packs):
 * mutations of it take the cross-tab lock, reads of it refresh another tab's saves first.
 */

/**
 * Whether a request may write presets or reference packs. Workspaces live in the session root, so only
 * their preset routes take part (capture saves a preset), except those known to only read the library:
 * preview, apply (edits the document) and transfer compatibility. Every other mutation is durable.
 */
export function writesDurableData(method: string, url: string): boolean {
  const verb = method.toUpperCase();
  if (verb === "GET" || verb === "HEAD") return false;
  const path = (url.split(/[?#]/, 1)[0] ?? "").toLowerCase();
  if (path === "/api/workspaces" || path.startsWith("/api/workspaces/"))
    return /\/presets(\/|$)/.test(path) && !/\/presets\/(?:[^/]+\/(?:preview|apply)|transfer\/compatibility)\/?$/.test(path);
  return true;
}

/**
 * Every LocalApi route (src/SaveToolkit.LocalApi/Endpoints) that reads the preset library or the reference
 * packs, so another tab's saves must be loaded first. A route missing here would answer from (and pin)
 * this tab's outdated view; `persistence.routes.test.ts` checks the list against the C# route table.
 */
const DURABLE_READ_ROUTES: readonly RegExp[] = [
  // PresetRoutes: the library, its reload and transfers (PresetStore, PresetTransferService).
  /^\/api\/presets(?:\/|$)/,
  // ReferenceRoutes: the installed packs (ReferencePackStore).
  /^\/api\/reference-packs(?:\/|$)/,
  // PresetRoutes under a document: list, preview, apply, capture and transfer compatibility.
  /^\/api\/workspaces\/[^/]+\/documents\/[^/]+\/presets(?:\/|$)/,
  // Workspace.Open pins the current packs to the new document (ReferencePackStore.Pin).
  /^\/api\/workspaces\/[^/]+\/files\/[^/]+\/open\/?$/,
  // Workspace.ProposeGroups validates each proposal with pinned packs (DocumentGroupRegistry.Pin).
  /^\/api\/workspaces\/[^/]+\/files\/[^/]+\/group-proposals\/?$/,
  // Workspace.OpenGroup pins the current packs to the new group document (DocumentGroupRegistry.Pin).
  /^\/api\/workspaces\/[^/]+\/document-groups\/?$/,
];

/** Whether a request reads presets or reference packs (so it is worth refreshing them first). */
export function readsDurableData(url: string): boolean {
  const path = (url.split(/[?#]/, 1)[0] ?? "").toLowerCase();
  return DURABLE_READ_ROUTES.some(route => route.test(path));
}
