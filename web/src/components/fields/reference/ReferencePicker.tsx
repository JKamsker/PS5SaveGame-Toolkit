import { ReferencePreview } from "./ReferencePreview";
import { ReferenceFilters, ReferenceOptions, ReferenceRawLookup } from "./ReferencePickerParts";
import { useReferencePicker, type ReferencePickerProps } from "./useReferencePicker";

export function ReferencePicker(props: ReferencePickerProps) {
  const { value, browseOnly = false, onClose } = props;
  const picker = useReferencePicker(props);
  const { id, dialog, workspaceId, documentId, preview, busy, loading, needsRefresh, error, loadFailed, setReload, refreshChoices, choose } = picker;
  return <dialog ref={dialog} className="dialog reference-picker" aria-labelledby={`${id}-title`} onCancel={(event) => { event.preventDefault(); if (!busy) onClose(); }}>
    <div className="dialog__body">
      <h2 id={`${id}-title`} className="dialog__title">{browseOnly ? "Browse items" : "Choose an item"}</h2>
      {browseOnly && <p role="note">Read-only browsing. Item details can be inspected; the saved ID cannot be changed.</p>}
      <p>Current ID: <code>{String(value ?? "")}</code>. Search by name or raw ID.</p>
      <ReferenceFilters picker={picker} />
      <div className="reference-picker__content">
        <ReferenceOptions picker={picker} browseOnly={browseOnly} />
        {preview && <ReferencePreview candidate={preview} workspaceId={workspaceId} documentId={documentId} />}
      </div>
      <ReferenceRawLookup picker={picker} />
      {error && <p role="alert" className="field__error">{error}</p>}
      {needsRefresh ? <button type="button" className="btn btn--small" disabled={busy || loading} onClick={event => {
        if (document.activeElement === event.currentTarget) picker.search.current?.focus();
        void refreshChoices();
      }}>Refresh choices</button>
        : loadFailed && <button type="button" className="btn btn--small" disabled={busy || loading} onClick={event => {
          if (document.activeElement === event.currentTarget) picker.search.current?.focus();
          setReload(previous => previous + 1);
        }}>Retry candidates</button>}
    </div>
    <div className="dialog__footer">
      <button type="button" className="btn btn--quiet" disabled={busy} onClick={onClose}>{browseOnly ? "Close" : "Cancel"}</button>
      {!browseOnly && <button type="button" className="btn btn--primary" disabled={busy || loading || needsRefresh || !preview?.eligibility.selectable} onClick={() => void choose()}>Use selected item</button>}
    </div>
  </dialog>;
}
