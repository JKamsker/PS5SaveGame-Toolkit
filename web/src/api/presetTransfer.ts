import type { PresetCompatibilitySummary, ToolkitPreset } from "./protocol";
import { request } from "./client";
import { ApiError, fetchApi, readJson, responseError } from "./http";
import { documentPath, postJson } from "./requests";

export const MAX_PRESET_PACKAGE_BYTES = 512 * 1024;
export interface ImportTarget { id: string; name: string }
export interface ImportItem {
  index: number; name: string; compatibility: ToolkitPreset["compatibility"]; actionCount: number;
  conflicts: ImportTarget[]; repeatedPackageName: boolean; resultingName?: string; replacesName?: string;
}
export interface ImportPreview {
  token: string; schemaVersion: string; provenance: { producer: string; exportedAt: string };
  items: ImportItem[]; readyToCommit: boolean; expiresAt: string;
}
export interface ImportChoice { index: number; decision: "keepBoth" | "replace"; replaceId?: string }

const path = "/presets/transfer";
export const presetTransfer = {
  preview: (file: File, signal?: AbortSignal) => {
    if (file.size > MAX_PRESET_PACKAGE_BYTES) return Promise.reject(new ApiError("Choose a preset package no larger than 512 KiB.", 413));
    // Send the original bytes: JSON.parse would round untagged large/exact numbers.
    return request<ImportPreview>(`${path}/preview`, { method: "POST", body: file, headers: { "content-type": "application/json" }, signal });
  },
  compatibility: (workspaceId: string, documentId: string, token: string) =>
    request<{ index: number; compatibility: PresetCompatibilitySummary }[]>(`${documentPath(workspaceId, documentId)}/presets/transfer/compatibility`, postJson({ token })),
  resolve: (token: string, choices: ImportChoice[]) => request<ImportPreview>(`${path}/resolve`, postJson({ token, choices })),
  commit: (token: string) => request<{ imported: ImportTarget[] }>(`${path}/commit`, postJson({ token })),
  cancel: (token: string) => request<void>(`${path}/previews/${encodeURIComponent(token)}`, { method: "DELETE" }),
  download: (ids: string[]) => fetchApi(`${path}/export`, postJson({ ids }), async response => {
    if (!response.ok) throw responseError(response.status, await readJson(response));
    const blob = await response.blob();
    // Consume and verify the download before announcing success. Never parse package JSON in JS.
    if (blob.size === 0 || blob.size > MAX_PRESET_PACKAGE_BYTES || !response.headers.get("content-type")?.includes("application/json"))
      throw new ApiError("The preset download was incomplete. Retry the export.", response.status);
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url; anchor.download = "Toolkit presets.toolkit-presets.json";
    document.body.append(anchor); anchor.click(); anchor.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }),
};
