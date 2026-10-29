import type { Dispatch, SetStateAction } from "react";
import type { FieldNode, FieldValue } from "../../api/protocol";
import { FieldControl } from "../fields/FieldControl";

interface ParameterForm {
  parameters: readonly FieldNode[];
  values: Record<string, FieldValue>;
  errors: Record<string, string | undefined>;
  setValues: Dispatch<SetStateAction<Record<string, FieldValue>>>;
  setErrors: Dispatch<SetStateAction<Record<string, string | undefined>>>;
}

interface ParameterFieldsProps {
  form: ParameterForm;
  idPrefix: string;
  actionId: string;
  busy: boolean;
  variant: "direct" | "planned";
  formVersion?: number;
}

/** Keep form keys and direct-dialog error focus markers specific to each action workflow. */
export function ActionParameterFields({ form, formVersion, ...props }: ParameterFieldsProps) {
  return form.parameters.map(parameter => <ParameterField key={props.variant === "planned" ? `${formVersion}-${parameter.valueId}` : parameter.valueId}
    {...props} parameter={parameter} form={form} />);
}

function ParameterField({ parameter, form, idPrefix, actionId, busy, variant }: Omit<ParameterFieldsProps, "formVersion"> & { parameter: FieldNode }) {
  const id = `${idPrefix}-${parameter.valueId}`;
  const error = form.errors[parameter.valueId];
  const stored = form.values[parameter.valueId];
  const value = variant === "direct"
    ? Object.hasOwn(form.values, parameter.valueId) ? stored! : parameter.value
    : stored === undefined ? parameter.value : stored;
  return <div className="field" data-field-error={variant === "direct" ? Boolean(error) : undefined}>
    <label className="field__label" id={`${id}-label`} htmlFor={id}>{parameter.label}</label>
    <FieldControl
      id={id}
      labelledBy={`${id}-label`}
      describedBy={[error && `${id}-error`, parameter.description && `${id}-description`].filter(Boolean).join(" ") || undefined}
      valueId={parameter.valueId}
      actionId={actionId}
      spec={parameter.spec}
      value={value}
      disabled={busy}
      invalid={Boolean(error)}
      onLocalError={message => form.setErrors(previous => ({ ...previous, [parameter.valueId]: message }))}
      onCommit={next => {
        form.setValues(previous => ({ ...previous, [parameter.valueId]: next }));
        return true;
      }}
    />
    {error && <span id={`${id}-error`} className="field__error" role="alert">{error}</span>}
    {parameter.description && <span id={`${id}-description`} className="field__description">{parameter.description}</span>}
  </div>;
}
