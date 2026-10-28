import { useEffect, useId, useRef, useState, type KeyboardEvent } from "react";
import { api, isStaleError } from "../../../api/client";
import type { ReferenceCandidate, ReferenceCandidatePage, ReferenceSpec } from "../../../api/protocol";
import { useDocument } from "../../../state/documentStore";
import type { FieldControlProps } from "../FieldControl";
import { ReferenceIcon, ReferencePreview } from "./ReferencePreview";

const PAGE_SIZE = 30;
const SEARCH_DELAY_MS = 180;

export function ReferencePicker({ valueId, actionId, value, current, onCommit, onClose }: FieldControlProps & {
  spec: ReferenceSpec; valueId: string; current?: ReferenceCandidate; onClose: () => void;
}) {
  const { workspaceId, documentId, store, refresh } = useDocument();
  const revision = store.state.revision;
  const id = useId();
  const dialog = useRef<HTMLDialogElement>(null);
  const search = useRef<HTMLInputElement>(null);
  const [q, setQ] = useState("");
  const [category, setCategory] = useState("");
  const [offset, setOffset] = useState(0);
  const [page, setPage] = useState<ReferenceCandidatePage>();
  const [active, setActive] = useState(-1);
  const [rawId, setRawId] = useState(String(value ?? ""));
  const [rawCandidate, setRawCandidate] = useState<ReferenceCandidate>();
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const lookupSequence = useRef(0);
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
    setLoading(true);
    setPage(undefined);
    setActive(-1);
    setRawCandidate(undefined);
    setError(undefined);
    const timer = setTimeout(() => {
      void api.referenceCandidates(workspaceId, documentId, { valueId, actionId, revision, q, category, offset, limit: PAGE_SIZE })
        .then((result) => { if (valid) { setPage(result); setLoading(false); } })
        .catch((failure: unknown) => {
          if (!valid) return;
          setLoading(false);
          setError(failure instanceof Error ? failure.message : "Could not load candidates.");
          if (isStaleError(failure)) { onClose(); void refresh(); }
        });
    }, SEARCH_DELAY_MS);
    return () => { valid = false; clearTimeout(timer); };
  }, [workspaceId, documentId, valueId, actionId, revision, q, category, offset, refresh]);

  const choose = async () => {
    if (!preview?.eligibility.selectable || busy) return;
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
    setBusy(true);
    setError(undefined);
    try {
      const result = await api.referenceCandidate(workspaceId, documentId, { ...candidateQuery, rawId });
      if (mounted.current && sequence === lookupSequence.current) { setActive(-1); setRawCandidate(result); }
    } catch (failure) {
      if (mounted.current) {
        setError(failure instanceof Error ? failure.message : "Could not look up the raw ID.");
        if (isStaleError(failure)) { onClose(); void refresh(); }
      }
    } finally {
      if (mounted.current && sequence === lookupSequence.current) setBusy(false);
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
      <h2 id={`${id}-title`} className="dialog__title">Choose an item</h2>
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
      </div>
      <div className="reference-picker__content">
        <div>
          <div id={`${id}-list`} role="listbox" aria-label="Candidates" aria-busy={loading} className="reference-options">
            {items.map((candidate, index) => <div key={candidate.rawId} id={`${id}-option-${index}`} role="option"
              aria-selected={active === index} aria-disabled={!candidate.eligibility.selectable}
              className={`reference-option${active === index ? " reference-option--active" : ""}`}
              onMouseDown={(event) => event.preventDefault()} onClick={() => { if (!busy) { setActive(index); setRawCandidate(undefined); search.current?.focus(); } }}>
              <ReferenceIcon candidate={candidate} workspaceId={workspaceId} documentId={documentId} />
              <span>{candidate.label ?? candidate.rawId}<small>{candidate.rawId} · {candidate.category ?? "Uncategorised"}</small>
                {candidate.eligibility.reasons.map((reason, n) => <small key={n}>{reason}</small>)}</span>
            </div>)}
          </div>
          <p role="status">{loading ? "Loading candidates…" : `${page?.total ?? 0} matches${items.length ? ` · ${offset + 1}–${offset + items.length}` : ""}`}</p>
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
    </div>
    <div className="dialog__footer">
      <button type="button" className="btn btn--quiet" disabled={busy} onClick={onClose}>Cancel</button>
      <button type="button" className="btn btn--primary" disabled={busy || !preview?.eligibility.selectable} onClick={() => void choose()}>Use selected item</button>
    </div>
  </dialog>;
}
