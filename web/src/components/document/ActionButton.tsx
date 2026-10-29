import { useId, useMemo, useState } from "react";
import type { ActionNode, FieldValue } from "../../api/protocol";
import { useDocument } from "../../state/documentStore";
import { PlannedActionButton } from "./review/PlannedActionButton";
import { FieldControl } from "../fields/FieldControl";
import { useViewJump } from "./review/navigation";
import { useActionDialog } from "../ui/useActionDialog";

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
    {props.node.supportsPlanning ? <PlannedActionButton {...props} /> : <LegacyActionButton {...props} />}
  </div>;
}

function LegacyActionButton({ node, compact, ariaLabel }: ActionButtonProps) {
  const { canEdit, runAction } = useDocument();
  // Unique per instance: the same action may be rendered in several table rows and the detail row.
  const idPrefix = useId();
  const parameters = useMemo(() => (node.parameters ?? []).map(parameter => ({
    ...parameter, spec: { ...parameter.spec, readOnly: false },
  })), [node.parameters]);
  const [values, setValues] = useState<Record<string, FieldValue>>(() => Object.fromEntries(parameters.map((p) => [p.valueId, p.value])));
  const [errors, setErrors] = useState<Record<string, string | undefined>>({});
  const [boundTo, setBoundTo] = useState(node);
  if (boundTo !== node) {
    // A structural change rebuilt the view: defaults may reference records that no longer exist.
    setBoundTo(node);
    setValues(Object.fromEntries(parameters.map((p) => [p.valueId, p.value])));
    setErrors({});
  }
  const [busy, setBusy] = useState(false);
  const { dialog, heading, trigger, openDialog, closeDialog, onClose, focusHeading, requestFocus } = useActionDialog(busy);
  const invalid = parameters.some(parameter => Boolean(errors[parameter.valueId]));
  const needsDialog = parameters.length > 0 || Boolean(node.confirm);

  const run = async () => {
    if (busy || invalid) return;
    focusHeading();
    setBusy(true);
    try {
      const result = await runAction(node.actionId, values);
      if (result.ok) {
        closeDialog();
        setErrors({});
      } else {
        setErrors(Object.fromEntries(result.errors.map((e) => [e.valueId, e.message])));
        requestFocus("error");
      }
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className={compact ? "action action--compact" : "action"}>
      <button
        ref={trigger}
        type="button"
        className={`btn${node.tone === "Danger" ? " btn--danger" : ""}${compact ? " btn--small" : ""}`}
        aria-label={ariaLabel}
        title={compact ? node.description : undefined}
        disabled={(!canEdit && node.mutates !== false) || busy}
        onClick={() => (needsDialog ? openDialog() : void run())}
      >
        {node.label}
      </button>
      {node.description && !compact && <span className="action__description">{node.description}</span>}
      {needsDialog && (
        <dialog ref={dialog} className="dialog" aria-labelledby={`${idPrefix}-title`} onClose={onClose}>
          <form
            method="dialog"
            onSubmit={(e) => {
              e.preventDefault();
              void run();
            }}
          >
            <div className="dialog__body">
              <h2 ref={heading} tabIndex={-1} className="dialog__title" id={`${idPrefix}-title`}>
                {node.label}
              </h2>
              {node.confirm && <p className="notice notice--Warning">{node.confirm}</p>}
              {parameters.map((p) => (
                <div className="field" key={p.valueId} data-field-error={Boolean(errors[p.valueId])}>
                  <label className="field__label" id={`${idPrefix}-${p.valueId}-label`} htmlFor={`${idPrefix}-${p.valueId}`}>
                    {p.label}
                  </label>
                  <FieldControl
                    id={`${idPrefix}-${p.valueId}`}
                    labelledBy={`${idPrefix}-${p.valueId}-label`}
                    describedBy={[errors[p.valueId] && `${idPrefix}-${p.valueId}-error`, p.description && `${idPrefix}-${p.valueId}-description`].filter(Boolean).join(" ") || undefined}
                    valueId={p.valueId}
                    actionId={node.actionId}
                    spec={p.spec}
                    value={Object.hasOwn(values, p.valueId) ? values[p.valueId]! : p.value}
                    disabled={busy}
                    onLocalError={message => setErrors(previous => ({ ...previous, [p.valueId]: message }))}
                    onCommit={(v) => {
                      setValues((prev) => ({ ...prev, [p.valueId]: v }));
                      return true;
                    }}
                  />
                  {errors[p.valueId] && <span id={`${idPrefix}-${p.valueId}-error`} className="field__error" role="alert">{errors[p.valueId]}</span>}
                  {p.description && <span id={`${idPrefix}-${p.valueId}-description`} className="field__description">{p.description}</span>}
                </div>
              ))}
              <p role="status">{busy ? "Working…" : ""}</p>
              {errors[node.actionId] && <p role="alert" tabIndex={-1} className="notice notice--Danger">{errors[node.actionId]}</p>}
            </div>
            <div className="dialog__footer">
              <button type="button" className="btn btn--quiet" onClick={closeDialog}>
                Cancel
              </button>
              <button type="submit" className={`btn ${node.tone === "Danger" ? "btn--danger" : "btn--primary"}`} disabled={busy || invalid}>
                {node.label}
              </button>
            </div>
          </form>
        </dialog>
      )}
    </div>
  );
}
