import type { KeyboardEvent } from "react";
import type { FieldControlProps } from "./FieldControl";
import { useTextualDraft } from "./useTextualDraft";

/** Integer, float, text and datetime: typed draft, commit on Enter or blur, Escape reverts. */
export function TextualControl(props: FieldControlProps) {
  const { id, spec, compact, labelledBy, describedBy } = props;
  const { draft, invalid, change, commit, revert, step } = useTextualDraft(props);
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
    onChange: (event: { target: { value: string } }) => change(event.target.value),
    onBlur: () => void commit(),
  };
  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "Enter") {
      // Enter commits this draft; action forms must not submit a previous value implicitly.
      event.preventDefault();
      void commit();
    }
    if (event.key === "Escape") revert();
    if (spec.kind === "integer" && (event.key === "ArrowUp" || event.key === "ArrowDown")) {
      event.preventDefault();
      void step(event.key === "ArrowUp" ? 1n : -1n);
    }
  };
  if (spec.kind === "text" && spec.multiline) return <textarea {...common} onKeyDown={event => event.key === "Escape" && revert()} />;
  return (
    <div className="control">
      <input {...common} type={spec.kind === "datetime" ? "datetime-local" : "text"}
        step={spec.kind === "datetime" ? 1 : undefined}
        inputMode={spec.kind === "integer" ? "numeric" : spec.kind === "float" ? "decimal" : undefined}
        spellCheck={spec.kind === "text"}
        className={`${className}${spec.kind === "integer" && spec.format === "Hex" ? " mono" : ""}`} onKeyDown={onKeyDown} />
      {spec.kind === "integer" && !compact && <IntegerStepper id={id} labelledBy={labelledBy} step={step} />}
      {spec.unit && <span id={`${id}-unit`} className="field__hint">{spec.unit}</span>}
    </div>
  );
}

function IntegerStepper({ id, labelledBy, step }: { id: string; labelledBy?: string; step: (delta: bigint) => Promise<void> }) {
  return <span className="stepper">
    <button type="button" aria-labelledby={`${id}-decrease-label${labelledBy ? ` ${labelledBy}` : ""}`} onClick={() => void step(-1n)}>
      <span id={`${id}-decrease-label`} className="visually-hidden">Decrease</span>
      <span aria-hidden="true">−</span>
    </button>
    <button type="button" aria-labelledby={`${id}-increase-label${labelledBy ? ` ${labelledBy}` : ""}`} onClick={() => void step(1n)}>
      <span id={`${id}-increase-label`} className="visually-hidden">Increase</span>
      <span aria-hidden="true">+</span>
    </button>
  </span>;
}
