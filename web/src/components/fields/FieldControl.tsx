import { useEffect, useRef, useState } from "react";
import type { FieldSpec, FieldValue } from "../../api/protocol";
import { displayValue, parseDraft, sameValue, toDraft } from "../../lib/values";
import { EnumControl } from "./EnumControl";
import { NullableControl, UnionControl } from "./VariantControl";
import { ReferenceControl } from "./reference/ReferenceControl";
import { useValueCommit } from "./useValueCommit";

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
  return (
    <div className="flags" id={id} role="group" aria-labelledby={labelledBy} aria-describedby={describedBy} aria-invalid={invalid || undefined}>
      {spec.bits.map((b) => (
        <label key={b.bit} title={b.description}>
          <input type="checkbox" disabled={busy} checked={(bits & (1n << BigInt(b.bit))) !== 0n} onChange={() => toggle(b.bit)} />
          {b.label}
        </label>
      ))}
    </div>
  );
}

/** Integer, float, text and datetime: typed draft, commit on Enter or blur, Escape reverts. */
function TextualControl({ id, spec, value, onCommit, compact, onLocalError, labelledBy, describedBy, invalid: parentInvalid }: FieldControlProps) {
  const [draft, setDraft] = useState(() => String(toDraft(spec, value)));
  const previousValue = useRef(draft);
  const [localInvalid, setInvalid] = useState(false);
  const pending = useRef<{ value: FieldValue; generation: number } | null>(null);
  const generation = useRef(0);
  const mounted = useRef(false);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  const invalid = localInvalid || Boolean(parentInvalid);
  useEffect(() => {
    const previous = previousValue.current;
    const next = String(toDraft(spec, value));
    previousValue.current = next;
    // A refresh or an earlier acknowledgement must not replace a newer/rejected draft.
    // Escape still explicitly restores the latest server value.
    setDraft(current => current === previous ? next : current);
  }, [spec, value]);

  const submit = async (next: FieldValue) => {
    if (pending.current && sameValue(pending.current.value, next)) return;
    const request = { value: next, generation: ++generation.current };
    pending.current = request;
    try {
      const accepted = await onCommit(next);
      if (mounted.current && request.generation === generation.current) setInvalid(!accepted);
    } finally {
      if (pending.current === request) pending.current = null;
    }
  };

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
    await submit(parsed.value);
  };

  const revert = () => {
    generation.current++;
    setDraft(String(toDraft(spec, value)));
    setInvalid(false);
    onLocalError?.(undefined);
  };

  const step = async (delta: bigint) => {
    const parsed = parseDraft(spec, draft);
    if (!parsed.ok || spec.kind !== "integer") return;
    const next = BigInt(String(parsed.value)) + delta;
    if (next < BigInt(spec.min) || next > BigInt(spec.max)) return;
    setDraft(next.toString());
    onLocalError?.(undefined);
    await submit(next.toString());
  };

  const className = ["input", spec.kind === "integer" || spec.kind === "float" || spec.kind === "exactNumber" ? "input--number" : "", compact ? "input--compact" : "", invalid ? "input--invalid" : ""]
    .filter(Boolean)
    .join(" ");
  const common = {
    id,
    className,
    value: draft,
    "aria-labelledby": labelledBy,
    "aria-describedby": [describedBy, spec.unit && !(spec.kind === "text" && spec.multiline) && `${id}-unit`].filter(Boolean).join(" ") || undefined,
    "aria-invalid": invalid,
    onChange: (e: { target: { value: string } }) => { generation.current++; setDraft(e.target.value); },
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
          if (e.key === "Enter") {
            // Enter commits this draft; action forms must not submit a previous value implicitly.
            e.preventDefault();
            void commit();
          }
          if (e.key === "Escape") revert();
          if (spec.kind === "integer" && (e.key === "ArrowUp" || e.key === "ArrowDown")) {
            e.preventDefault();
            step(e.key === "ArrowUp" ? 1n : -1n);
          }
        }}
      />
      {spec.kind === "integer" && !compact && (
        <span className="stepper">
          <button type="button" aria-labelledby={`${id}-decrease-label${labelledBy ? ` ${labelledBy}` : ""}`} onClick={() => step(-1n)}>
            <span id={`${id}-decrease-label`} className="visually-hidden">Decrease</span>
            <span aria-hidden="true">−</span>
          </button>
          <button type="button" aria-labelledby={`${id}-increase-label${labelledBy ? ` ${labelledBy}` : ""}`} onClick={() => step(1n)}>
            <span id={`${id}-increase-label`} className="visually-hidden">Increase</span>
            <span aria-hidden="true">+</span>
          </button>
        </span>
      )}
      {spec.unit && <span id={`${id}-unit`} className="field__hint">{spec.unit}</span>}
    </div>
  );
}
