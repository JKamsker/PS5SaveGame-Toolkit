import type {
  DocumentSources,
  EditResult,
  ExportPlan,
  ExportScope,
  FieldValue,
  Plugin,
  ReferencePackInfo,
  ReferencePacks,
  ReferencePage,
  ReferenceRecordDetail,
  ReferenceSortKey,
  ReferenceCandidate,
  ReferenceCandidatePage,
  ReferenceRecordPair,
  ReferenceRecordsResponse,
  SaveDocument,
  TitleInfo,
  ViewNode,
  Workspace,
  WorkspaceSummary,
  CollectionQuery,
  QueryablePage,
  ToolkitPreset,
  DocumentPreset,
  PresetPreview,
} from "./protocol";
import { ApiError, fetchApi, readJson, responseError, TRANSFER_TIMEOUT_MS } from "./http";
import { documentPath, postJson, workspacePath } from "./requests";
import { workspaceClosed, workspaceOpened } from "../state/tabWorkspaces";

export { ApiError } from "./http";

/** True for the server's typed "view is out of date" rejection (HTTP 409, `code: "stale"`). */
export function isStaleError(error: unknown): boolean {
  if (!(error instanceof ApiError) || error.status !== 409) return false;
  const body = error.body;
  return typeof body === "object" && body !== null && "code" in body && (body as { code: unknown }).code === "stale";
}

export async function request<T>(path: string, init?: RequestInit): Promise<T> {
  return fetchApi(path, init, async response => {
    if (response.status === 204) return undefined as T;
    const body = await readJson(response);
    if (!response.ok) {
      // Only the typed edit/preview envelopes are normal results on rejected mutations.
      // ProblemDetails may also contain an `errors` extension, which is not an EditResult.
      if ((response.status === 422 || response.status === 409) && body && typeof body === "object") {
        if (("ok" in body && "state" in body && "errors" in body && Array.isArray(body.errors)) ||
          ("plan" in body && body.plan && typeof body.plan === "object" && "errors" in body.plan) ||
          ("preview" in body && body.preview && typeof body.preview === "object" && "plan" in body.preview)) return body as T;
      }
      throw responseError(response.status, body);
    }
    if (body === undefined) throw new ApiError("The editor returned an unreadable response. Refresh the current view before retrying.", response.status);
    return body as T;
  });
}

/** Fetches a binary resource; `name` comes from Content-Disposition when present. */
export function fetchBinary(path: string, signal?: AbortSignal, timeout?: number): Promise<{ blob: Blob; name: string | undefined }> {
  return fetchApi(path, { signal }, async response => {
    if (!response.ok) throw responseError(response.status, await readJson(response));
    return { blob: await response.blob(), name: dispositionName(response) };
  }, timeout);
}

/** A 404 for a workspace means this tab no longer holds it, so leaving the page cannot lose it. */
const forgetMissing = (id: string) => (error: unknown): never => {
  if (error instanceof ApiError && error.status === 404) workspaceClosed(id);
  throw error;
};

export const api = {
  plugins: () => request<Plugin[]>("/plugins"),
  workspaces: () => request<WorkspaceSummary[]>("/workspaces"),
  workspace: (id: string) => request<Workspace>(workspacePath(id)).catch(forgetMissing(id)),
  deleteWorkspace: (id: string) => request<void>(workspacePath(id), { method: "DELETE" })
    .then(() => workspaceClosed(id), forgetMissing(id)),

  titles: () => request<TitleInfo[]>("/titles"),
  presets: () => request<ToolkitPreset[]>("/presets"),
  reloadPresets: () => request<ToolkitPreset[]>("/presets/reload", { method: "POST" }),
  documentPresets: (wsId: string, docId: string) => request<DocumentPreset[]>(`${documentPath(wsId, docId)}/presets`),
  createPreset: (name: string, compatibility: ToolkitPreset["compatibility"], actions: ToolkitPreset["actions"]) =>
    request<ToolkitPreset>("/presets", postJson({ name, compatibility, actions })),
  capturePreset: (wsId: string, docId: string, name: string, action: { actionId: string; parameters: Record<string, unknown>; scopeKind?: string; selectedRowKeys?: string[]; allMatches?: boolean }) =>
    request<ToolkitPreset>(`${documentPath(wsId, docId)}/presets/capture`, postJson({ name, ...action })),
  renamePreset: (id: string, name: string) => request<ToolkitPreset>(`/presets/${encodeURIComponent(id)}`, { ...postJson({ name }), method: "PATCH" }),
  deletePreset: (id: string) => request<void>(`/presets/${encodeURIComponent(id)}`, { method: "DELETE" }),

  /**
   * Uploads zips or loose files; `path` keeps folder structure for directory drops. The files are read
   * into one multipart body for the in-browser runtime; progress completes once the runtime has answered.
   */
  upload: (files: { file: File; path: string }[], name?: string, onProgress?: (fraction: number) => void) => {
    const form = new FormData();
    for (const { file, path } of files) form.append("files", file, path);
    if (name) form.append("name", name);
    return fetchApi("/workspaces", { method: "POST", body: form }, async response => {
      onProgress?.(1);
      const body = await readJson(response);
      if (!response.ok || !body) throw responseError(response.status, body);
      const workspace = body as Workspace;
      workspaceOpened(workspace.id);
      return workspace;
    }, TRANSFER_TIMEOUT_MS);
  },

  /** API-relative paths for binary resources; render them with ApiImage or fetch them with `api.binary`. */
  iconPath: (wsId: string, dirId: string) => `${workspacePath(wsId)}/directories/${encodeURIComponent(dirId)}/icon`,

  open: (wsId: string, fileId: string, pluginId?: string) =>
    request<SaveDocument>(`${workspacePath(wsId)}/files/${encodeURIComponent(fileId)}/open`, postJson({ pluginId: pluginId ?? null })),
  document: (wsId: string, docId: string) => request<SaveDocument>(documentPath(wsId, docId)),
  closeDocument: (wsId: string, docId: string) => request<void>(documentPath(wsId, docId), { method: "DELETE" }),
  expand: (wsId: string, docId: string, lazyId: string, signal?: AbortSignal) =>
    request<ViewNode[]>(`${documentPath(wsId, docId)}/nodes?id=${encodeURIComponent(lazyId)}`, { signal }),
  queryCollection: (wsId: string, docId: string, collectionId: string, query: CollectionQuery = {}, revision?: number) => {
    const params = new URLSearchParams({ collectionId });
    if (query.q) params.set("q", query.q);
    const filters = Object.fromEntries(Object.entries(query.filters ?? {}).filter(([, value]) => value !== ""));
    if (Object.keys(filters).length > 0) params.set("filters", JSON.stringify(filters));
    if (query.sort) params.set("sort", query.sort);
    if (query.descending) params.set("descending", "true");
    if (query.includeEmpty !== undefined) params.set("includeEmpty", String(query.includeEmpty));
    if (query.offset !== undefined) params.set("offset", String(query.offset));
    if (query.limit !== undefined) params.set("limit", String(query.limit));
    if (query.cursor) params.set("cursor", query.cursor);
    if (revision !== undefined) params.set("revision", String(revision));
    return request<QueryablePage>(`${documentPath(wsId, docId)}/collections?${params.toString()}`);
  },
  /** `revision` is the view revision the caller is bound to; the server rejects stale ones (`stale: true`). */
  edit: (wsId: string, docId: string, edits: { valueId: string; value: FieldValue }[], revision?: number) =>
    request<EditResult>(`${documentPath(wsId, docId)}/edits`, postJson({ edits, revision })),
  action: (wsId: string, docId: string, actionId: string, parameters: Record<string, FieldValue>, revision?: number) =>
    request<EditResult>(`${documentPath(wsId, docId)}/actions`, postJson({ actionId, parameters, revision })),
  presetPreview: (wsId: string, docId: string, presetId: string, revision?: number, choices?: Record<string, string>) =>
    request<PresetPreview>(`${documentPath(wsId, docId)}/presets/${encodeURIComponent(presetId)}/preview`, postJson({ revision, choices })),
  presetApply: (wsId: string, docId: string, presetId: string, planId: string, revision: number) =>
    request<EditResult>(`${documentPath(wsId, docId)}/presets/${encodeURIComponent(presetId)}/apply`, postJson({ planId, revision })),
  undo: (wsId: string, docId: string) => request<EditResult>(`${documentPath(wsId, docId)}/undo`, { method: "POST" }),
  redo: (wsId: string, docId: string) => request<EditResult>(`${documentPath(wsId, docId)}/redo`, { method: "POST" }),
  assetPath: (wsId: string, docId: string, assetId: string) => `${documentPath(wsId, docId)}/assets?id=${encodeURIComponent(assetId)}`,
  blob: async (wsId: string, docId: string, blobId: string, offset: number, count: number) => {
    return fetchApi(`${documentPath(wsId, docId)}/blobs?id=${encodeURIComponent(blobId)}&offset=${offset}&count=${count}`, undefined, async response => {
      if (!response.ok) throw responseError(response.status, await readJson(response));
      return new Uint8Array(await response.arrayBuffer());
    });
  },
  /** The current bytes of one save file, with the server-chosen file name. */
  download: (wsId: string, docId: string) => fetchBinary(`${documentPath(wsId, docId)}/download`, undefined, TRANSFER_TIMEOUT_MS),
  binary: fetchBinary,

  /** Reference datasets pinned to the document, with attribution (Sources panel). */
  sources: (wsId: string, docId: string) => request<DocumentSources>(`${documentPath(wsId, docId)}/sources`),
  /** Paged catalog search; F4's picker builds on this. */
  searchReferences: (
    wsId: string,
    docId: string,
    catalogId: string,
    query: { q?: string; category?: string; sortBy?: ReferenceSortKey; descending?: boolean; offset?: number; limit?: number } = {},
  ) => {
    const params = new URLSearchParams();
    for (const [key, value] of Object.entries(query)) if (value !== undefined && value !== "") params.set(key, String(value));
    return request<ReferencePage>(`${documentPath(wsId, docId)}/references/${encodeURIComponent(catalogId)}?${params.toString()}`);
  },
  /** One catalog record with its pack metadata and full dataset provenance. */
  referenceRecord: (wsId: string, docId: string, catalogId: string, key: string, signal?: AbortSignal) =>
    request<ReferenceRecordDetail>(`${documentPath(wsId, docId)}/references/${encodeURIComponent(catalogId)}/record?${referenceParams({ key })}`, { signal }),
  referenceAssetPath: (wsId: string, docId: string, assetId: string) =>
    `${documentPath(wsId, docId)}/reference-assets?id=${encodeURIComponent(assetId)}`,
  referenceCandidates: (wsId: string, docId: string, query: { valueId: string; actionId?: string; q?: string; category?: string; sortBy?: ReferenceSortKey; descending?: boolean; offset?: number; limit?: number; revision?: number }) =>
    request<ReferenceCandidatePage>(`${documentPath(wsId, docId)}/reference-fields/candidates?${referenceParams(query)}`),
  referenceCandidate: (wsId: string, docId: string, query: { valueId: string; rawId: string; actionId?: string; revision?: number }) =>
    request<ReferenceCandidate>(`${documentPath(wsId, docId)}/reference-fields/record?${referenceParams(query)}`),
  referenceRecords: (wsId: string, docId: string, pairs: ReferenceRecordPair[], revision?: number, signal?: AbortSignal) =>
    request<ReferenceRecordsResponse>(`${documentPath(wsId, docId)}/reference-fields/records`, { ...postJson({ pairs, revision }), signal }),
  referencePacks: () => request<ReferencePacks>("/reference-packs"),
  importReferencePack: (file: File) => {
    const form = new FormData();
    form.append("file", file, file.name);
    return request<ReferencePackInfo>("/reference-packs", { method: "POST", body: form });
  },
  deleteReferencePack: (packId: string) => request<void>(`/reference-packs/${encodeURIComponent(packId)}`, { method: "DELETE" }),

  exportPlan: (wsId: string, scope: ExportScope, mirrorBackups: boolean) =>
    request<ExportPlan>(`${workspacePath(wsId)}/export/plan`, postJson({ scope, mirrorBackups })),
  exportZip: async (wsId: string, scope: ExportScope, mirrorBackups: boolean) => {
    return fetchApi(`${workspacePath(wsId)}/export`, postJson({ scope, mirrorBackups }), async response => {
      if (!response.ok) throw responseError(response.status, await readJson(response));
      return { blob: await response.blob(), name: dispositionName(response) ?? "savebench-export.zip" };
    }, TRANSFER_TIMEOUT_MS);
  },
};

function dispositionName(response: Response): string | undefined {
  const disposition = response.headers.get("content-disposition") ?? "";
  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(disposition)?.[1];
  if (encoded) {
    try { return decodeURIComponent(encoded); } catch { /* fall back to the plain parameter */ }
  }
  return /filename="?([^";]+)"?/.exec(disposition)?.[1];
}

function referenceParams(query: Record<string, string | number | boolean | undefined>): string {
  return new URLSearchParams(Object.entries(query).filter((entry) => entry[1] !== undefined).map(([key, value]) => [key, String(value)])).toString();
}
