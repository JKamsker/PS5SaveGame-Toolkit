import type { FieldReferenceNode } from "../../api/protocol";

/** No editor or mutation fallback: only the host can establish a live canonical binding. */
export function UnavailableFieldReference({ node, compact = false }: { node: FieldReferenceNode; compact?: boolean }) {
  const label = node.label ?? "Referenced field";
  const explanation = "Field unavailable. Its canonical target may be unloaded, missing, or removed. Refresh the document or open its canonical view.";
  if (compact) return <span className="cell-text cell-text--Muted" title={`${label}. ${explanation}`}>{label}: unavailable</span>;
  return <div className="field">
    <span className="field__label">{label}</span>
    <span className="field__hint">{explanation}</span>
    {node.description && <span className="field__description">{node.description}</span>}
  </div>;
}
