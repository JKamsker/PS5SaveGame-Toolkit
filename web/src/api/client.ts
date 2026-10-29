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
  SaveDocument,
  TitleInfo,
  ViewNode,
  Workspace,
  WorkspaceSummary,
  CollectionQuery,
  QueryablePage,
} from "./protocol";
import { ApiError, cancelledMessage, fetchApi, networkMessage, readJson, responseError, timeoutMessage, TRANSFER_TIMEOUT_MS } from "./http";

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
          ("plan" in body && body.plan && typeof body.plan === "object" && "errors" in body.plan)) return body as T;
      }
      throw responseError(response.status, body);
    }
    if (body === undefined) throw new ApiError("The server returned an unreadable response. Refresh the current view before retrying.", response.status);
    return body as T;
  });
}

const json = (body: unknown): RequestInit => ({
  method: "POST",
  headers: { "content-type": "application/json" },
  body: JSON.stringify(body),
});

const ws = (id: string) => `/workspaces/${encodeURIComponent(id)}`;
const doc = (wsId: string, docId: string) => `${ws(wsId)}/documents/${encodeURIComponent(docId)}`;

export const api = {
  plugins: () => request<Plugin[]>("/plugins"),
  workspaces: () => request<WorkspaceSummary[]>("/workspaces"),
  workspace: (id: string) => request<Workspace>(ws(id)),
  deleteWorkspace: (id: string) => request<void>(ws(id), { method: "DELETE" }),

  titles: () => request<TitleInfo[]>("/titles"),

  /** Uploads zips or loose files; `path` keeps folder structure for directory drops. */
  upload: (files: { file: File; path: string }[], name?: string, onProgress?: (fraction: number) => void) =>
    new Promise<Workspace>((resolve, reject) => {
      const form = new FormData();
      for (const { file, path } of files) form.append("files", file, path);
      if (name) form.append("name", name);
      // XHR rather than fetch: fetch cannot report upload progress, and dumps can be hundreds of MiB.
      const xhr = new XMLHttpRequest();
      xhr.open("POST", "/api/workspaces");
      xhr.responseType = "json";
      xhr.timeout = TRANSFER_TIMEOUT_MS;
      xhr.upload.onprogress = (e) => e.lengthComputable && onProgress?.(e.loaded / e.total);
      xhr.onload = () => {
        const body = xhr.response as (Workspace & { error?: string }) | null;
        if (xhr.status >= 200 && xhr.status < 300 && body) resolve(body);
        else reject(responseError(xhr.status, body));
      };
      xhr.onerror = () => reject(new ApiError(networkMessage, 0));
      xhr.onabort = () => reject(new ApiError(cancelledMessage, 0));
      xhr.ontimeout = () => reject(new ApiError(timeoutMessage, 0));
      xhr.send(form);
    }),

  iconUrl: (wsId: string, dirId: string) => `/api${ws(wsId)}/directories/${encodeURIComponent(dirId)}/icon`,

  open: (wsId: string, fileId: string, pluginId?: string) =>
    request<SaveDocument>(`${ws(wsId)}/files/${encodeURIComponent(fileId)}/open`, json({ pluginId: pluginId ?? null })),
  document: (wsId: string, docId: string) => request<SaveDocument>(doc(wsId, docId)),
  closeDocument: (wsId: string, docId: string) => request<void>(doc(wsId, docId), { method: "DELETE" }),
  expand: (wsId: string, docId: string, lazyId: string, signal?: AbortSignal) =>
    request<ViewNode[]>(`${doc(wsId, docId)}/nodes?id=${encodeURIComponent(lazyId)}`, { signal }),
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
    return request<QueryablePage>(`${doc(wsId, docId)}/collections?${params.toString()}`);
  },
  /** `revision` is the view revision the caller is bound to; the server rejects stale ones (`stale: true`). */
  edit: (wsId: string, docId: string, edits: { valueId: string; value: FieldValue }[], revision?: number) =>
    request<EditResult>(`${doc(wsId, docId)}/edits`, json({ edits, revision })),
  action: (wsId: string, docId: string, actionId: string, parameters: Record<string, FieldValue>, revision?: number) =>
    request<EditResult>(`${doc(wsId, docId)}/actions`, json({ actionId, parameters, revision })),
  undo: (wsId: string, docId: string) => request<EditResult>(`${doc(wsId, docId)}/undo`, { method: "POST" }),
  redo: (wsId: string, docId: string) => request<EditResult>(`${doc(wsId, docId)}/redo`, { method: "POST" }),
  assetUrl: (wsId: string, docId: string, assetId: string) => `/api${doc(wsId, docId)}/assets?id=${encodeURIComponent(assetId)}`,
  blob: async (wsId: string, docId: string, blobId: string, offset: number, count: number) => {
    return fetchApi(`${doc(wsId, docId)}/blobs?id=${encodeURIComponent(blobId)}&offset=${offset}&count=${count}`, undefined, async response => {
      if (!response.ok) throw responseError(response.status, await readJson(response));
      return new Uint8Array(await response.arrayBuffer());
    });
  },
  downloadUrl: (wsId: string, docId: string) => `/api${doc(wsId, docId)}/download`,

  /** Reference datasets pinned to the document, with attribution (Sources panel). */
  sources: (wsId: string, docId: string) => request<DocumentSources>(`${doc(wsId, docId)}/sources`),
  /** Paged catalog search; F4's picker builds on this. */
  searchReferences: (
    wsId: string,
    docId: string,
    catalogId: string,
    query: { q?: string; category?: string; sortBy?: ReferenceSortKey; descending?: boolean; offset?: number; limit?: number } = {},
  ) => {
    const params = new URLSearchParams();
    for (const [key, value] of Object.entries(query)) if (value !== undefined && value !== "") params.set(key, String(value));
    return request<ReferencePage>(`${doc(wsId, docId)}/references/${encodeURIComponent(catalogId)}?${params.toString()}`);
  },
  /** One catalog record with its pack metadata and full dataset provenance. */
  referenceRecord: (wsId: string, docId: string, catalogId: string, key: string, signal?: AbortSignal) =>
    request<ReferenceRecordDetail>(`${doc(wsId, docId)}/references/${encodeURIComponent(catalogId)}/record?${referenceParams({ key })}`, { signal }),
  referenceAssetUrl: (wsId: string, docId: string, assetId: string) =>
    `/api${doc(wsId, docId)}/reference-assets?id=${encodeURIComponent(assetId)}`,
  referenceCandidates: (wsId: string, docId: string, query: { valueId: string; actionId?: string; q?: string; category?: string; sortBy?: ReferenceSortKey; descending?: boolean; offset?: number; limit?: number; revision?: number }) =>
    request<ReferenceCandidatePage>(`${doc(wsId, docId)}/reference-fields/candidates?${referenceParams(query)}`),
  referenceCandidate: (wsId: string, docId: string, query: { valueId: string; rawId: string; actionId?: string; revision?: number }) =>
    request<ReferenceCandidate>(`${doc(wsId, docId)}/reference-fields/record?${referenceParams(query)}`),
  referencePacks: () => request<ReferencePacks>("/reference-packs"),
  importReferencePack: (file: File) => {
    const form = new FormData();
    form.append("file", file, file.name);
    return request<ReferencePackInfo>("/reference-packs", { method: "POST", body: form });
  },
  deleteReferencePack: (packId: string) => request<void>(`/reference-packs/${encodeURIComponent(packId)}`, { method: "DELETE" }),

  exportPlan: (wsId: string, scope: ExportScope, mirrorBackups: boolean) =>
    request<ExportPlan>(`${ws(wsId)}/export/plan`, json({ scope, mirrorBackups })),
  exportZip: async (wsId: string, scope: ExportScope, mirrorBackups: boolean) => {
    return fetchApi(`${ws(wsId)}/export`, json({ scope, mirrorBackups }), async response => {
      if (!response.ok) throw responseError(response.status, await readJson(response));
      const disposition = response.headers.get("content-disposition") ?? "";
      const name = /filename="?([^";]+)"?/.exec(disposition)?.[1] ?? "savebench-export.zip";
      return { blob: await response.blob(), name };
    }, TRANSFER_TIMEOUT_MS);
  },
};

function referenceParams(query: Record<string, string | number | boolean | undefined>): string {
  return new URLSearchParams(Object.entries(query).filter((entry) => entry[1] !== undefined).map(([key, value]) => [key, String(value)])).toString();
}
