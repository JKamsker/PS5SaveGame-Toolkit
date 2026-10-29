import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import type { ReferenceCandidate, ReferenceSpec } from "../../../api/protocol";
import { api, isStaleError } from "../../../api/client";
import { useDocument } from "../../../state/documentStore";
import type { FieldControlProps } from "../FieldControl";
import { ReferencePicker } from "./ReferencePicker";
import "../../../styles/reference-picker.css";

export function ReferenceControl(props: FieldControlProps & { spec: ReferenceSpec }) {
  const { id, valueId, actionId, value, spec, disabled, labelledBy, describedBy, compact } = props;
  const { workspaceId, documentId, store, structureVersion, refresh } = useDocument();
  const rawId = String(value ?? "");
  const [current, setCurrent] = useState<ReferenceCandidate>();
  const [open, setOpen] = useState(false);
  const trigger = useRef<HTMLButtonElement>(null);
  const wasOpen = useRef(false);
  const revision = store.state.revision;

  useEffect(() => {
    let active = true;
    setCurrent(undefined);
    setOpen(false);
    if (valueId) void api.referenceCandidate(workspaceId, documentId, { valueId, actionId, rawId, revision })
      .then((candidate) => { if (active) setCurrent(candidate); })
      .catch((error: unknown) => { if (active && isStaleError(error)) void refresh(); });
    return () => { active = false; };
  }, [workspaceId, documentId, valueId, actionId, rawId, revision, structureVersion, refresh]);

  useEffect(() => {
    // Restore focus after the modal has unmounted and the rest of the page is no longer inert.
    if (wasOpen.current && !open) trigger.current?.focus();
    wasOpen.current = open;
  }, [open]);

  const close = () => setOpen(false);
  return <>
    <button ref={trigger} id={id} type="button" className={`input reference-trigger${compact ? " input--compact" : ""}`}
      aria-labelledby={labelledBy} aria-describedby={describedBy} aria-haspopup="dialog" disabled={!valueId} onClick={() => setOpen(true)}
      title={`${current?.label ?? "Unknown ID"} (${rawId})`}>
      {current?.label ? `${current.label} (${rawId})` : rawId || "Choose an item…"}
    </button>
    {open && valueId && createPortal(<ReferencePicker {...props} valueId={valueId} browseOnly={Boolean(disabled || spec.readOnly)} current={current} onClose={close} />, document.body)}
  </>;
}
