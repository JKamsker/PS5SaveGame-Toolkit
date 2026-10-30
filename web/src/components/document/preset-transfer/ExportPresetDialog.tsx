import { useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api } from "../../../api/client";
import { presetTransfer } from "../../../api/presetTransfer";
import { TransferDialog } from "./TransferDialog";

export default function ExportPresetDialog({ trigger, onDismiss, onDownloaded }: {
  trigger: HTMLElement; onDismiss: () => void; onDownloaded: (message: string) => void;
}) {
  const presets = useQuery({ queryKey: ["presets"], queryFn: api.presets });
  const [selected, setSelected] = useState(new Set<string>());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const errorHeading = useRef<HTMLParagraphElement>(null);
  const download = async () => {
    setBusy(true); setError(undefined);
    try {
      await presetTransfer.download([...selected]);
      onDownloaded(`Downloaded ${selected.size} toolkit preset${selected.size === 1 ? "" : "s"}.`); onDismiss();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "Could not download presets.");
      requestAnimationFrame(() => errorHeading.current?.focus());
    } finally { setBusy(false); }
  };
  return <TransferDialog title="Export toolkit presets" busy={busy} trigger={trigger} onDismiss={onDismiss}>
    <p>Select presets to download as one versioned package. It contains semantic actions and compatibility metadata, with no save bytes or save paths.</p>
    {presets.isPending && <p role="status">Loading preset library…</p>}
    {presets.isError && <><p role="alert">{presets.error.message}</p><button type="button" className="btn" onClick={() => void presets.refetch()}>Retry loading library</button></>}
    {presets.data?.length === 0 && <p>No presets to export. Save or import a preset first.</p>}
    {presets.data && presets.data.length > 0 && <>
      <label><input type="checkbox" disabled={busy} checked={selected.size === presets.data.length}
        onChange={event => setSelected(new Set(event.target.checked ? presets.data.map(preset => preset.id) : []))} /> Select all {presets.data.length} presets</label>
      <ul className="preset-transfer-items">{presets.data.map(preset => <li key={preset.id}><label>
        <input type="checkbox" disabled={busy} checked={selected.has(preset.id)} onChange={event => setSelected(current => {
          const next = new Set(current); if (event.target.checked) next.add(preset.id); else next.delete(preset.id); return next;
        })} /> <span>{preset.name}<small className="presets-meta">{preset.compatibility.titleIds.join(", ")} · {preset.compatibility.gameBuild ?? "build unknown"}</small></span>
      </label></li>)}</ul>
    </>}
    {error && <p ref={errorHeading} tabIndex={-1} role="alert">{error}</p>}
    <div className="bar__actions"><button type="button" className="btn" disabled={busy} onClick={onDismiss}>Cancel</button>
      <button type="button" className="btn btn--primary" disabled={busy || selected.size === 0 || presets.isError} onClick={() => void download()}>
        {busy ? "Downloading…" : error ? "Retry download" : `Download ${selected.size} preset${selected.size === 1 ? "" : "s"}`}
      </button></div>
  </TransferDialog>;
}
