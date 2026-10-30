import { useQueryClient, type QueryClient, type UseQueryResult } from "@tanstack/react-query";
import type { SaveDocument } from "../../api/protocol";
import { useCallback, useState } from "react";

const unavailable = "The document snapshot could not be refreshed.";

/** Read requirements belong to the document lifetime, including while no dialog is open. */
export function useDocumentSnapshot(workspaceId: string, documentId: string, lifetime: { active: boolean },
  refetch: UseQueryResult<SaveDocument, Error>["refetch"]) {
  const queryClient = useQueryClient();
  const [readState, setReadState] = useState<{ owner: typeof lifetime; status: "pending" | "required"; message?: string } | null>(null);
  const readSnapshot = useCallback(async () => {
    // Publish before awaiting the read: native Escape/reopen must not expose an obsolete revision.
    if (lifetime.active) setReadState(previous => ({ owner: lifetime, status: "pending",
      message: previous?.owner === lifetime ? previous.message : undefined }));
    const error = await readDocumentSnapshot(queryClient, refetch, workspaceId, documentId);
    if (lifetime.active) setReadState(error === undefined ? null : { owner: lifetime, status: "required", message: error });
    return error;
  }, [queryClient, refetch, workspaceId, documentId, lifetime]);
  const currentRead = readState?.owner === lifetime ? readState : undefined;
  return { readSnapshot, snapshotError: currentRead?.message, snapshotRefresh: currentRead?.status };
}

/** Only the current modal owns read-error announcements; older cleanup cannot retire a newer owner. */
export function useSnapshotErrorOwner(document: object) {
  const [owner, setOwner] = useState<{ document: object } | null>(null);
  const claim = useCallback(() => {
    const next = { document };
    setOwner(next);
    return () => setOwner(current => current === next ? null : current);
  }, [document]);
  return { claim, inDialog: owner?.document === document };
}

/** A cancelled refetch can return cached success; require proof that a snapshot was installed. */
export async function readDocumentSnapshot(client: QueryClient, refetch: UseQueryResult<SaveDocument, Error>["refetch"],
  workspaceId: string, documentId: string): Promise<string | undefined> {
  const key = ["document", workspaceId, documentId];
  const before = client.getQueryState(key)?.dataUpdateCount ?? 0;
  try {
    const result = await refetch();
    const after = client.getQueryState(key)?.dataUpdateCount ?? 0;
    return result.isSuccess && after > before ? undefined : result.error?.message || unavailable;
  } catch (failure) {
    return failure instanceof Error && failure.message ? failure.message : unavailable;
  }
}
