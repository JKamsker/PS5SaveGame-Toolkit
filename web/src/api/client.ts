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
  SaveDocument,
  TitleInfo,
  ViewNode,
  Workspace,
  WorkspaceSummary,
} from "./protocol";

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly body?: unknown,
  ) {
    super(message);
  }
}

/** True for the server's typed "view is out of date" rejection (HTTP 409, `code: "stale"`). */
export function isStaleError(error: unknown): boolean {
  if (!(error instanceof ApiError) || error.status !== 409) return false;
  const body = error.body;
  return typeof body === "object" && body !== null && "code" in body && (body as { code: unknown }).code === "stale";
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`/api${path}`, init);
  if (response.status === 204) return undefined as T;
  const isJson = response.headers.get("content-type")?.includes("json") ?? false;
  const body: unknown = isJson ? await response.json() : undefined;
  if (!response.ok) {
    // Edit endpoints return a full EditResult with field errors on 422 (or 409 when stale); callers inspect it.
    if ((response.status === 422 || response.status === 409) && body && typeof body === "object" && "errors" in body) return body as T;
    const message = body && typeof body === "object" && "error" in body ? String((body as { error: unknown }).error) : response.statusText;
    throw new ApiError(message || `Request failed (${response.status})`, response.status, body);
  }
  return body as T;
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
      xhr.upload.onprogress = (e) => e.lengthComputable && onProgress?.(e.loaded / e.total);
      xhr.onload = () => {
        const body = xhr.response as (Workspace & { error?: string }) | null;
        if (xhr.status >= 200 && xhr.status < 300 && body) resolve(body);
        else reject(new ApiError(body?.error ?? `Import failed (${xhr.status})`, xhr.status, body));
      };
      xhr.onerror = () => reject(new ApiError("The server could not be reached.", 0));
      xhr.send(form);
    }),

  iconUrl: (wsId: string, dirId: string) => `/api${ws(wsId)}/directories/${encodeURIComponent(dirId)}/icon`,

  open: (wsId: string, fileId: string, pluginId?: string) =>
    request<SaveDocument>(`${ws(wsId)}/files/${encodeURIComponent(fileId)}/open`, json({ pluginId: pluginId ?? null })),
  document: (wsId: string, docId: string) => request<SaveDocument>(doc(wsId, docId)),
  closeDocument: (wsId: string, docId: string) => request<void>(doc(wsId, docId), { method: "DELETE" }),
  expand: (wsId: string, docId: string, lazyId: string) =>
    request<ViewNode[]>(`${doc(wsId, docId)}/nodes?id=${encodeURIComponent(lazyId)}`),
  /** `revision` is the view revision the caller is bound to; the server rejects stale ones (`stale: true`). */
  edit: (wsId: string, docId: string, edits: { valueId: string; value: FieldValue }[], revision?: number) =>
    request<EditResult>(`${doc(wsId, docId)}/edits`, json({ edits, revision })),
  action: (wsId: string, docId: string, actionId: string, parameters: Record<string, FieldValue>, revision?: number) =>
    request<EditResult>(`${doc(wsId, docId)}/actions`, json({ actionId, parameters, revision })),
  undo: (wsId: string, docId: string) => request<EditResult>(`${doc(wsId, docId)}/undo`, { method: "POST" }),
  redo: (wsId: string, docId: string) => request<EditResult>(`${doc(wsId, docId)}/redo`, { method: "POST" }),
  assetUrl: (wsId: string, docId: string, assetId: string) => `/api${doc(wsId, docId)}/assets?id=${encodeURIComponent(assetId)}`,
  blob: async (wsId: string, docId: string, blobId: string, offset: number, count: number) => {
    const response = await fetch(`/api${doc(wsId, docId)}/blobs?id=${encodeURIComponent(blobId)}&offset=${offset}&count=${count}`);
    if (!response.ok) throw new ApiError("Could not read bytes", response.status);
    return new Uint8Array(await response.arrayBuffer());
  },
  downloadUrl: (wsId: string, docId: string) => `/api${doc(wsId, docId)}/download`,

  /** Reference datasets pinned to the document, with attribution (Sources panel). */
  sources: (wsId: string, docId: string) => request<DocumentSources>(`${doc(wsId, docId)}/sources`),
  /** Paged catalog search; F4's picker builds on this. */
  searchReferences: (
    wsId: string,
    docId: string,
    catalogId: string,
    query: { q?: string; category?: string; offset?: number; limit?: number } = {},
  ) => {
    const params = new URLSearchParams();
    for (const [key, value] of Object.entries(query)) if (value !== undefined && value !== "") params.set(key, String(value));
    return request<ReferencePage>(`${doc(wsId, docId)}/references/${encodeURIComponent(catalogId)}?${params.toString()}`);
  },
  referenceAssetUrl: (wsId: string, docId: string, assetId: string) =>
    `/api${doc(wsId, docId)}/reference-assets?id=${encodeURIComponent(assetId)}`,
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
    const response = await fetch(`/api${ws(wsId)}/export`, json({ scope, mirrorBackups }));
    if (!response.ok) {
      const body = (await response.json().catch(() => null)) as { error?: string } | null;
      throw new ApiError(body?.error ?? "Export failed", response.status);
    }
    const disposition = response.headers.get("content-disposition") ?? "";
    const name = /filename="?([^";]+)"?/.exec(disposition)?.[1] ?? "savebench-export.zip";
    return { blob: await response.blob(), name };
  },
};
