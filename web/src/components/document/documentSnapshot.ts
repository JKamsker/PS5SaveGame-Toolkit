import { useQueryClient, type QueryClient } from "@tanstack/react-query";
import { useCallback, useRef, useState } from "react";
import { api } from "../../api/client";

const unavailable = "The document snapshot could not be refreshed.";

/** Read requirements belong to the document lifetime, including while no dialog is open. */
export function useDocumentSnapshot(workspaceId: string, documentId: string, lifetime: { active: boolean; generation: number }) {
  const queryClient = useQueryClient();
  const request = useRef(0);
  const [readState, setReadState] = useState<{ owner: typeof lifetime; status: "pending" | "required"; message?: string } | null>(null);
  const readSnapshot = useCallback(async () => {
    if (!lifetime.active) return unavailable;
    const requestId = ++request.current;
    lifetime.generation++;
    // Publish before awaiting the read: native Escape/reopen must not expose an obsolete revision.
    if (lifetime.active) setReadState(previous => ({ owner: lifetime, status: "pending",
      message: previous?.owner === lifetime ? previous.message : undefined }));
    const isCurrent = () => lifetime.active && requestId === request.current;
    const error = await readDocumentSnapshot(queryClient, workspaceId, documentId, isCurrent);
    if (isCurrent()) setReadState(error === undefined ? null : { owner: lifetime, status: "required", message: error });
    return error;
  }, [queryClient, workspaceId, documentId, lifetime]);
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

/** Only a fresh read installed by this lifetime can satisfy a snapshot requirement. */
export async function readDocumentSnapshot(client: QueryClient, workspaceId: string, documentId: string,
  isCurrent: () => boolean): Promise<string | undefined> {
  const key = ["document", workspaceId, documentId];
  const before = client.getQueryState(key)?.dataUpdateCount ?? 0;
  try {
    await client.cancelQueries({ queryKey: key, exact: true });
    if (!isCurrent()) return unavailable;
    // fetchQuery forces a fresh request after cancellation, and cancellation rejects
    // instead of resolving a refetch observer's restored cached-success result.
    await client.fetchQuery({ queryKey: key, staleTime: 0, retry: false, queryFn: async () => {
      const document = await api.document(workspaceId, documentId);
      if (!isCurrent()) throw new Error(unavailable);
      return document;
    } });
    if (!isCurrent()) return unavailable;
    const after = client.getQueryState(key)?.dataUpdateCount ?? 0;
    return after > before ? undefined : unavailable;
  } catch (failure) {
    return failure instanceof Error && failure.message ? failure.message : unavailable;
  }
}
