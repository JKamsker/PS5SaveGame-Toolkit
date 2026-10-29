import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { actionReview, reviewValue, type SemanticChange } from "../../../api/actionReview";
import { ApiError } from "../../../api/client";
import { useDocument } from "../../../state/documentStore";
import { useDocumentContentVersion } from "../../../state/useDocumentContentVersion";
import { useSemanticJump } from "./useSemanticJump";
import { ReviewPager, useReviewPage } from "./paging";
import { useAsyncTask } from "../../ui/useAsyncTask";
import "./review.css";

export function ChangesPanel() {
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
  const changes = query.data?.changes.filter(change => `${change.label} ${change.valueId}`.toLowerCase().includes(search.toLowerCase())) ?? [];

  const page = useReviewPage(changes);
  const partial = query.data && (!query.data.complete || query.data.warnings.some(warning => /truncat/i.test(warning)));
  const refreshChanges = async (refreshDocument = false) => {
    try {
      if (refreshDocument) await doc.refresh();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "Could not refresh the document.");
    }
    await client.invalidateQueries({ queryKey: ["changes", doc.workspaceId, doc.documentId] });
  };

  const navigate = (change: SemanticChange) => submit(async isCurrent => {
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

  const revert = (valueId: string) => submit(async isCurrent => {
    if (!query.data) return;
    setError("");
    try {
      const result = await doc.mutate(() => actionReview.revert(doc.workspaceId, doc.documentId, valueId, query.data.revision));
      if (!isCurrent()) return;
      if (!result.ok) setError(result.stale ? "This review is out of date. Review refreshed changes before retrying." : result.errors.map(e => e.message).join(" "));
      // Content changes already load a new key. Only explicitly refetch unchanged results;
      // invalidating after a changed result would duplicate the new snapshot request.
      if (doc.store.valueVersion === version && doc.store.state.revision === revision) await refreshChanges();
    } catch (failure) {
      if (!isCurrent()) return;
      setError(failure instanceof Error ? failure.message : "Could not revert this change.");
      await refreshChanges(failure instanceof ApiError && failure.status === 409);
    }
  });

  return <details className="section changes-panel" open={open} onToggle={event => setOpen(event.currentTarget.open)}>
    <summary className="section__summary"><span className="section__title">Changes against original</span></summary>
    {open && <div className="section__body" aria-busy={busy}>
      <p>Changed fields only. Reverting an editable field recomputes dependent values. Use Undo for added, removed or derived fields.</p>
      <label>Find a changed field <input className="input" type="search" value={search} onChange={event => setSearch(event.target.value)} /></label>
      {query.isPending && <p role="status">Loading changes…</p>}
      {(error || query.isError) && <p role="alert">{error || query.error?.message}</p>}
      {query.isError && <button type="button" className="btn btn--small" disabled={query.isFetching} onClick={() => void query.refetch()}>Retry changes</button>}
      {partial && <p className="notice notice--Warning" role="status">Review coverage is incomplete. Only proven changes are listed; other changes may be unreviewed.</p>}
      {query.data?.warnings.map((warning, i) => <p className="notice notice--Warning" key={i}>{warning}</p>)}
      {query.data && <p aria-live="polite">{changes.length} of {query.data.changes.length} changed fields</p>}
      {query.data && query.data.changes.length === 0 && <p>{partial ? "No proven changes in the reviewed portion." : "No semantic changes."}</p>}
      {changes.length > 0 && <div className="review-scroll"><table className="review-table">
        <thead><tr><th>Field</th><th>Original</th><th>Current</th><th>Review</th></tr></thead>
        <tbody>{page.items.map(change => <tr key={change.valueId}>
          <th scope="row">{change.label}<small>{change.valueId}</small></th>
          <td>{reviewValue(change.originalValue, change.originalExists)}</td><td>{reviewValue(change.currentValue, change.currentExists)}</td>
          <td className="review-actions">
            <button type="button" className="btn btn--small" disabled={busy || query.isFetching || query.isError || !change.currentExists || (!change.target && change.path.length === 0) || query.data?.revision !== revision}
              onClick={() => void navigate(change)} aria-label={`Jump to ${change.label}`}>Jump</button>
            <button type="button" className="btn btn--small" disabled={busy || query.isFetching || query.isError || !doc.canEdit || !change.canRevert || query.data?.revision !== revision}
              onClick={() => void revert(change.valueId)} aria-label={`Revert ${change.label}`}>Revert</button>
          </td>
        </tr>)}</tbody>
      </table><ReviewPager {...page} label="Changed fields pages" /></div>}
    </div>}
  </details>;
}
