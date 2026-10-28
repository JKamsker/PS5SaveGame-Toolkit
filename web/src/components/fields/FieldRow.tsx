import { useId, useState } from "react";
import type { FieldNode } from "../../api/protocol";
import { describeDuration, describeRange } from "../../lib/values";
import { useDocument, useFieldValue } from "../../state/documentStore";
import { FieldControl } from "./FieldControl";

const confidenceText = { Verified: "verified", Inferred: "inferred", Unknown: "meaning unknown" } as const;

/** A labelled field bound to the live document. */
export function FieldRow({ node }: { node: FieldNode }) {
  const { canEdit } = useDocument();
  const { value, error, modified, commit } = useLiveField(node);
  const [localError, setLocalError] = useState<string>();
  const id = useId();
  const labelId = `${id}-label`;
  const shownError = localError ?? error;
  const duration = node.spec.kind === "integer" ? describeDuration(node.spec, value) : null;
  const rangeHint = node.spec.kind === "integer" && !node.spec.readOnly && canEdit && node.spec.format !== "Duration" ? describeRange(node.spec) : null;

  return (
    <div className={`field${modified ? " field--modified" : ""}${shownError ? " field--error" : ""}`}>
      <label className="field__label" id={labelId} htmlFor={id}>
        {node.label}
        {node.confidence && node.confidence !== "Verified" && (
          <span className={`tag tag--${node.confidence.toLowerCase()}`}>{confidenceText[node.confidence]}</span>
        )}
        {modified && <span className="tag tag--modified">modified</span>}
      </label>
      <FieldControl
        id={id}
        valueId={node.valueId}
        labelledBy={labelId}
        spec={node.spec}
        value={value}
        disabled={!canEdit}
        onCommit={commit}
        onLocalError={setLocalError}
      />
      {shownError && <span className="field__error" role="alert">{shownError}</span>}
      {node.warning && <span className="field__warning">{node.warning}</span>}
      {duration && <span className="field__hint">{duration}</span>}
      {node.description && <span className="field__description">{node.description}</span>}
      {!node.description && rangeHint && <span className="field__hint">Range {rangeHint}</span>}
    </div>
  );
}

/** Live value + commit function for a field node (shared by rows and table cells). */
export function useLiveField(node: FieldNode) {
  const doc = useDocument();
  const live = useFieldValue(node.valueId, node.value);
  return { ...live, commit: (v: Parameters<typeof doc.commit>[1]) => doc.commit(node.valueId, v) };
}
