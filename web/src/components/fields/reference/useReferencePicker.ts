import { useEffect, useId, useRef, useState, type KeyboardEvent } from "react";
import { api, isStaleError } from "../../../api/client";
import type { ReferenceCandidate, ReferenceCandidatePage, ReferenceSortKey, ReferenceSpec } from "../../../api/protocol";
import { useDocument } from "../../../state/documentStore";
import type { FieldControlProps } from "../FieldControl";

export const REFERENCE_PAGE_SIZE = 30;
const SEARCH_DELAY_MS = 180;

export type ReferencePickerProps = FieldControlProps & {
  spec: ReferenceSpec; valueId: string; current?: ReferenceCandidate; browseOnly?: boolean; onClose: () => void;
};

type PickerState = ReturnType<typeof usePickerState>;
type CandidateIdentity = {
  workspaceId: string; documentId: string; valueId: string; actionId?: string; revision: number | undefined; structureVersion: number;
};

function usePickerState(value: FieldControlProps["value"]) {
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
  const lookupSequenceRef = useRef(0);
  const lookupPendingRef = useRef(false);
  const selectionPendingRef = useRef(false);
  const mountedRef = useRef(true);
  return {
    q, setQ, category, setCategory, sortBy, setSortBy, descending, setDescending, sortKeys, setSortKeys,
    offset, setOffset, page, setPage, active, setActive, rawId, setRawId, rawCandidate, setRawCandidate,
    loading, setLoading, busy, setBusy, error, setError, loadFailed, setLoadFailed,
    needsRefresh, setNeedsRefresh, reload, setReload, lookupSequenceRef, lookupPendingRef, selectionPendingRef, mountedRef,
  };
}

function useCandidateSearch(identity: CandidateIdentity, state: PickerState) {
  const { workspaceId, documentId, valueId, actionId, revision, structureVersion } = identity;
  const { q, category, sortBy, descending, offset, reload, lookupSequenceRef, lookupPendingRef,
    setBusy, setLoading, setPage, setActive, setRawCandidate, setError, setLoadFailed, setSortKeys, setNeedsRefresh } = state;
  useEffect(() => {
    let valid = true;
    // A raw lookup is bound to the same revision and search lifetime as the candidate search.
    lookupSequenceRef.current++;
    if (lookupPendingRef.current) { lookupPendingRef.current = false; setBusy(false); }
    setLoading(true);
    setPage(undefined);
    setActive(-1);
    setRawCandidate(undefined);
    setError(undefined);
    setLoadFailed(false);
    const timer = setTimeout(() => {
      void api.referenceCandidates(workspaceId, documentId, { valueId, actionId, revision, q, category, sortBy: sortBy || undefined, descending, offset, limit: REFERENCE_PAGE_SIZE })
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
  }, [workspaceId, documentId, valueId, actionId, revision, structureVersion, q, category, sortBy, descending, offset, reload,
    lookupSequenceRef, lookupPendingRef, setBusy, setLoading, setPage, setActive, setRawCandidate, setError, setLoadFailed, setSortKeys, setNeedsRefresh]);
}

function useChoiceRefresh(state: PickerState, refresh: () => Promise<void>) {
  const { selectionPendingRef, lookupPendingRef, mountedRef, setBusy, setReload, setError } = state;
  return async () => {
    if (selectionPendingRef.current || lookupPendingRef.current) return;
    selectionPendingRef.current = true;
    setBusy(true);
    try {
      await refresh();
      if (!mountedRef.current) return;
      setReload(previous => previous + 1);
    } catch (failure) {
      if (mountedRef.current) setError(failure instanceof Error ? failure.message : "Could not refresh choices. Try again.");
    } finally {
      selectionPendingRef.current = false;
      if (mountedRef.current) setBusy(false);
    }
  };
}

function useChoiceCommit(state: PickerState, preview: ReferenceCandidate | undefined, props: ReferencePickerProps) {
  const { busy, loading, needsRefresh, selectionPendingRef, lookupPendingRef, mountedRef, setRawId, setBusy, setError } = state;
  const { browseOnly = false, value, onCommit, onClose } = props;
  return async () => {
    if (browseOnly || !preview?.eligibility.selectable || busy || loading || needsRefresh || selectionPendingRef.current || lookupPendingRef.current) return;
    selectionPendingRef.current = true;
    // Retain the intended ID even if stale recovery replaces the candidate page.
    setRawId(preview.rawId);
    setBusy(true);
    setError(undefined);
    try {
      const accepted = preview.rawId === String(value) || await onCommit(preview.rawId);
      if (!mountedRef.current) return;
      if (accepted) onClose();
      else setError("The server rejected this selection. Choose another item or refresh the document.");
    } catch (failure) {
      if (mountedRef.current) setError(failure instanceof Error ? failure.message : "Could not save the selection.");
    } finally {
      selectionPendingRef.current = false;
      if (mountedRef.current) setBusy(false);
    }
  };
}

function useRawLookup(identity: CandidateIdentity, state: PickerState) {
  const { workspaceId, documentId, valueId, actionId, revision } = identity;
  const { rawId, lookupSequenceRef, lookupPendingRef, mountedRef, setBusy, setError, setActive, setRawCandidate, setNeedsRefresh } = state;
  return async () => {
    const sequence = ++lookupSequenceRef.current;
    lookupPendingRef.current = true;
    setBusy(true);
    setError(undefined);
    try {
      const result = await api.referenceCandidate(workspaceId, documentId, { valueId, actionId, revision, rawId });
      if (mountedRef.current && sequence === lookupSequenceRef.current) { setActive(-1); setRawCandidate(result); }
    } catch (failure) {
      if (mountedRef.current && sequence === lookupSequenceRef.current) {
        setError(failure instanceof Error ? failure.message : "Could not look up the raw ID.");
        if (isStaleError(failure)) setNeedsRefresh(true);
      }
    } finally {
      if (mountedRef.current && sequence === lookupSequenceRef.current) { lookupPendingRef.current = false; setBusy(false); }
    }
  };
}

export function useReferencePicker(props: ReferencePickerProps) {
  const { workspaceId, documentId, store, structureVersion, refresh } = useDocument();
  const identity = { workspaceId, documentId, structureVersion, valueId: props.valueId, actionId: props.actionId, revision: store.state.revision };
  const id = useId();
  const dialog = useRef<HTMLDialogElement>(null);
  const search = useRef<HTMLInputElement>(null);
  const state = usePickerState(props.value);
  const { mountedRef, page, active, rawCandidate, loading, setActive, setRawCandidate } = state;
  const items = page?.items ?? [];
  const preview = rawCandidate ?? items[active] ?? props.current;
  useEffect(() => {
    mountedRef.current = true;
    dialog.current?.showModal();
    search.current?.focus();
    return () => { mountedRef.current = false; };
  }, [mountedRef]);
  useCandidateSearch(identity, state);
  const refreshChoices = useChoiceRefresh(state, refresh);
  const choose = useChoiceCommit(state, preview, props);
  const previewRaw = useRawLookup(identity, state);
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
  return { ...state, workspaceId, documentId, id, dialog, search, items, preview, navigate, refreshChoices, choose, previewRaw };
}

export type ReferencePickerController = ReturnType<typeof useReferencePicker>;

