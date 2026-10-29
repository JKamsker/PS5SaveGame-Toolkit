import type { FieldSpec, FieldValue } from "../../api/protocol";
import { displayValue } from "../../lib/values";
import { EnumControl } from "./EnumControl";
import { NullableControl, UnionControl } from "./VariantControl";
import { ReferenceControl } from "./reference/ReferenceControl";
import { useValueCommit } from "./useValueCommit";
import { TextualControl } from "./TextualControl";

export interface FieldControlProps {
  id: string;
  spec: FieldSpec;
  value: FieldValue;
  /** Called with a locally validated value. Return false to keep the draft (server rejected). */
  onCommit: (value: FieldValue) => Promise<boolean> | boolean;
  disabled?: boolean;
  compact?: boolean;
  /** Reports local validation problems while typing. */
  onLocalError?: (message: string | undefined) => void;
  labelledBy?: string;
  /** IDs of help, warnings and validation feedback belonging to this editor. */
  describedBy?: string;
  /** Validation state owned by the parent (for example a server rejection). */
  invalid?: boolean;
  /** Published binding used for field-specific candidate queries (not the DOM id). */
  valueId?: string;
  actionId?: string;
}

/** Picks a control from the spec. Plugins never choose widgets; this mapping is the only place that does. */
export function FieldControl(props: FieldControlProps) {
  const { spec, value, disabled } = props;
  if (spec.kind === "reference") return <ReferenceControl {...props} spec={spec} />;
  if (spec.readOnly || disabled) {
    const text = displayValue(spec, value);
    return <span title={text} className={spec.kind === "integer" && spec.format === "Hex" ? "readonly-value mono" : "readonly-value"}>{text}</span>;
  }
  switch (spec.kind) {
    case "nullable":
      return <NullableControl {...props} spec={spec} renderInner={(inner) => <FieldControl {...inner} />} />;
    case "union":
      return <UnionControl {...props} spec={spec} renderInner={(inner) => <FieldControl key={inner.spec.kind} {...inner} />} />;
    case "bool":
      return <BoolControl {...props} />;
    case "enum":
      return <EnumControl {...props} spec={spec} />;
    case "flags":
      return <FlagsControl {...props} spec={spec} />;
    default:
      return <TextualControl {...props} />;
  }
}

function BoolControl({ id, value, onCommit, labelledBy, describedBy, invalid }: FieldControlProps) {
  const checked = value === true;
  const { busy, commit } = useValueCommit(id, onCommit);
  return (
    <button
      id={id}
      type="button"
      role="switch"
      aria-checked={checked}
      aria-labelledby={labelledBy}
      aria-describedby={describedBy}
      aria-invalid={invalid || undefined}
      className="switch"
      disabled={busy}
      onClick={() => void commit(!checked)}
    />
  );
}

function FlagsControl({ id, spec, value, onCommit, labelledBy, describedBy, invalid }: FieldControlProps & { spec: Extract<FieldSpec, { kind: "flags" }> }) {
  const bits = value === null ? 0n : BigInt(String(value));
  const { busy, commit } = useValueCommit(id, onCommit);
  const toggle = (bit: number) => void commit((bits ^ (1n << BigInt(bit))).toString());
  const isSet = (bit: number) => (bits & (1n << BigInt(bit))) !== 0n;
  return (
    // eslint-disable-next-line jsx-a11y/role-supports-aria-props -- The flags group exposes the existing shared validation state for its field.
    <div className="flags" id={id} role="group" aria-labelledby={labelledBy} aria-describedby={describedBy} aria-invalid={invalid || undefined}>
      {spec.bits.map((b) => (
        <label key={b.bit} title={b.description}>
          <input type="checkbox" disabled={busy} checked={isSet(b.bit)} onChange={() => toggle(b.bit)} />
          {b.label}
        </label>
      ))}
    </div>
  );
}

