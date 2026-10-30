import type { FieldSpec, FieldValue } from "../../api/protocol";
import { displayValue } from "../../lib/values";
import type { FieldControlProps } from "./FieldControl";
import { TextCapacityHint } from "./TextCapacityHint";

/** Resolve only the selected, present text value; absent/unknown variants have no text capacity to inspect. */
function textProjection(spec: FieldSpec, value: FieldValue): { text: string; capacity: number } | undefined {
  if (value === null) return undefined;
  if (spec.kind === "nullable") return textProjection(spec.inner, value);
  if (spec.kind === "union") {
    if (typeof value !== "object" || value.kind !== "union") return undefined;
    const options = spec.options.filter(option => option.id === value.type);
    return options.length === 1 ? textProjection(options[0]!.spec, value.value) : undefined;
  }
  return spec.kind === "text" && spec.maxUtf8Bytes != null && typeof value === "string"
    ? { text: value, capacity: spec.maxUtf8Bytes } : undefined;
}

/** Inspect nested text hints without mounting variant selectors or editable controls. */
export function ReadOnlyField({ id, spec, value, compact }: FieldControlProps) {
  const text = displayValue(spec, value);
  const projection = textProjection(spec, value);
  const display = <span title={text} className={spec.kind === "integer" && spec.format === "Hex" ? "readonly-value mono" : "readonly-value"}>{text}</span>;
  return projection ? <div className={`textual-control${compact ? " textual-control--compact" : ""}`}>
    {display}<TextCapacityHint id={`${id}-capacity`} text={projection.text} capacity={projection.capacity} compact={compact} />
  </div> : display;
}
