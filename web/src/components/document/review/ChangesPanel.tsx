import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { actionReview, reviewValue } from "../../../api/actionReview";
import { useDocument, useDocumentState } from "../../../state/documentStore";
import { useReviewNavigation } from "./navigation";
import { ReviewPager, useReviewPage } from "./paging";
import "./review.css";

export function ChangesPanel() {
  const doc = useDocument();
  const { state, version } = useDocumentState(doc.store);
  const { jump } = useReviewNavigation();
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const query = useQuery({ queryKey: ["changes", doc.workspaceId, doc.documentId, version, doc.structureVersion],
    queryFn: () => actionReview.changes(doc.workspaceId, doc.documentId), enabled: open });
  const changes = query.data?.changes.filter(change => `${change.label} ${change.valueId}`.toLowerCase().includes(search.toLowerCase())) ?? [];

  const page = useReviewPage(changes);

  const revert = async (valueId: string) => {
    if (!query.data) return;
    setBusy(true);
    setError("");
    try {
      const result = await actionReview.revert(doc.workspaceId, doc.documentId, valueId, query.data.revision);
      await doc.acceptResult(result);
      if (!result.ok) setError(result.errors.map(e => e.message).join(" "));
      await query.refetch();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "Could not revert this change.");
    } finally {
      setBusy(false);
    }
  };

  return <details className="section changes-panel" open={open} onToggle={event => setOpen(event.currentTarget.open)}>
    <summary className="section__summary"><span className="section__title">Changes against original</span></summary>
    {open && <div className="section__body">
      <p>Changed fields only. Reverting an editable field recomputes dependent values. Use Undo for added, removed or derived fields.</p>
      <label>Find a changed field <input className="input" type="search" value={search} onChange={event => setSearch(event.target.value)} /></label>
      {query.isPending && <p role="status">Loading changes…</p>}
      {(error || query.isError) && <p role="alert">{error || query.error?.message}</p>}
      {query.data?.warnings.map((warning, i) => <p className="notice notice--Warning" key={i}>{warning}</p>)}
      {query.data && <p aria-live="polite">{changes.length} of {query.data.changes.length} changed fields</p>}
      {query.data && query.data.changes.length === 0 && <p>No semantic changes.</p>}
      {changes.length > 0 && <div className="review-scroll"><table className="review-table">
        <thead><tr><th>Field</th><th>Original</th><th>Current</th><th>Review</th></tr></thead>
        <tbody>{page.items.map(change => <tr key={change.valueId}>
          <th scope="row">{change.label}<small>{change.valueId}</small></th>
          <td>{reviewValue(change.originalValue, change.originalExists)}</td><td>{reviewValue(change.currentValue, change.currentExists)}</td>
          <td className="review-actions">
            <button type="button" className="btn btn--small" disabled={!change.currentExists || change.path.length === 0} onClick={() => jump(change)} aria-label={`Jump to ${change.label}`}>Jump</button>
            <button type="button" className="btn btn--small" disabled={busy || !doc.canEdit || !change.canRevert || query.data?.revision !== state.revision}
              onClick={() => void revert(change.valueId)} aria-label={`Revert ${change.label}`}>Revert</button>
          </td>
        </tr>)}</tbody>
      </table><ReviewPager {...page} label="Changed fields pages" /></div>}
    </div>}
  </details>;
}
