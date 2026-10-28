import { useEffect, useState } from "react";
import type { FieldSpec, FieldValue } from "../../api/protocol";
import { displayValue, parseDraft, sameValue, toDraft } from "../../lib/values";

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
}

/** Picks a control from the spec. Plugins never choose widgets; this mapping is the only place that does. */
export function FieldControl(props: FieldControlProps) {
  const { spec, value, disabled } = props;
  if (spec.readOnly || disabled) {
    return <span className={spec.kind === "integer" && spec.format === "Hex" ? "readonly-value mono" : "readonly-value"}>{displayValue(spec, value)}</span>;
  }
  switch (spec.kind) {
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

function BoolControl({ id, value, onCommit, labelledBy }: FieldControlProps) {
  const checked = value === true;
  return (
    <button
      id={id}
      type="button"
      role="switch"
      aria-checked={checked}
      aria-labelledby={labelledBy}
      className="switch"
      onClick={() => void onCommit(!checked)}
    />
  );
}

function EnumControl({ id, spec, value, onCommit, compact, labelledBy }: FieldControlProps & { spec: Extract<FieldSpec, { kind: "enum" }> }) {
  const current = value === null ? "" : String(value);
  const known = spec.options.some((o) => o.value === current);
  return (
    <select
      id={id}
      aria-labelledby={labelledBy}
      className={compact ? "input input--compact" : "input"}
      value={current}
      onChange={(e) => void onCommit(e.target.value)}
    >
      {!known && <option value={current}>{current === "" ? "—" : `${current} (unlisted)`}</option>}
      {spec.options.map((o) => (
        <option key={o.value} value={o.value} title={o.description}>
          {o.label}
        </option>
      ))}
    </select>
  );
}

function FlagsControl({ id, spec, value, onCommit }: FieldControlProps & { spec: Extract<FieldSpec, { kind: "flags" }> }) {
  const bits = value === null ? 0n : BigInt(String(value));
  const toggle = (bit: number) => void onCommit((bits ^ (1n << BigInt(bit))).toString());
  return (
    <div className="flags" id={id} role="group">
      {spec.bits.map((b) => (
        <label key={b.bit} title={b.description}>
          <input type="checkbox" checked={(bits & (1n << BigInt(b.bit))) !== 0n} onChange={() => toggle(b.bit)} />
          {b.label}
        </label>
      ))}
    </div>
  );
}

/** Integer, float, text and datetime: typed draft, commit on Enter or blur, Escape reverts. */
function TextualControl({ id, spec, value, onCommit, compact, onLocalError, labelledBy }: FieldControlProps) {
  const [draft, setDraft] = useState(() => String(toDraft(spec, value)));
  const [invalid, setInvalid] = useState(false);
  useEffect(() => {
    setDraft(String(toDraft(spec, value)));
    setInvalid(false);
  }, [spec, value]);

  const commit = async () => {
    const parsed = parseDraft(spec, draft);
    if (!parsed.ok) {
      setInvalid(true);
      onLocalError?.(parsed.error);
      return;
    }
    setInvalid(false);
    onLocalError?.(undefined);
    if (sameValue(parsed.value, value)) return;
    const accepted = await onCommit(parsed.value);
    setInvalid(!accepted);
  };

  const revert = () => {
    setDraft(String(toDraft(spec, value)));
    setInvalid(false);
    onLocalError?.(undefined);
  };

  const step = (delta: bigint) => {
    const parsed = parseDraft(spec, draft);
    if (!parsed.ok || spec.kind !== "integer") return;
    const next = BigInt(String(parsed.value)) + delta;
    if (next < BigInt(spec.min) || next > BigInt(spec.max)) return;
    void onCommit(next.toString());
  };

  const className = ["input", spec.kind === "integer" || spec.kind === "float" ? "input--number" : "", compact ? "input--compact" : "", invalid ? "input--invalid" : ""]
    .filter(Boolean)
    .join(" ");
  const common = {
    id,
    className,
    value: draft,
    "aria-labelledby": labelledBy,
    "aria-invalid": invalid,
    onChange: (e: { target: { value: string } }) => setDraft(e.target.value),
    onBlur: () => void commit(),
  };

  if (spec.kind === "text" && spec.multiline) {
    return <textarea {...common} onKeyDown={(e) => e.key === "Escape" && revert()} />;
  }

  return (
    <div className="control">
      <input
        {...common}
        type={spec.kind === "datetime" ? "datetime-local" : "text"}
        step={spec.kind === "datetime" ? 1 : undefined}
        inputMode={spec.kind === "integer" ? "numeric" : spec.kind === "float" ? "decimal" : undefined}
        spellCheck={spec.kind === "text"}
        className={`${className}${spec.kind === "integer" && spec.format === "Hex" ? " mono" : ""}`}
        onKeyDown={(e) => {
          if (e.key === "Enter") void commit();
          if (e.key === "Escape") revert();
          if (spec.kind === "integer" && (e.key === "ArrowUp" || e.key === "ArrowDown")) {
            e.preventDefault();
            step(e.key === "ArrowUp" ? 1n : -1n);
          }
        }}
      />
      {spec.kind === "integer" && !compact && (
        <span className="stepper">
          <button type="button" aria-label="Decrease" onClick={() => step(-1n)}>
            −
          </button>
          <button type="button" aria-label="Increase" onClick={() => step(1n)}>
            +
          </button>
        </span>
      )}
      {spec.unit && <span className="field__hint">{spec.unit}</span>}
    </div>
  );
}
