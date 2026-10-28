import { useRef, useState } from "react";
import type { ActionNode, FieldValue } from "../../api/protocol";
import { useDocument } from "../../state/documentStore";
import { FieldControl } from "../fields/FieldControl";

/** Plugin command. Opens a dialog when it has parameters or asks for confirmation. */
export function ActionButton({ node }: { node: ActionNode }) {
  const { canEdit, runAction } = useDocument();
  const dialog = useRef<HTMLDialogElement>(null);
  const parameters = node.parameters ?? [];
  const [values, setValues] = useState<Record<string, FieldValue>>(() => Object.fromEntries(parameters.map((p) => [p.valueId, p.value])));
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [boundTo, setBoundTo] = useState(node);
  if (boundTo !== node) {
    // A structural change rebuilt the view: defaults may reference records that no longer exist.
    setBoundTo(node);
    setValues(Object.fromEntries(parameters.map((p) => [p.valueId, p.value])));
    setErrors({});
  }
  const [busy, setBusy] = useState(false);
  const needsDialog = parameters.length > 0 || Boolean(node.confirm);

  const run = async () => {
    setBusy(true);
    try {
      const result = await runAction(node.actionId, values);
      if (result.ok) {
        dialog.current?.close();
        setErrors({});
      } else {
        setErrors(Object.fromEntries(result.errors.map((e) => [e.valueId, e.message])));
      }
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="action">
      <button
        type="button"
        className={`btn${node.tone === "Danger" ? " btn--danger" : ""}`}
        disabled={!canEdit || busy}
        onClick={() => (needsDialog ? dialog.current?.showModal() : void run())}
      >
        {node.label}
      </button>
      {node.description && <span className="action__description">{node.description}</span>}
      {needsDialog && (
        <dialog ref={dialog} className="dialog" aria-labelledby={`${node.actionId}-title`}>
          <form
            method="dialog"
            onSubmit={(e) => {
              e.preventDefault();
              void run();
            }}
          >
            <div className="dialog__body">
              <h2 className="dialog__title" id={`${node.actionId}-title`}>
                {node.label}
              </h2>
              {node.confirm && <p className="notice notice--Warning">{node.confirm}</p>}
              {parameters.map((p) => (
                <div className="field" key={p.valueId}>
                  <label className="field__label" htmlFor={`${node.actionId}-${p.valueId}`}>
                    {p.label}
                  </label>
                  <FieldControl
                    id={`${node.actionId}-${p.valueId}`}
                    spec={{ ...p.spec, readOnly: false }}
                    value={values[p.valueId] ?? p.value}
                    onCommit={(v) => {
                      setValues((prev) => ({ ...prev, [p.valueId]: v }));
                      return true;
                    }}
                  />
                  {errors[p.valueId] && <span className="field__error">{errors[p.valueId]}</span>}
                  {p.description && <span className="field__description">{p.description}</span>}
                </div>
              ))}
              {errors[node.actionId] && <p className="notice notice--Danger">{errors[node.actionId]}</p>}
            </div>
            <div className="dialog__footer">
              <button type="button" className="btn btn--quiet" onClick={() => dialog.current?.close()}>
                Cancel
              </button>
              <button type="submit" className={`btn ${node.tone === "Danger" ? "btn--danger" : "btn--primary"}`} disabled={busy}>
                {node.label}
              </button>
            </div>
          </form>
        </dialog>
      )}
    </div>
  );
}
