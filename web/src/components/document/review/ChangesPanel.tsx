import { ChangedFieldsTable } from "./ChangedFieldsTable";
import { useChangesPanel } from "./useChangesPanel";
import "./review.css";

export function ChangesPanel() {
  const review = useChangesPanel();
  return <details className="section changes-panel" open={review.open} onToggle={event => review.setOpen(event.currentTarget.open)}>
    <summary className="section__summary"><span className="section__title">Changes against original</span></summary>
    {review.open && <ChangesPanelBody review={review} />}
  </details>;
}

function ChangesPanelBody({ review }: { review: ReturnType<typeof useChangesPanel> }) {
  const { busy, search, setSearch, query, error, partial, changes } = review;
  return <div className="section__body" aria-busy={busy}>
    <p>Changed fields only. Reverting an editable field recomputes dependent values. Use Undo for added, removed or derived fields.</p>
    <label>Find a changed field <input className="input" type="search" value={search} onChange={event => setSearch(event.target.value)} /></label>
    {query.isPending && <p role="status">Loading changes…</p>}
    {(error || query.isError) && <p role="alert">{error || query.error?.message}</p>}
    {query.isError && <button type="button" className="btn btn--small" disabled={query.isFetching} onClick={() => void query.refetch()}>Retry changes</button>}
    {partial && <p className="notice notice--Warning" role="status">Review coverage is incomplete. Only proven changes are listed; other changes may be unreviewed.</p>}
    {query.data?.warnings.map((warning, index) => <p className="notice notice--Warning" key={index}>{warning}</p>)}
    {query.data && <p aria-live="polite">{changes.length} of {query.data.changes.length} changed fields</p>}
    {query.data && query.data.changes.length === 0 && <p>{partial ? "No proven changes in the reviewed portion." : "No semantic changes."}</p>}
    {changes.length > 0 && <ChangedFieldsTable review={review} />}
  </div>;
}
