import { useEffect, useId, useRef, useState } from "react";
import type { FieldNode } from "../../api/protocol";
import { describeDuration, describeRange, sameValue } from "../../lib/values";
import { useDocument, useFieldValue, type FieldDiagnosticReceipt } from "../../state/documentStore";
import { FieldControl } from "./FieldControl";

const confidenceText = { Verified: "verified", Inferred: "inferred", Unknown: "meaning unknown" } as const;

/** A labelled field bound to the live document. */
export function FieldRow({ node }: { node: FieldNode }) {
  const { canEdit } = useDocument();
  const { value, error, modified, commit, discardDraft } = useLiveField(node);
  const [localError, setLocalError] = useState<string>();
  const id = useId();
  const labelId = `${id}-label`;
  const shownError = localError ?? error;
  const duration = node.spec.kind === "integer" || node.spec.kind === "float" ? describeDuration(node.spec, value) : null;
  const rangeHint = node.spec.kind === "integer" && !node.spec.readOnly && canEdit && node.spec.format !== "Duration" ? describeRange(node.spec) : null;
  const describedBy = [shownError && `${id}-error`, node.warning && `${id}-warning`, duration && `${id}-duration`,
    node.description ? `${id}-description` : rangeHint && `${id}-range`].filter(Boolean).join(" ") || undefined;

  return (
    <div className={`field${modified ? " field--modified" : ""}${shownError ? " field--error" : ""}`}>
      <label className="field__label" id={labelId} htmlFor={id}>
        {node.label}
        {node.confidence && (
          <span className={`tag tag--${node.confidence.toLowerCase()}`}>{confidenceText[node.confidence]}</span>
        )}
        {modified && <span className="tag tag--modified">modified</span>}
      </label>
      <FieldControl
        id={id}
        valueId={node.valueId}
        labelledBy={labelId}
        describedBy={describedBy}
        invalid={Boolean(shownError)}
        spec={node.spec}
        value={value}
        disabled={!canEdit}
        onCommit={commit}
        onDraftDiscard={discardDraft}
        onLocalError={setLocalError}
      />
      {shownError && <span id={`${id}-error`} className="field__error" role="alert">{shownError}</span>}
      {node.warning && <span id={`${id}-warning`} className="field__warning">{node.warning}</span>}
      {duration && <span id={`${id}-duration`} className="field__hint">{duration}</span>}
      {node.description && <span id={`${id}-description`} className="field__description">{node.description}</span>}
      {!node.description && rangeHint && <span id={`${id}-range`} className="field__hint">Range {rangeHint}</span>}
    </div>
  );
}

/** Live value + commit function for a field node (shared by rows and table cells). */
export function useLiveField(node: FieldNode) {
  const doc = useDocument();
  const live = useFieldValue(node.valueId, node.value);
  const submission = useRef({ pending: 0, request: 0, rejection: undefined as FieldDiagnosticReceipt | undefined });
  useEffect(() => {
    const current = submission.current;
    current.request++;
    current.rejection = undefined;
    // A fresh field diagnostic (even the same message) must never be dismissed as an old rejection.
    return doc.store.subscribe(node.valueId, () => { current.rejection = undefined; });
  }, [doc.store, node.valueId]);
  const commit = async (value: Parameters<typeof doc.commit>[1]) => {
    const current = submission.current;
    const request = ++current.request;
    current.pending++;
    current.rejection = undefined;
    try {
      const outcome = doc.commitWithDiagnostics ? await doc.commitWithDiagnostics(node.valueId, value)
        : { accepted: await doc.commit(node.valueId, value), rejection: undefined };
      if (!outcome.accepted && request === current.request && outcome.rejection !== undefined
        && outcome.rejection === doc.store.diagnostic(node.valueId)) {
        current.rejection = outcome.rejection;
      }
      return outcome.accepted;
    } finally {
      current.pending--;
    }
  };
  const discardDraft = () => {
    const current = submission.current;
    if (current.pending || current.rejection === undefined) return;
    if (!sameValue(live.value, doc.store.value(node.valueId, node.value))) return;
    const receipt = current.rejection;
    current.rejection = undefined;
    doc.store.clearError(node.valueId, receipt);
  };
  return { ...live, commit, discardDraft };
}
