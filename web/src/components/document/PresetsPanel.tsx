import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, isStaleError } from "../../api/client";
import type { ActionPreview } from "../../api/actionReview";
import type { ActionNode, FieldValue, SaveDocument, ToolkitPreset } from "../../api/protocol";
import { useDocument } from "../../state/documentStore";
import "./presets.css";

interface PresetsPanelProps { document: SaveDocument; onToast: (message: string, isError?: boolean) => void }

export function PresetsPanel({ document, onToast }: PresetsPanelProps) {
  const context = useDocument();
  const client = useQueryClient();
  const presets = useQuery({ queryKey: ["presets"], queryFn: api.presets });
  const actions = useMemo(() => collectActions(document.view), [document.view]);
  const [name, setName] = useState("");
  const [actionId, setActionId] = useState(actions[0]?.actionId ?? "");
  const [parameters, setParameters] = useState(() => JSON.stringify(actionParameters(actions[0]), null, 2));
  const [preview, setPreview] = useState<{ preset: ToolkitPreset; preview: ActionPreview }>();
  const [stale, setStale] = useState(false);
  const [message, setMessage] = useState<string>();
  const create = useMutation({
    mutationFn: () => api.capturePreset(context.workspaceId, context.documentId, name,
      { actionId, parameters: JSON.parse(parameters) as Record<string, unknown>, scopeKind: "document" }),
    onSuccess: result => { setName(""); setMessage(`Saved “${result.name}”.`); void client.invalidateQueries({ queryKey: ["presets"] }); },
    onError: error => setMessage(error instanceof Error ? error.message : "Could not save preset."),
  });
  const apply = useMutation({
    mutationFn: () => api.presetApply(context.workspaceId, context.documentId, preview!.preset.id, preview!.preview.planId!, preview!.preview.revision),
    onSuccess: async result => { await context.acceptResult(result); if (result.ok) setPreview(undefined); onToast(result.ok ? "Preset applied. Use Undo to reverse this one history step." : "Preset was not applied.", !result.ok); },
    onError: error => { if (isStaleError(error)) setStale(true); onToast(error instanceof Error ? error.message : "Could not apply preset.", true); },
  });
  const selectAction = (id: string) => { setActionId(id); setParameters(JSON.stringify(actionParameters(actions.find(action => action.actionId === id)), null, 2)); };
  return <details className="section presets-panel">
    <summary className="section__summary"><span className="section__title">Toolkit presets</span></summary>
    <div className="section__body" aria-busy={presets.isFetching || create.isPending || apply.isPending}>
      <p>Presets are stored outside save bytes and apply through a revision-bound preview.</p>
      {presets.isPending && <p role="status">Loading presets…</p>}
      {presets.isError && <p role="alert">Could not load presets: {presets.error.message}</p>}
      {presets.data?.length === 0 && <p className="notice notice--Muted">No presets saved for this toolkit yet.</p>}
      {presets.data && <ul className="presets-list">{presets.data.map(preset => <PresetRow key={preset.id} preset={preset} document={document}
        busy={apply.isPending} onPreview={async () => {
          setMessage(undefined);
          try { const result = await api.presetPreview(context.workspaceId, context.documentId, preset.id, context.store.state.revision); setPreview(result); setStale(false); }
          catch (error) { setMessage(error instanceof Error ? error.message : "Could not preview preset."); }
        }} onDelete={async () => { if (!window.confirm(`Delete toolkit preset “${preset.name}”?`)) return; try { await api.deletePreset(preset.id); if (preview?.preset.id === preset.id) setPreview(undefined); void client.invalidateQueries({ queryKey: ["presets"] }); }
          catch (error) { setMessage(error instanceof Error ? error.message : "Could not delete preset."); } }} />)}</ul>}
      {preview && <PreviewCard preview={preview.preview} busy={apply.isPending} stale={stale} canApply={document.canEdit} onApply={() => void apply.mutate()} onCancel={() => setPreview(undefined)} onPreviewAgain={async choices => {
        try { const result = await api.presetPreview(context.workspaceId, context.documentId, preview.preset.id, context.store.state.revision, choices); setPreview(result); setStale(false); }
        catch (error) { setMessage(error instanceof Error ? error.message : "Could not refresh preset preview."); }
      }} />}
      {message && <p role="status" className="notice notice--Warning">{message}</p>}
      <form className="presets-create" onSubmit={event => { event.preventDefault(); setMessage(undefined); try { create.mutate(); } catch (error) { setMessage(error instanceof Error ? error.message : "Invalid preset JSON."); } }}>
        <h3>Save an action preset</h3>
        {!document.canEdit && <p className="notice notice--Muted">This document is read-only; presets can be reviewed but not created or applied.</p>}
        <label>Name <input className="input" value={name} onChange={event => setName(event.target.value)} required maxLength={100} disabled={!document.canEdit || create.isPending} /></label>
        <p className="presets-meta">The host records the current title, plugin, variant, build and reference-pack boundary when the plugin publishes them.</p>
        <label>Action <select className="input" value={actionId} onChange={event => selectAction(event.target.value)} disabled={!document.canEdit || create.isPending || actions.length === 0}>{actions.map(action => <option key={action.actionId} value={action.actionId}>{action.label}</option>)}</select></label>
        <label>Parameters (JSON) <textarea className="input presets-json" value={parameters} onChange={event => setParameters(event.target.value)} spellCheck={false} required disabled={!document.canEdit || create.isPending} /></label>
        <button className="btn btn--primary btn--small" type="submit" disabled={!document.canEdit || create.isPending || !actionId}>{create.isPending ? "Saving…" : "Save preset"}</button>
      </form>
    </div>
  </details>;
}

function PresetRow({ preset, document, busy, onPreview, onDelete }: { preset: ToolkitPreset; document: SaveDocument; busy: boolean; onPreview: () => Promise<void>; onDelete: () => Promise<void> }) {
  const compatible = preset.compatibility.pluginId === document.plugin.id && preset.compatibility.pluginVersion === document.plugin.version;
  return <li className="presets-list__item"><div><strong>{preset.name}</strong><span className="presets-meta">{preset.compatibility.titleIds.join(", ")} · {preset.compatibility.gameBuild ?? "build unknown"} · {preset.actions.length} action{preset.actions.length === 1 ? "" : "s"}</span></div>
    <div className="bar__actions"><button className="btn btn--small" type="button" disabled={busy || !compatible} onClick={() => void onPreview()}>{compatible ? "Preview" : "Cannot apply to this save"}</button>
      <button className="btn btn--small btn--danger" type="button" onClick={() => void onDelete()}>Delete</button></div></li>;
}

function PreviewCard({ preview, busy, stale, canApply, onApply, onCancel, onPreviewAgain }: { preview: ActionPreview & { resolution?: { instances: { requested: { stableId: string; label?: string }; status: string; candidates?: { stableId: string; label?: string }[]; message?: string }[] } }; busy: boolean; stale: boolean; canApply: boolean; onApply: () => void; onCancel: () => void; onPreviewAgain: (choices: Record<string, string>) => Promise<void> }) {
  const [choices, setChoices] = useState<Record<string, string>>({});
  const ambiguous = preview.resolution?.instances.filter(item => item.status === "Ambiguous") ?? [];
  const unresolved = preview.resolution?.instances.filter(item => item.status !== "Resolved") ?? [];
  const ready = ambiguous.every(item => choices[item.requested.stableId]);
  return <div className="presets-preview" aria-live="polite"><h3 tabIndex={-1}>{stale ? "Preset preview is stale" : "Preset preview"}</h3>{stale && <p className="notice notice--Warning" role="alert">The document changed. Preview again before applying; no preset changes were written.</p>} {!canApply && <p className="notice notice--Muted">You can review this preset, but this document is read-only.</p>}{unresolved.length > 0 && <div className="presets-resolution"><strong>Owned-instance resolution</strong>{unresolved.map(item => <fieldset key={item.requested.stableId}><legend>{item.requested.label ?? "Target"} ({item.status.toLowerCase()})</legend><p>{item.message ?? "Choose an eligible current instance before continuing."}</p>{item.candidates?.map(candidate => <label key={candidate.stableId}><input type="radio" name={`preset-${item.requested.stableId}`} checked={choices[item.requested.stableId] === candidate.stableId} onChange={() => setChoices(current => ({ ...current, [item.requested.stableId]: candidate.stableId }))} /> {candidate.label ?? "Candidate"}</label>)}</fieldset>)}</div>}<p>{preview.plan.changes.length} change{preview.plan.changes.length === 1 ? "" : "s"}; revision {preview.revision}.</p>
    {preview.plan.warnings.map((warning, index) => <p className="notice notice--Warning" key={index}>{warning}</p>)}
    {preview.plan.errors.map(error => <p role="alert" key={error.valueId}>{error.message}</p>)}
    <ul>{preview.plan.changes.slice(0, 50).map(change => <li key={change.valueId}><span>{change.label}</span>: {String(change.oldValue)} → {String(change.newValue)}</li>)}</ul>
    <div className="bar__actions"><button className="btn btn--primary btn--small" type="button" disabled={busy || !canApply || stale || unresolved.length > 0 || !preview.planId || preview.plan.errors.length > 0} onClick={onApply}>{busy ? "Applying…" : "Apply preset"}</button><button className="btn btn--small" type="button" disabled={busy || !ready} onClick={() => void onPreviewAgain(choices)}>{stale || unresolved.length > 0 ? "Preview again" : "Refresh preview"}</button><button className="btn btn--small" type="button" onClick={onCancel}>Cancel</button></div></div>;
}

function collectActions(node: SaveDocument["view"]): ActionNode[] {
  if (node.type === "action") return node.supportsPlanning && node.mutates !== false ? [node] : [];
  if ("children" in node) return node.children.flatMap(collectActions);
  if (node.type === "tabs") return node.tabs.flatMap(tab => tab.children.flatMap(collectActions));
  if (node.type === "table") return node.rows.flatMap(row => Object.values(row.cells).flatMap(collectActions));
  return [];
}

function actionParameters(action: ActionNode | undefined): Record<string, FieldValue> {
  return Object.fromEntries((action?.parameters ?? []).map(field => [field.valueId, field.value]));
}
