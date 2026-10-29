import { useRef } from "react";
import { ChangedFieldsTable } from "./ChangedFieldsTable";
import { useChangesPanel } from "./useChangesPanel";
import "./review.css";

export function ChangesPanel() {
  const review = useChangesPanel();
  const summary = useRef<HTMLElement>(null);
  return <details className="section changes-panel" open={review.open} onToggle={event => review.setOpen(event.currentTarget.open)}>
    <summary ref={summary} className="section__summary"><span className="section__title">Changes against original</span></summary>
    {review.open && <ChangesPanelBody review={review} focusSummary={() => summary.current?.focus()} />}
  </details>;
}

function ChangesPanelBody({ review, focusSummary }: { review: ReturnType<typeof useChangesPanel>; focusSummary: () => void }) {
  const { busy, search, setSearch, query, error, partial, changes } = review;
  const searchInput = useRef<HTMLInputElement>(null);
  return <div className="section__body">
    <p>Changed fields only. Reverting an editable field recomputes dependent values. Use Undo for added, removed or derived fields.</p>
    <label>Find a changed field <input ref={searchInput} className="input" type="search" value={search} onChange={event => setSearch(event.target.value)} /></label>
    <p role="status" aria-label="Changes review" aria-atomic="true">{busy ? "Updating changes…" : query.isFetching ? "Loading changes…" : query.data ? `${changes.length} of ${query.data.changes.length} changed fields` : ""}</p>
    {(error || query.isError) && <p role="alert">{error || query.error?.message}</p>}
    {query.isError && <button type="button" className="btn btn--small" disabled={query.isFetching} onClick={event => {
      if (document.activeElement === event.currentTarget) searchInput.current?.focus();
      void query.refetch();
    }}>Retry changes</button>}
    {partial && <p className="notice notice--Warning" role="status">
      Review coverage is incomplete. Only proven changes are listed; other changes may be unreviewed.
      {query.data?.coverage && <> Examined {query.data.coverage.examinedCount}; omitted {query.data.coverage.omittedCountIsLowerBound ? "at least " : ""}{query.data.coverage.omittedCount} within a {query.data.coverage.budgetBytes}-byte budget.</>}
    </p>}
    {query.data?.warnings.map((warning, index) => <p className="notice notice--Warning" key={index}>{warning}</p>)}
    {query.data && query.data.changes.length === 0 && <p>{partial ? "No proven changes in the reviewed portion." : "No semantic changes."}</p>}
    {changes.length > 0 && <ChangedFieldsTable review={review} focusSummary={focusSummary} />}
  </div>;
}
