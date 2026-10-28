import type { FieldError, SaveDocument } from "./protocol";
import { request } from "./client";

export interface GroupMember {
  role: string;
  fileId: string;
  relativePath: string;
  sha256: string;
  isBackup: boolean;
}

export interface GroupOffer {
  token: string;
  pluginId: string;
  title: string;
  members: GroupMember[];
  errors: FieldError[];
}

const workspacePath = (id: string) => `/workspaces/${encodeURIComponent(id)}`;

export const groupApi = {
  propose: (workspaceId: string, fileId: string, pluginId: string) => request<GroupOffer[]>(
    `${workspacePath(workspaceId)}/files/${encodeURIComponent(fileId)}/group-proposals?pluginId=${encodeURIComponent(pluginId)}`,
  ),
  open: (workspaceId: string, proposalToken: string, releaseCleanDocumentId?: string) => request<SaveDocument>(
    `${workspacePath(workspaceId)}/document-groups`, {
      method: "POST", headers: { "content-type": "application/json" },
      body: JSON.stringify({ proposalToken, releaseCleanDocumentId }),
    },
  ),
};
