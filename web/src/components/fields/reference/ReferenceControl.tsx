import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import type { ReferenceCandidate, ReferenceSpec } from "../../../api/protocol";
import { isStaleError } from "../../../api/client";
import { queueReferenceRecord } from "../../../api/referenceBatch";
import { useDocument } from "../../../state/documentStore";
import type { FieldControlProps } from "../FieldControl";
import { ReferencePicker } from "./ReferencePicker";
import "../../../styles/reference-picker.css";

// One stale batch can affect an entire page. Share its recovery while the
// document refresh is in flight, so cells do not enqueue a refetch each.
const recoveries = new WeakMap<() => Promise<void>, Promise<void>>();
function refreshLabels(refresh: () => Promise<void>) {
  if (recoveries.has(refresh)) return;
  const recovery = refresh().catch(() => {}).finally(() => recoveries.delete(refresh));
  recoveries.set(refresh, recovery);
}

export function ReferenceControl(props: FieldControlProps & { spec: ReferenceSpec }) {
  const { id, valueId, actionId, value, spec, disabled, labelledBy, describedBy, compact } = props;
  const { workspaceId, documentId, store, structureVersion, refresh } = useDocument();
  const rawId = String(value ?? "");
  const [current, setCurrent] = useState<ReferenceCandidate>();
  const [open, setOpen] = useState(false);
  const trigger = useRef<HTMLButtonElement>(null);
  const wasOpen = useRef(false);
  const recoveryAttempted = useRef(false);
  const revision = store.state.revision ?? 0;

  useEffect(() => {
    let active = true;
    setCurrent(undefined);
    const lookup = valueId ? queueReferenceRecord(workspaceId, documentId, revision, structureVersion,
      { fieldId: valueId, actionId, recordKey: rawId }) : undefined;
    if (lookup) void lookup.result
      .then((candidate) => { if (active && candidate) { setCurrent(candidate); recoveryAttempted.current = false; } })
      .catch((error: unknown) => {
        if (active && isStaleError(error) && !recoveryAttempted.current) {
          recoveryAttempted.current = true;
          refreshLabels(refresh);
        }
      });
    return () => { active = false; lookup?.cancel(); };
  }, [workspaceId, documentId, valueId, actionId, rawId, revision, structureVersion, refresh]);

  useEffect(() => {
    // Restore focus after the modal has unmounted and the rest of the page is no longer inert.
    if (wasOpen.current && !open) trigger.current?.focus();
    wasOpen.current = open;
  }, [open]);

  const close = () => setOpen(false);
  return <>
    {/* eslint-disable-next-line jsx-a11y/role-supports-aria-props -- This field's picker trigger retains the shared validation state and error description. */}
    <button ref={trigger} id={id} type="button" aria-invalid={props.invalid || undefined} className={`input reference-trigger${compact ? " input--compact" : ""}`}
      aria-labelledby={labelledBy ? `${labelledBy} ${id}-value` : undefined} aria-describedby={describedBy} aria-haspopup="dialog" disabled={!valueId} onClick={() => setOpen(true)}
      title={`${current?.label ?? "Unknown ID"} (${rawId})`}>
      <span id={`${id}-value`}>{current?.label ? `${current.label} (${rawId})` : rawId || "Choose an item…"}</span>
    </button>
    {open && valueId && createPortal(<ReferencePicker {...props} valueId={valueId} browseOnly={Boolean(disabled || spec.readOnly)} current={current} onClose={close} />, document.body)}
  </>;
}
