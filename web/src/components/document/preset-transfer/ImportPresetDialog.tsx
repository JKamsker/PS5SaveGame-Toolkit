import { useEffect, useRef, useState } from "react";
import { ApiError } from "../../../api/client";
import { presetTransfer, type ImportChoice, type ImportPreview } from "../../../api/presetTransfer";
import type { PresetCompatibilitySummary } from "../../../api/protocol";
import { TransferDialog } from "./TransferDialog";

export default function ImportPresetDialog({ workspaceId, documentId, trigger, onDismiss, onImported }: {
  workspaceId: string; documentId: string; trigger: HTMLElement; onDismiss: () => void; onImported: (message: string) => void;
}) {
  const [statuses, setStatuses] = useState<Record<number, PresetCompatibilitySummary>>({});
  const [file, setFile] = useState<File>();
  const [preview, setPreview] = useState<ImportPreview>();
  const [choices, setChoices] = useState<Record<number, ImportChoice>>({});
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
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
    catch (failure) { if (alive.current) { setError(failure instanceof Error ? failure.message : "Could not import presets."); setStale(failure instanceof ApiError && failure.status === 409); } }
    finally { if (alive.current) setBusy(false); }
  };
  const load = (selected: File) => run(async () => {
    setFile(selected); setPreview(undefined); setChoices({}); setStale(false);
    for (const token of tokens.current) await presetTransfer.cancel(token).catch(() => { /* Expiry still bounds abandoned previews. */ });
    tokens.current.clear();
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
    {error && <div><p role="alert">{error}</p>{file && <button className="btn" type="button" disabled={busy} onClick={() => void load(file)}>{stale ? "Preview file again" : "Retry validation"}</button>}</div>}
    {preview && <>
      <p>Schema {preview.schemaVersion} · {preview.items.length} presets · {preview.provenance.producer}</p>
      <p className="presets-meta">Exported {preview.provenance.exportedAt}. Preview expires {preview.expiresAt}.</p>
      <ol className="preset-transfer-items">{preview.items.map(item => {
        const status = statuses[item.index];
        const reason = !status?.compatible ? status?.reason ?? "Compatibility is unknown."
          : status.readOnly ? "This save is read-only."
          : status.unsupportedActions.length > 0 ? "The current plugin cannot apply these actions." : undefined;
        return <li key={item.index}>
          <strong>{item.resultingName ?? item.name}</strong>
          <p>{item.compatibility.titleIds.join(", ")} · {item.compatibility.gameBuild ?? "build unknown"} · {item.actionCount} actions</p>
          <p className="presets-meta">{reason ? `Stored for later. Cannot apply to this save: ${reason}` : "Matching known boundaries; plugin validation is required before applying."}</p>
          {item.replacesName && <p className="notice notice--Warning">Replaces “{item.replacesName}”.</p>}
          {!preview.readyToCommit && (item.conflicts.length > 0 || item.repeatedPackageName) && <fieldset disabled={busy}>
            <legend>Resolve duplicate for “{item.name}”</legend>
            {item.repeatedPackageName && <p>Another preset in this package has the same name.</p>}
            <label><input type="radio" name={`import-${item.index}`} checked={choices[item.index]?.decision === "keepBoth"}
              onChange={() => setChoices(current => ({ ...current, [item.index]: { index: item.index, decision: "keepBoth" } }))} /> Keep both (use a distinct name)</label>
            {item.conflicts.map((target, index) => <label key={target.id}><input type="radio" name={`import-${item.index}`}
              checked={choices[item.index]?.replaceId === target.id} onChange={() => setChoices(current => ({ ...current, [item.index]: { index: item.index, decision: "replace", replaceId: target.id } }))} /> Replace existing “{target.name}” (match {index + 1})</label>)}
          </fieldset>}
          <details><summary>Technical details</summary><p>Plugin {item.compatibility.pluginId} · version {item.compatibility.pluginVersion}</p><p>Variant {item.compatibility.formatVariant ?? "unknown"} · role {item.compatibility.fileRole ?? "unknown"} · capability schema {item.compatibility.capabilitySchemaVersion ?? "legacy"}</p><p>Kind {item.compatibility.presetKind} · schema fingerprint {item.compatibility.schemaFingerprint ?? "unknown"}</p><p>Reference identity {item.compatibility.referencePackIdentity ?? "unknown"}</p>{item.conflicts.map((target, index) => <p key={target.id}>Match {index + 1}: {target.id}</p>)}</details>
        </li>;
      })}</ol>
      {preview.readyToCommit ? <button className="btn btn--primary" type="button" disabled={busy || stale} onClick={() => void commit()}>Confirm import</button>
        : <button className="btn btn--primary" type="button" disabled={busy || stale || Object.keys(choices).length !== preview.items.length} onClick={() => void review()}>Review resulting names</button>}
    </>}
    <button className="btn" type="button" disabled={busy} onClick={onDismiss}>Cancel</button>
  </TransferDialog>;
}
