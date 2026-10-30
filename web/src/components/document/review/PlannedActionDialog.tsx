import type { ActionNode } from "../../../api/protocol";
import { ActionParameterFields } from "../ActionParameterFields";
import { ActionPreviewChanges } from "./ActionPreviewChanges";
import type { usePlannedAction } from "./usePlannedAction";

interface PlannedDialogProps {
  node: ActionNode;
  action: ReturnType<typeof usePlannedAction>;
}

export function PlannedActionDialog({ node, action }: PlannedDialogProps) {
  const { dialog, id, onClose, busy, invalid, preview, stale, refreshError, apply, plan, retrySnapshot } = action;
  return <dialog ref={dialog} className="dialog review-dialog" aria-labelledby={`${id}-title`} onClose={onClose}>
    <form onSubmit={event => {
      event.preventDefault();
      if (busy || action.doc.snapshotRefresh === "pending") return;
      if (refreshError) void retrySnapshot();
      else if (!invalid) void (preview?.planId && !stale ? apply() : plan());
    }}>
      <PlannedActionBody node={node} action={action} />
      <PlannedActionFooter node={node} action={action} />
    </form>
  </dialog>;
}

function PlannedActionBody({ node, action }: PlannedDialogProps) {
  const { heading, id, scopeLabel, preview, form, formVersion, busy, error, stale, refreshError, doc } = action;
  const pending = doc.snapshotRefresh === "pending";
  const failure = [error, refreshError && `Could not refresh the document: ${refreshError}`].filter(Boolean).join(" ");
  return <div className="dialog__body">
    <h2 ref={heading} tabIndex={-1} id={`${id}-title`} className="dialog__title">Preview: {node.label}</h2>
    <p>{scopeLabel}</p>
    {node.confirm && <p className="notice notice--Warning">{node.confirm}</p>}
    {!preview && <ActionParameterFields form={form} formVersion={formVersion} idPrefix={id} actionId={node.actionId} busy={busy} variant="planned" />}
    <p role="status" aria-atomic="true">{pending ? "Refreshing the document before another preview…" : busy ? "Working…" : preview
      ? preview.plan.changes.length ? `${preview.plan.changes.length} field changes · ${preview.plan.skippedRows.length} skipped rows · one undo step`
        : "No changes. No undo step will be created."
      : ""}</p>
    {failure && <p role="alert" tabIndex={-1} className="notice notice--Danger">{failure}{stale && <><br />{refreshError
      ? "This preview is stale. Refresh the document before previewing again."
      : "This preview is stale. Preview again before applying."}</>}</p>}
    {stale && !failure && !pending && <p role="alert" tabIndex={-1}>This preview is stale. The document or selection changed. Preview again before applying.</p>}
    <ActionPreviewChanges action={action} />
  </div>;
}

function PlannedActionFooter({ node, action }: PlannedDialogProps) {
  const { busy, closeDialog, preview, changeParameters, invalid, binding, stale, doc, refreshError } = action;
  return <div className="dialog__footer">
    <button type="button" className="btn btn--quiet" disabled={busy} onClick={closeDialog}>Cancel</button>
    {preview && <button type="button" className="btn" disabled={busy} onClick={changeParameters}>Change parameters</button>}
    <button type="submit" className={`btn ${node.tone === "Danger" ? "btn--danger" : "btn--primary"}`} disabled={busy || doc.snapshotRefresh === "pending" || (!refreshError && (invalid || binding.unavailable || Boolean(preview?.planId && !stale && (!doc.canEdit || !preview.plan.changes.length))))}>
      {doc.snapshotRefresh === "pending" ? "Refreshing document…" : refreshError ? "Retry document refresh" : preview?.planId && !stale ? "Apply" : stale ? "Preview again" : "Preview"}
    </button>
  </div>;
}
