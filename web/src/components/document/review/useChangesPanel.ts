import { useState } from "react";
import { useQuery, useQueryClient, type UseQueryResult } from "@tanstack/react-query";
import { actionReview, type SemanticChange, type SemanticChangeLog } from "../../../api/actionReview";
import { ApiError } from "../../../api/client";
import { useDocument } from "../../../state/documentStore";
import { useDocumentContentVersion } from "../../../state/useDocumentContentVersion";
import { useAsyncTask } from "../../ui/useAsyncTask";
import { useSemanticJump } from "./useSemanticJump";
import { useReviewPage } from "./paging";

interface ChangeCommandContext {
  doc: ReturnType<typeof useDocument>;
  query: UseQueryResult<SemanticChangeLog>;
  submit: ReturnType<typeof useAsyncTask>["run"];
  revision: ReturnType<typeof useDocumentContentVersion>["revision"];
  version: number;
  setError: (message: string) => void;
  refreshChanges: (refreshDocument?: boolean) => Promise<void>;
}

function createChangeNavigation(context: ChangeCommandContext, jump: ReturnType<typeof useSemanticJump>) {
  const { query, submit, setError, refreshChanges } = context;
  return (change: SemanticChange) => submit(async isCurrent => {
    if (!query.data) return;
    setError("");
    try {
      await jump(change, query.data.revision, isCurrent);
    } catch (failure) {
      if (!isCurrent()) return;
      setError(failure instanceof Error ? failure.message : "Could not jump to this change.");
      await refreshChanges(failure instanceof ApiError && failure.status === 409);
    }
  });
}

function createChangeRevert(context: ChangeCommandContext) {
  const { doc, query, submit, setError, revision, version, refreshChanges } = context;
  return (valueId: string) => submit(async isCurrent => {
    if (!query.data) return;
    setError("");
    try {
      const result = await doc.mutate(() => actionReview.revert(doc.workspaceId, doc.documentId, valueId, query.data.revision));
      if (!isCurrent()) return;
      if (!result.ok) setError(result.stale ? "This review is out of date. Review refreshed changes before retrying." : result.errors.map(error => error.message).join(" "));
      // A changed result already loads a new key. Refetch only unchanged results to avoid
      // requesting the new snapshot twice.
      if (doc.store.valueVersion === version && doc.store.state.revision === revision) await refreshChanges();
    } catch (failure) {
      if (!isCurrent()) return;
      setError(failure instanceof Error ? failure.message : "Could not revert this change.");
      await refreshChanges(failure instanceof ApiError && failure.status === 409);
    }
  });
}

export function useChangesPanel() {
  const doc = useDocument();
  const { revision, version } = useDocumentContentVersion(doc.store);
  const jump = useSemanticJump();
  const client = useQueryClient();
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
  const { busy, run: submit } = useAsyncTask(doc.store);
  const [error, setError] = useState("");
  const query = useQuery({ queryKey: ["changes", doc.workspaceId, doc.documentId, revision, version, doc.structureVersion],
    queryFn: () => actionReview.changes(doc.workspaceId, doc.documentId), enabled: open });
  const changes = query.data?.changes.filter(change => {
    const searchable = [change.label, change.valueId, change.canonicalValueId, change.member?.role,
      change.member?.relativePath, change.member?.groupTitle, ...(change.aliases?.flatMap(alias => [alias.label, alias.valueId]) ?? [])]
      .filter(Boolean).join(" ").toLowerCase();
    return searchable.includes(search.trim().toLowerCase());
  }) ?? [];
  const page = useReviewPage(changes);
  const partial = query.data && (!query.data.complete
    || query.data.warnings.some(warning => /truncat/i.test(warning))
    || (query.data.coverage?.omittedCount ?? 0) > 0);
  const refreshChanges = async (refreshDocument = false) => {
    try {
      if (refreshDocument) await doc.refresh();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "Could not refresh the document.");
    }
    await client.invalidateQueries({ queryKey: ["changes", doc.workspaceId, doc.documentId] });
  };
  const commands = { doc, query, submit, setError, revision, version, refreshChanges };
  return { open, setOpen, search, setSearch, busy, error, query, changes, page, partial, canEdit: doc.canEdit,
    disabled: busy || query.isFetching || query.isError || query.data?.revision !== revision,
    navigate: createChangeNavigation(commands, jump), revert: createChangeRevert(commands) };
}
