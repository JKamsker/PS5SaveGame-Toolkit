import { useEffect, useRef, useState } from "react";
import type { FieldSpec } from "../../api/protocol";
import type { FieldControlProps } from "./FieldControl";

const searchThreshold = 20;

export function EnumControl({ id, spec, value, onCommit, compact, labelledBy, describedBy, onLocalError, invalid }: FieldControlProps & { spec: Extract<FieldSpec, { kind: "enum" }> }) {
  const current = value === null ? "" : String(value);
  const [draft, setDraft] = useState(current);
  const [query, setQuery] = useState("");
  const previousValue = useRef(current);
  useEffect(() => {
    const previous = previousValue.current;
    previousValue.current = current;
    setDraft(draft => draft === previous ? current : draft);
  }, [current]);
  const known = spec.options.some((o) => o.value === current);
  const selected = spec.options.find((o) => o.value === current);
  const className = compact ? "input input--compact" : "input";
  const commit = async () => {
    onLocalError?.(undefined);
    if (draft !== current) await onCommit(draft);
  };
  if (spec.allowCustom) {
    return <div className="enum-control">
      <input id={id} list={`${id}-options`} aria-labelledby={labelledBy} aria-describedby={describedBy} aria-invalid={invalid || undefined} className={className} value={draft}
        onChange={(e) => setDraft(e.target.value)} onBlur={() => void commit()}
        onKeyDown={(e) => { if (e.key === "Enter") { e.preventDefault(); void commit(); } if (e.key === "Escape") setDraft(current); }} />
      <datalist id={`${id}-options`}>
        {spec.options.map((o) => <option key={o.value} value={o.value}>{o.label}{o.description ? ` — ${o.description}` : ""}</option>)}
      </datalist>
      {selected?.description && <span className="field__hint">{selected.description}</span>}
    </div>;
  }
  const normalizedQuery = query.trim().toLocaleLowerCase();
  const options = spec.options.filter((o) => o.value === current ||
    `${o.label} ${o.value} ${o.description ?? ""}`.toLocaleLowerCase().includes(normalizedQuery));
  return <div className="enum-control">
    {spec.options.length >= searchThreshold && <input type="search" className={className} aria-label="Search options"
      aria-describedby={labelledBy} value={query} onChange={(e) => setQuery(e.target.value)} />}
    <select id={id} aria-labelledby={labelledBy} aria-describedby={describedBy} aria-invalid={invalid || undefined} className={className} value={current}
      onChange={(e) => { onLocalError?.(undefined); void onCommit(e.target.value); }}>
      {!known && <option value={current}>{current === "" ? "—" : `${current} (unlisted)`}</option>}
      {options.map((o) => <option key={o.value} value={o.value} title={o.description}>{o.label}</option>)}
    </select>
    {selected?.description && <span className="field__hint">{selected.description}</span>}
  </div>;
}
