import { useEffect, useRef } from "react";
import type { ReactNode } from "react";
import type { FieldSpec, FieldValue } from "../../api/protocol";
import { parseDraft } from "../../lib/values";
import type { FieldControlProps } from "./FieldControl";

type Props = FieldControlProps & { renderInner: (props: FieldControlProps) => ReactNode };

/** A type change is explicit: initialise from a valid scalar seed, then let the server validate it. */
function initialValue(spec: FieldSpec): FieldValue | undefined {
  if (spec.readOnly) return undefined;
  if (spec.kind === "nullable") return null;
  if (spec.kind === "union") {
    const option = spec.options.find((o) => !o.spec.readOnly);
    const value = option ? initialValue(option.spec) : undefined;
    return option && value !== undefined ? { kind: "union", type: option.id, value } : undefined;
  }
  let seed: string | boolean = "";
  switch (spec.kind) {
    case "bool": seed = false; break;
    case "integer": seed = spec.min; break;
    case "float": seed = String(Math.max(spec.min ?? -Infinity, Math.min(spec.max ?? Infinity, 0))); break;
    case "flags": seed = "0"; break;
    case "exactNumber": seed = spec.min ?? spec.max ?? "0"; break;
    case "enum": seed = spec.options[0]?.value ?? ""; break;
    case "datetime": seed = "1970-01-01T00:00:00"; break;
  }
  const parsed = parseDraft(spec, seed);
  return parsed.ok ? parsed.value : undefined;
}

export function NullableControl(props: Props & { spec: Extract<FieldSpec, { kind: "nullable" }> }) {
  const { id, spec, value, onCommit, labelledBy, renderInner, onLocalError } = props;
  const remembered = useRef<FieldValue | undefined>(value === null ? undefined : value);
  useEffect(() => { if (value !== null) remembered.current = value; }, [value]);
  return (
    <div className="variant-control">
      <select id={id} className="input" aria-label="Null or value" aria-describedby={labelledBy}
        value={value === null ? "null" : "value"} onChange={(e) => {
          onLocalError?.(undefined);
          const next = e.target.value === "null" ? null : remembered.current ?? initialValue(spec.inner);
          if (next === undefined) onLocalError?.("This type has no valid initial value.");
          else void onCommit(next);
        }}>
        <option value="null">null</option>
        <option value="value" disabled={spec.inner.readOnly}>Value</option>
      </select>
      {value !== null && renderInner({ ...props, id: `${id}-value`, spec: spec.inner })}
    </div>
  );
}

export function UnionControl(props: Props & { spec: Extract<FieldSpec, { kind: "union" }> }) {
  const { id, spec, value, onCommit, labelledBy, renderInner, onLocalError } = props;
  const union = value !== null && typeof value === "object" && value.kind === "union" ? value : undefined;
  const option = spec.options.find((o) => o.id === union?.type);
  // Keep each type's last value while switching; never parse one type as another implicitly.
  const remembered = useRef(new Map<string, FieldValue>());
  useEffect(() => { if (union) remembered.current.set(union.type, union.value); }, [union]);
  return (
    <div className="variant-control">
      <select id={id} className="input" aria-label="Value type" aria-describedby={labelledBy}
        value={union?.type ?? ""} onChange={(e) => {
          const selected = spec.options.find((o) => o.id === e.target.value);
          if (!selected) return;
          const next = remembered.current.get(selected.id) ?? initialValue(selected.spec);
          onLocalError?.(undefined);
          if (next === undefined) onLocalError?.("This type has no valid initial value.");
          else void onCommit({ kind: "union", type: selected.id, value: next });
        }}>
        {!option && <option value={union?.type ?? ""}>{union?.type ?? "Select type"}</option>}
        {spec.options.map((o) => <option key={o.id} value={o.id} disabled={o.spec.readOnly}>{o.label}</option>)}
      </select>
      {option && union && renderInner({ ...props, id: `${id}-value`, spec: option.spec, value: union.value,
        onCommit: (next) => onCommit({ kind: "union", type: option.id, value: next }) })}
    </div>
  );
}
