import type { QueryClient, UseQueryResult } from "@tanstack/react-query";
import type { SaveDocument } from "../../api/protocol";
import { useCallback, useState } from "react";

const unavailable = "The document snapshot could not be refreshed.";

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
    return result.isSuccess && after > before ? undefined : result.error?.message ?? unavailable;
  } catch (failure) {
    return failure instanceof Error ? failure.message : unavailable;
  }
}
