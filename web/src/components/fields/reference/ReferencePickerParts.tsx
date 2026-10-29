import type { ReferenceSortKey } from "../../../api/protocol";
import { ReferenceIcon } from "./ReferencePreview";
import { REFERENCE_PAGE_SIZE, type ReferencePickerController } from "./useReferencePicker";

export function ReferenceFilters({ picker }: { picker: ReferencePickerController }) {
  const { id, search, active, q, setQ, category, setCategory, sortBy, setSortBy,
    descending, setDescending, sortKeys, setOffset, page, navigate } = picker;
  return <div className="reference-filters">
    <label>Search
      <input ref={search} className="input" role="combobox" aria-autocomplete="list" aria-expanded="true" aria-controls={`${id}-list`}
        aria-activedescendant={active >= 0 ? `${id}-option-${active}` : undefined} value={q}
        onChange={(event) => { setQ(event.target.value); setOffset(0); }} onKeyDown={navigate} />
    </label>
    <label>Category
      <select className="input" value={category} onChange={(event) => { setCategory(event.target.value); setOffset(0); }}>
        <option value="">All categories</option>
        {(page?.categories ?? (category ? [category] : [])).map((name) => <option key={name}>{name}</option>)}
      </select>
    </label>
    {sortKeys.length > 0 && <>
      <label>Sort by
        <select className="input" value={sortBy} onChange={(event) => { setSortBy(event.target.value as ReferenceSortKey | ""); setOffset(0); }}>
          <option value="">Catalog order</option>
          {sortKeys.map((key) => <option key={key} value={key}>{key}</option>)}
        </select>
      </label>
      <label><input type="checkbox" checked={descending} disabled={!sortBy}
        onChange={(event) => { setDescending(event.target.checked); setOffset(0); }} />Descending</label>
    </>}
  </div>;
}

export function ReferenceOptions({ picker, browseOnly }: { picker: ReferencePickerController; browseOnly: boolean }) {
  const { id, search, items, active, setActive, setRawCandidate, workspaceId, documentId,
    loading, busy, loadFailed, page, offset, setOffset } = picker;
  return <div>
    <div id={`${id}-list`} role="listbox" aria-label="Candidates" aria-busy={loading} className="reference-options">
      {/* eslint-disable-next-line jsx-a11y/click-events-have-key-events, jsx-a11y/interactive-supports-focus -- Combobox focus and aria-activedescendant provide all option keyboard navigation. */}
      {items.map((candidate, index) => <div key={candidate.rawId} id={`${id}-option-${index}`} role="option"
        aria-selected={active === index} aria-disabled={browseOnly ? undefined : !candidate.eligibility.selectable}
        className={`reference-option${active === index ? " reference-option--active" : ""}`}
        onMouseDown={(event) => event.preventDefault()} onClick={() => { if (!busy) { setActive(index); setRawCandidate(undefined); search.current?.focus(); } }}>
        <ReferenceIcon candidate={candidate} workspaceId={workspaceId} documentId={documentId} />
        <span>{candidate.label ?? candidate.rawId}<small>{candidate.rawId} · {candidate.category ?? "Uncategorised"}</small>
          {candidate.eligibility.reasons.map((reason, n) => <small key={n}>{reason}</small>)}</span>
      </div>)}
    </div>
    <p role="status">{loading ? "Loading candidates…" : loadFailed ? "Candidates unavailable" : `${page?.total ?? 0} matches${items.length ? ` · ${offset + 1}–${offset + items.length}` : ""}`}</p>
    <div className="reference-paging">
      <button type="button" className="btn btn--small" disabled={busy || loading || offset === 0} onClick={() => setOffset(Math.max(0, offset - REFERENCE_PAGE_SIZE))}>Previous</button>
      <button type="button" className="btn btn--small" disabled={busy || loading || offset + REFERENCE_PAGE_SIZE >= (page?.total ?? 0)} onClick={() => setOffset(offset + REFERENCE_PAGE_SIZE)}>Next</button>
    </div>
  </div>;
}

export function ReferenceRawLookup({ picker }: { picker: ReferencePickerController }) {
  const { rawId, setRawId, setRawCandidate, busy, previewRaw } = picker;
  return <div className="reference-raw">
    <label>Raw ID<input className="input mono" value={rawId} disabled={busy} onChange={(event) => { setRawId(event.target.value); setRawCandidate(undefined); }} /></label>
    <button type="button" className="btn btn--small" disabled={busy} onClick={() => void previewRaw()}>Preview raw ID</button>
  </div>;
}
