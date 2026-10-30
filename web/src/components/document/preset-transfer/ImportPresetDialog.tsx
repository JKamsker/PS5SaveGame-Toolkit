import { useCallback, useEffect, useRef, useState } from "react";
import { api, ApiError } from "../../../api/client";
import { presetTransfer, type ImportChoice, type ImportPreview } from "../../../api/presetTransfer";
import type { PresetCompatibilitySummary } from "../../../api/protocol";
import { TransferDialog } from "./TransferDialog";
import { ImportPresetItem } from "./ImportPresetItem";

export default function ImportPresetDialog({ workspaceId, documentId, trigger, onDismiss, onImported, onReloaded }: {
  workspaceId: string; documentId: string; trigger: HTMLElement; onDismiss: () => void; onImported: (message: string) => void; onReloaded: () => Promise<void>;
}) {
  const [statuses, setStatuses] = useState<Record<number, PresetCompatibilitySummary>>({});
  const [file, setFile] = useState<File>();
  const [preview, setPreview] = useState<ImportPreview>();
  const [choices, setChoices] = useState<Record<number, ImportChoice>>({});
  const choose = useCallback((choice: ImportChoice) => setChoices(current => ({ ...current, [choice.index]: choice })), []);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const [reloadRequired, setReloadRequired] = useState(false);
  const [stale, setStale] = useState(false);
  const tokens = useRef(new Set<string>());
  const alive = useRef(true);
  const summary = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    const pending = tokens.current;
    alive.current = true;
    return () => { alive.current = false; for (const token of pending) void presetTransfer.cancel(token).catch(() => { /* Expiring read-only preview: no storage mutation to recover. */ }); };
  }, []);
  useEffect(() => { if (preview || error) summary.current?.focus(); }, [preview, error]);
  const run = async (operation: () => Promise<void>) => {
    setBusy(true); setError(undefined);
    try { await operation(); }
    catch (failure) { if (alive.current) { setError(failure instanceof Error ? failure.message : "Could not import presets."); setStale(failure instanceof ApiError && failure.status === 409);
      if (failure instanceof ApiError && failure.body && typeof failure.body === "object" && "code" in failure.body && failure.body.code === "presetLibraryReloadRequired") setReloadRequired(true); } }
    finally { if (alive.current) setBusy(false); }
  };
  const load = (selected: File, reload = false) => run(async () => {
    setFile(selected); setPreview(undefined); setChoices({}); setStale(false); setReloadRequired(reload);
    for (const token of tokens.current) await presetTransfer.cancel(token).catch(() => { /* Expiry still bounds abandoned previews. */ });
    tokens.current.clear();
    if (reload) { await api.reloadPresets(); await onReloaded(); setReloadRequired(false); }
    const result = await presetTransfer.preview(selected);
    tokens.current.add(result.token);
    const compatibility = await presetTransfer.compatibility(workspaceId, documentId, result.token);
    if (alive.current) {
      setStatuses(Object.fromEntries(compatibility.map(item => [item.index, item.compatibility])));
      setPreview(result);
      setChoices(Object.fromEntries(result.items.filter(item => item.conflicts.length === 0 && !item.repeatedPackageName)
        .map(item => [item.index, { index: item.index, decision: "keepBoth" as const }])));
    }
  });
  const review = () => run(async () => {
    const result = await presetTransfer.resolve(preview!.token, Object.values(choices));
    tokens.current.add(result.token); setPreview(result);
  });
  const commit = () => run(async () => {
    const result = await presetTransfer.commit(preview!.token);
    onImported(`Imported ${result.imported.length} preset${result.imported.length === 1 ? "" : "s"}. Save data and history are unchanged.`);
    onDismiss();
  });
  return <TransferDialog title="Import toolkit presets" busy={busy} trigger={trigger} onDismiss={onDismiss}>
    <p>Choose a toolkit preset package (up to 512 KiB and 256 presets). Importing stores presets for later; it never applies them to a save.</p>
    <label className="preset-transfer-file">Preset package <input type="file" accept=".json,application/json" disabled={busy} onChange={event => {
      const selected = event.target.files?.[0]; event.target.value = ""; if (selected) void load(selected);
    }} /></label>
    {busy && <p role="status">{preview?.readyToCommit ? "Importing presets…" : "Validating preset package…"}</p>}
    <h4 ref={summary} tabIndex={-1}>{preview?.readyToCommit ? "Confirm import" : "Package preview"}</h4>
    {error && <div><p role="alert">{error}</p>{file && <button className="btn" type="button" disabled={busy} onClick={() => void load(file, reloadRequired)}>{reloadRequired ? "Reload library and preview again" : stale ? "Preview file again" : "Retry validation"}</button>}</div>}
    {preview && <>
      <p>Schema {preview.schemaVersion} · {preview.items.length} presets · {preview.provenance.producer}</p>
      <p className="presets-meta">Exported {preview.provenance.exportedAt}. Preview expires {preview.expiresAt}.</p>
      <ol className="preset-transfer-items">{preview.items.map(item => <ImportPresetItem key={item.index} item={item}
        status={statuses[item.index]} choice={choices[item.index]} busy={busy} reviewed={preview.readyToCommit} onChoice={choose} />)}</ol>
      {preview.readyToCommit ? <button className="btn btn--primary" type="button" disabled={busy || stale} onClick={() => void commit()}>Confirm import</button>
        : <button className="btn btn--primary" type="button" disabled={busy || stale || Object.keys(choices).length !== preview.items.length} onClick={() => void review()}>Review resulting names</button>}
    </>}
    <button className="btn" type="button" disabled={busy} onClick={onDismiss}>Cancel</button>
  </TransferDialog>;
}
