import { useEffect, useRef, useState } from "react";
import type { FieldValue } from "../../api/protocol";
import { parseDraft, sameValue, toDraft } from "../../lib/values";
import type { FieldControlProps } from "./FieldControl";

/** A new keystroke or Escape retires validation from an older in-flight submission. */
function useDraftSubmission(onCommit: FieldControlProps["onCommit"]) {
  const [invalid, setInvalid] = useState(false);
  const pending = useRef<{ value: FieldValue; generation: number } | null>(null);
  const generation = useRef(0);
  const mounted = useRef(false);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  const invalidate = () => { generation.current++; };
  const submit = async (next: FieldValue) => {
    if (pending.current && sameValue(pending.current.value, next)) return;
    const request = { value: next, generation: ++generation.current };
    pending.current = request;
    try {
      const accepted = await onCommit(next);
      if (mounted.current && request.generation === generation.current) setInvalid(!accepted);
    } finally {
      if (pending.current === request) pending.current = null;
    }
  };
  return { invalid, setInvalid, invalidate, submit };
}

/** Owns typed drafts independently of snapshots, preserving newer and rejected edits. */
export function useTextualDraft({ spec, value, onCommit, onLocalError, onDraftDiscard, invalid: parentInvalid }: FieldControlProps) {
  const [draft, setDraft] = useState(() => String(toDraft(spec, value)));
  const previousValue = useRef(draft);
  const { invalid, setInvalid, invalidate, submit } = useDraftSubmission(onCommit);
  useEffect(() => {
    const previous = previousValue.current;
    const next = String(toDraft(spec, value));
    previousValue.current = next;
    // An acknowledgement must not replace a newer/rejected draft. Escape explicitly does.
    setDraft(current => current === previous ? next : current);
  }, [spec, value]);

  const commit = async () => {
    const parsed = parseDraft(spec, draft);
    if (!parsed.ok) {
      setInvalid(true);
      onLocalError?.(parsed.error);
      return;
    }
    setInvalid(false);
    onLocalError?.(undefined);
    if (sameValue(parsed.value, value)) return;
    await submit(parsed.value);
  };
  const revert = () => {
    invalidate();
    const saved = String(toDraft(spec, value));
    setDraft(saved);
    setInvalid(false);
    onLocalError?.(undefined);
    if (draft !== saved) onDraftDiscard?.();
  };
  const step = async (delta: bigint) => {
    const parsed = parseDraft(spec, draft);
    if (!parsed.ok || spec.kind !== "integer") return;
    const next = BigInt(String(parsed.value)) + delta;
    if (next < BigInt(spec.min) || next > BigInt(spec.max)) return;
    setDraft(next.toString());
    onLocalError?.(undefined);
    await submit(next.toString());
  };
  const change = (text: string) => { invalidate(); setDraft(text); };
  return { draft, invalid: invalid || Boolean(parentInvalid), change, commit, revert, step };
}
