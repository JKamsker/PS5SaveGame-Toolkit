/** Build API-relative paths with opaque workspace and document identities. */
export const workspacePath = (id: string) => `/workspaces/${encodeURIComponent(id)}`;
export const documentPath = (workspaceId: string, documentId: string) =>
  `${workspacePath(workspaceId)}/documents/${encodeURIComponent(documentId)}`;

export const postJson = (body: unknown): RequestInit => ({
  method: "POST",
  headers: { "content-type": "application/json" },
  body: JSON.stringify(body),
});
