import { useId, useState } from "react";
import type { ActionNode, FieldValue } from "../../api/protocol";
import { useDocument } from "../../state/documentStore";
import { useActionDialog } from "../ui/useActionDialog";
import { useAsyncTask } from "../ui/useAsyncTask";
import { actionParameterDefaults, useWritableActionParameters } from "./actionParameters";

function useDirectParameters(node: ActionNode) {
  const parameters = useWritableActionParameters(node);
  const [values, setValues] = useState<Record<string, FieldValue>>(() => actionParameterDefaults(parameters));
  const [errors, setErrors] = useState<Record<string, string | undefined>>({});
  const [boundTo, setBoundTo] = useState(node);
  if (boundTo !== node) {
    // A structural change rebuilt the view: defaults may reference records that no longer exist.
    setBoundTo(node);
    setValues(actionParameterDefaults(parameters));
    setErrors({});
  }
  return { parameters, values, setValues, errors, setErrors };
}

export function useDirectAction(node: ActionNode) {
  const { canEdit, runAction } = useDocument();
  // Unique per instance: the action can appear in several table rows and their details.
  const idPrefix = useId();
  const form = useDirectParameters(node);
  const { busy, run: submit } = useAsyncTask(node.actionId);
  const modal = useActionDialog(busy);
  const invalid = form.parameters.some(parameter => Boolean(form.errors[parameter.valueId]));
  const needsDialog = form.parameters.length > 0 || Boolean(node.confirm);
  const run = () => submit(async isCurrent => {
    if (invalid) return;
    modal.focusHeading();
    const result = await runAction(node.actionId, form.values);
    if (!isCurrent()) return;
    if (result.ok) {
      modal.closeDialog();
      form.setErrors({});
    } else {
      form.setErrors(result.stale ? { [node.actionId]: "The document changed. Review the parameters and try again." }
        : Object.fromEntries(result.errors.map(error => [error.valueId, error.message])));
      modal.requestFocus("error");
    }
  });
  return { canEdit, idPrefix, form, busy, ...modal, invalid, needsDialog, run };
}
