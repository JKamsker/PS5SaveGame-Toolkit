import type { ActionNode } from "../../api/protocol";
import { ActionParameterFields } from "./ActionParameterFields";
import type { useDirectAction } from "./useDirectAction";

export function DirectActionDialog({ node, action }: { node: ActionNode; action: ReturnType<typeof useDirectAction> }) {
  const { idPrefix, form, busy, dialog, heading, onClose, closeDialog, invalid, run } = action;
  return <dialog ref={dialog} className="dialog" aria-labelledby={`${idPrefix}-title`} onClose={onClose}>
    <form method="dialog" onSubmit={event => { event.preventDefault(); void run(); }}>
      <div className="dialog__body">
        <h2 ref={heading} tabIndex={-1} className="dialog__title" id={`${idPrefix}-title`}>{node.label}</h2>
        {node.confirm && <p className="notice notice--Warning">{node.confirm}</p>}
        <ActionParameterFields form={form} idPrefix={idPrefix} actionId={node.actionId} busy={busy} variant="direct" />
        <p role="status">{busy ? "Working…" : ""}</p>
        {(form.errors[node.actionId] || form.errors[""]) && <p role="alert" tabIndex={-1} className="notice notice--Danger">{form.errors[node.actionId] || form.errors[""]}</p>}
      </div>
      <div className="dialog__footer">
        <button type="button" className="btn btn--quiet" onClick={closeDialog}>Cancel</button>
        <button type="submit" className={`btn ${node.tone === "Danger" ? "btn--danger" : "btn--primary"}`} disabled={busy || invalid}>{node.label}</button>
      </div>
    </form>
  </dialog>;
}
