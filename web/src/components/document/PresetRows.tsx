import { useState } from "react";
import type { DocumentPreset, ToolkitPreset } from "../../api/protocol";
import "./preset-rows.css";

interface PresetRowsProps {
  rows: DocumentPreset[];
  busy: boolean;
  onPreview: (id: string) => Promise<void>;
  onDelete: (preset: ToolkitPreset) => void;
  onRename: (id: string, value: string) => Promise<void>;
}

/** Saved-row rendering is deferred separately; filter state and transfer controls stay mounted in the panel. */
export function PresetRows({ rows, busy, onPreview, onDelete, onRename }: PresetRowsProps) {
  return <ul className="presets-list" aria-label="Saved toolkit presets">{rows.map(row => <PresetRow key={row.preset.id} row={row}
    busy={busy} onPreview={() => onPreview(row.preset.id)} onDelete={() => { onDelete(row.preset); return Promise.resolve(); }}
    onRename={value => onRename(row.preset.id, value)} />)}</ul>;
}

function PresetRow({ row, busy, onPreview, onDelete, onRename }: { row: DocumentPreset; busy: boolean; onPreview: () => Promise<void>; onDelete: () => Promise<void>; onRename: (value: string) => Promise<void> }) {
  const [renaming, setRenaming] = useState(false);
  const [name, setName] = useState(row.preset.name);
  const reason = !row.compatibility.compatible ? row.compatibility.reason ?? "Compatibility is unknown."
    : !row.compatibility.canPreview || row.compatibility.unsupportedActions.length > 0
      ? row.compatibility.previewReason ?? "This preset is not supported by the current document."
      : undefined;
  return <li className="presets-list__item"><div>{renaming ? <form onSubmit={event => { event.preventDefault(); void onRename(name).then(() => setRenaming(false)); }}><label>Preset name <input className="input" value={name} onChange={event => setName(event.target.value)} maxLength={100} /></label><button className="btn btn--small" type="submit">Save name</button><button className="btn btn--small" type="button" onClick={() => setRenaming(false)}>Cancel</button></form> : <><strong>{row.preset.name}</strong><span className="presets-meta">{row.preset.compatibility.titleIds.join(", ")} · {row.preset.compatibility.gameBuild ?? "build unknown"} · {row.preset.actions.length} action{row.preset.actions.length === 1 ? "" : "s"}</span><span className="presets-meta">Current title: {row.compatibility.currentTitleId ?? "unknown"} · kind: {row.compatibility.currentPresetKind ?? row.compatibility.presetKind} · readiness: {row.compatibility.readiness ?? (row.compatibility.canPreview ? "ready" : "unknown")} · {row.compatibility.canApply ? "Can apply" : "Review only"}</span>{reason && <span className="presets-meta notice--Warning" role="status">{reason}</span>}<details className="presets-boundaries"><summary>Compatibility boundaries</summary><dl><dt>Titles</dt><dd>{(row.compatibility.savedTitleIds ?? row.preset.compatibility.titleIds).join(", ")} → {row.compatibility.currentTitleId ?? "unknown"}</dd><dt>Plugin</dt><dd>{row.compatibility.savedPluginVersion} → {row.compatibility.currentPluginVersion}</dd><dt>Plugin id</dt><dd>{row.preset.compatibility.pluginId}</dd><dt>Build</dt><dd>{boundary(row.compatibility.savedGameBuild, row.compatibility.currentGameBuild)}</dd><dt>Variant</dt><dd>{boundary(row.compatibility.savedFormatVariant, row.compatibility.currentFormatVariant)}</dd><dt>Reference pack</dt><dd>{boundary(row.compatibility.savedReferencePackIdentity, row.compatibility.currentReferencePackIdentity)}</dd><dt>Schema</dt><dd>{boundary(row.compatibility.savedSchemaFingerprint, row.compatibility.currentSchemaFingerprint)}</dd><dt>Role</dt><dd>{boundary(row.compatibility.savedFileRole, row.compatibility.currentFileRole)}</dd><dt>Capability schema</dt><dd>{boundary(row.compatibility.savedCapabilitySchemaVersion, row.compatibility.currentCapabilitySchemaVersion)}</dd></dl></details></>}</div>
    <div className="bar__actions"><button className="btn btn--small" type="button" disabled={busy || Boolean(reason)} onClick={() => void onPreview()}>{reason ?? "Preview"}</button><button className="btn btn--small" type="button" disabled={busy} onClick={() => setRenaming(true)}>Rename</button>
      <button className="btn btn--small btn--danger" type="button" onClick={() => void onDelete()}>Delete</button></div></li>;
}

function boundary(saved: string | undefined, current: string | undefined): string {
  return `${saved ?? "unknown"} → ${current ?? "unknown"}`;
}
