import type { ActionNode } from "../../../api/protocol";
import { PlannedActionDialog } from "./PlannedActionDialog";
import { usePlannedAction } from "./usePlannedAction";
import "./review.css";

export function PlannedActionButton({ node, compact, ariaLabel }: { node: ActionNode; compact?: boolean; ariaLabel?: string }) {
  const action = usePlannedAction(node);
  const { trigger, id, busy, binding, open } = action;
  return <div className={compact ? "action action--compact" : "action"}>
    <button ref={trigger} type="button" className={`btn${compact ? " btn--small" : ""}${node.tone === "Danger" ? " btn--danger" : ""}`} aria-label={ariaLabel} aria-describedby={node.selectionTableId != null ? `${id}-selection` : undefined} title={compact ? node.description : undefined} disabled={busy || binding.unavailable} onClick={open}>{node.label}</button>
    {node.selectionTableId != null && <span id={`${id}-selection`} className="action__description">{binding.unavailable
      ? `Selection table ${binding.title} is unavailable or ambiguous. Open the table to use this action.`
      : `Applies to ${binding.title}. With no selection, applies to the whole document.`}</span>}
    {node.description && !compact && <span className="action__description">{node.description}</span>}
    <PlannedActionDialog node={node} action={action} />
  </div>;
}
