import type { ActionNode } from "../../api/protocol";
import { PlannedActionButton } from "./review/PlannedActionButton";
import { useViewJump } from "./review/navigation";
import { DirectActionDialog } from "./DirectActionDialog";
import { useDirectAction } from "./useDirectAction";

interface ActionButtonProps {
  node: ActionNode;
  /** Table-cell form: no inline description (it goes into the tooltip) and an optional fuller accessible name. */
  compact?: boolean;
  ariaLabel?: string;
}

/** Plugin command. Opens a dialog when it has parameters or asks for confirmation. */
export function ActionButton(props: ActionButtonProps) {
  const ref = useViewJump(props.node.actionId, "Action");
  return <div ref={ref} tabIndex={-1} className="action-target">
    {props.node.supportsPlanning ? <PlannedActionButton {...props} /> : <DirectActionButton {...props} />}
  </div>;
}

function DirectActionButton({ node, compact, ariaLabel }: ActionButtonProps) {
  const action = useDirectAction(node);
  const { canEdit, busy, trigger, openDialog, needsDialog, run } = action;
  return <div className={compact ? "action action--compact" : "action"}>
    <button
      ref={trigger}
      type="button"
      className={`btn${node.tone === "Danger" ? " btn--danger" : ""}${compact ? " btn--small" : ""}`}
      aria-label={ariaLabel}
      title={compact ? node.description : undefined}
      disabled={(!canEdit && node.mutates !== false) || busy}
      onClick={() => (needsDialog ? openDialog() : void run())}
    >{node.label}</button>
    {node.description && !compact && <span className="action__description">{node.description}</span>}
    {needsDialog && <DirectActionDialog node={node} action={action} />}
  </div>;
}
