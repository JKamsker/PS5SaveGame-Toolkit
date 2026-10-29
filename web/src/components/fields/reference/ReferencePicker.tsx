import { useEffect, useId, useRef, useState, type KeyboardEvent } from "react";
import { api, isStaleError } from "../../../api/client";
import type { ReferenceCandidate, ReferenceCandidatePage, ReferenceSortKey, ReferenceSpec } from "../../../api/protocol";
import { useDocument } from "../../../state/documentStore";
import type { FieldControlProps } from "../FieldControl";
import { ReferenceIcon, ReferencePreview } from "./ReferencePreview";

const PAGE_SIZE = 30;
const SEARCH_DELAY_MS = 180;

export function ReferencePicker({ valueId, actionId, value, current, browseOnly = false, onCommit, onClose }: FieldControlProps & {
  spec: ReferenceSpec; valueId: string; current?: ReferenceCandidate; browseOnly?: boolean; onClose: () => void;
}) {
  const { workspaceId, documentId, store, structureVersion, refresh } = useDocument();
  const revision = store.state.revision;
  const id = useId();
  const dialog = useRef<HTMLDialogElement>(null);
  const search = useRef<HTMLInputElement>(null);
  const [q, setQ] = useState("");
  const [category, setCategory] = useState("");
  const [sortBy, setSortBy] = useState<ReferenceSortKey | "">("");
  const [descending, setDescending] = useState(false);
  const [sortKeys, setSortKeys] = useState<ReferenceSortKey[]>([]);
  const [offset, setOffset] = useState(0);
  const [page, setPage] = useState<ReferenceCandidatePage>();
  const [active, setActive] = useState(-1);
  const [rawId, setRawId] = useState(String(value ?? ""));
  const [rawCandidate, setRawCandidate] = useState<ReferenceCandidate>();
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const [loadFailed, setLoadFailed] = useState(false);
  const [needsRefresh, setNeedsRefresh] = useState(false);
  const [reload, setReload] = useState(0);
  const lookupSequence = useRef(0);
  const lookupPending = useRef(false);
  const mounted = useRef(true);
  const items = page?.items ?? [];
  const preview = rawCandidate ?? items[active] ?? current;
  const candidateQuery = { valueId, actionId, revision };

  useEffect(() => {
    mounted.current = true;
    dialog.current?.showModal();
    search.current?.focus();
    return () => { mounted.current = false; };
  }, []);

  useEffect(() => {
    let valid = true;
    // A raw lookup is bound to the same revision as the candidate search.
    lookupSequence.current++;
    if (lookupPending.current) { lookupPending.current = false; setBusy(false); }
    setLoading(true);
    setPage(undefined);
    setActive(-1);
    setRawCandidate(undefined);
    setError(undefined);
    setLoadFailed(false);
    const timer = setTimeout(() => {
      void api.referenceCandidates(workspaceId, documentId, { valueId, actionId, revision, q, category, sortBy: sortBy || undefined, descending, offset, limit: PAGE_SIZE })
        .then((result) => { if (valid) { setSortKeys(result.sortKeys ?? []); setPage(result); setLoading(false); setNeedsRefresh(false); } })
        .catch((failure: unknown) => {
          if (!valid) return;
          setLoading(false);
          setError(failure instanceof Error ? failure.message : "Could not load candidates.");
          setLoadFailed(true);
          if (isStaleError(failure)) setNeedsRefresh(true);
        });
    }, SEARCH_DELAY_MS);
    return () => { valid = false; clearTimeout(timer); };
  }, [workspaceId, documentId, valueId, actionId, revision, structureVersion, q, category, sortBy, descending, offset, reload]);

  const refreshChoices = async () => {
    setBusy(true);
    try {
      await refresh();
      setReload(previous => previous + 1);
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "Could not refresh choices. Try again.");
    } finally {
      if (mounted.current) setBusy(false);
    }
  };

  const choose = async () => {
    if (browseOnly || !preview?.eligibility.selectable || busy || loading || needsRefresh) return;
    // Retain the intended ID even if stale recovery replaces the candidate page.
    setRawId(preview.rawId);
    setBusy(true);
    setError(undefined);
    try {
      if (preview.rawId === String(value) || await onCommit(preview.rawId)) onClose();
      else setError("The server rejected this selection. Choose another item or refresh the document.");
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "Could not save the selection.");
    } finally {
      if (mounted.current) setBusy(false);
    }
  };

  const previewRaw = async () => {
    const sequence = ++lookupSequence.current;
    lookupPending.current = true;
    setBusy(true);
    setError(undefined);
    try {
      const result = await api.referenceCandidate(workspaceId, documentId, { ...candidateQuery, rawId });
      if (mounted.current && sequence === lookupSequence.current) { setActive(-1); setRawCandidate(result); }
    } catch (failure) {
      if (mounted.current && sequence === lookupSequence.current) {
        setError(failure instanceof Error ? failure.message : "Could not look up the raw ID.");
        if (isStaleError(failure)) setNeedsRefresh(true);
      }
    } finally {
      if (mounted.current && sequence === lookupSequence.current) { lookupPending.current = false; setBusy(false); }
    }
  };

  const navigate = (event: KeyboardEvent<HTMLInputElement>) => {
    if (["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key) && items.length) {
      event.preventDefault();
      setRawCandidate(undefined);
      const next = event.key === "Home" ? 0 : event.key === "End" ? items.length - 1
        : active < 0 ? (event.key === "ArrowDown" ? 0 : items.length - 1)
        : (active + (event.key === "ArrowDown" ? 1 : -1) + items.length) % items.length;
      setActive(next);
      document.getElementById(`${id}-option-${next}`)?.scrollIntoView?.({ block: "nearest" });
    } else if (event.key === "Enter") {
      event.preventDefault();
      if (!loading && active >= 0) void choose();
    }
  };

  return <dialog ref={dialog} className="dialog reference-picker" aria-labelledby={`${id}-title`} onCancel={(event) => { event.preventDefault(); if (!busy) onClose(); }}>
    <div className="dialog__body">
      <h2 id={`${id}-title`} className="dialog__title">{browseOnly ? "Browse items" : "Choose an item"}</h2>
      {browseOnly && <p role="note">Read-only browsing. Item details can be inspected; the saved ID cannot be changed.</p>}
      <p>Current ID: <code>{String(value ?? "")}</code>. Search by name or raw ID.</p>
      <div className="reference-filters">
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
      </div>
      <div className="reference-picker__content">
        <div>
          <div id={`${id}-list`} role="listbox" aria-label="Candidates" aria-busy={loading} className="reference-options">
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
            <button type="button" className="btn btn--small" disabled={busy || loading || offset === 0} onClick={() => setOffset(Math.max(0, offset - PAGE_SIZE))}>Previous</button>
            <button type="button" className="btn btn--small" disabled={busy || loading || offset + PAGE_SIZE >= (page?.total ?? 0)} onClick={() => setOffset(offset + PAGE_SIZE)}>Next</button>
          </div>
        </div>
        {preview && <ReferencePreview candidate={preview} workspaceId={workspaceId} documentId={documentId} />}
      </div>
      <div className="reference-raw">
        <label>Raw ID<input className="input mono" value={rawId} disabled={busy} onChange={(event) => { setRawId(event.target.value); setRawCandidate(undefined); }} /></label>
        <button type="button" className="btn btn--small" disabled={busy} onClick={() => void previewRaw()}>Preview raw ID</button>
      </div>
      {error && <p role="alert" className="field__error">{error}</p>}
      {needsRefresh ? <button type="button" className="btn btn--small" disabled={busy || loading} onClick={() => void refreshChoices()}>Refresh choices</button>
        : loadFailed && <button type="button" className="btn btn--small" disabled={busy || loading} onClick={() => setReload(previous => previous + 1)}>Retry candidates</button>}
    </div>
    <div className="dialog__footer">
      <button type="button" className="btn btn--quiet" disabled={busy} onClick={onClose}>{browseOnly ? "Close" : "Cancel"}</button>
      {!browseOnly && <button type="button" className="btn btn--primary" disabled={busy || loading || needsRefresh || !preview?.eligibility.selectable} onClick={() => void choose()}>Use selected item</button>}
    </div>
  </dialog>;
}
