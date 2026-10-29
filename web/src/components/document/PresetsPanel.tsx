import { useEffect, useMemo, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ApiError, api, isStaleError } from "../../api/client";
import { reviewValue, type ActionPreview } from "../../api/actionReview";
import type { ActionNode, FieldValue, PresetActionCapability, PresetCapability, SaveDocument, ToolkitPreset, DocumentPreset } from "../../api/protocol";
import { useDocument } from "../../state/documentStore";
import { ActionParameterFields } from "./ActionParameterFields";
import { useActionSelection } from "./table/selectionBindings";
import { PresetTransferToolbar } from "./preset-transfer/PresetTransferToolbar";
import "./presets.css";

interface PresetsPanelProps { document: SaveDocument; onToast: (message: string, isError?: boolean) => void }

export function PresetsPanel({ document, onToast }: PresetsPanelProps) {
  const context = useDocument();
  const client = useQueryClient();
  const actions = useMemo(() => collectActions(document.view), [document.view]);
  // A view without planned actions has no meaningful preset surface. Keep the
  // panel mounted for consistent layout, but avoid an unnecessary global store
  // request (and its error state) in read-only/document-only views.
  const presets = useQuery({ queryKey: ["presets", context.workspaceId, context.documentId], queryFn: () => api.documentPresets(context.workspaceId, context.documentId), enabled: actions.length > 0 });
  const [name, setName] = useState("");
  const [actionId, setActionId] = useState(actions[0]?.actionId ?? "");
  const [scopeKind, setScopeKind] = useState("document");
  const [parameters, setParameters] = useState<Record<string, FieldValue>>(() => actionParameters(actions[0]));
  const [parameterErrors, setParameterErrors] = useState<Record<string, string | undefined>>({});
  const [preview, setPreview] = useState<{ preset: ToolkitPreset; preview: ActionPreview }>();
  const [stale, setStale] = useState(false);
  const [message, setMessage] = useState<string>();
  const [filter, setFilter] = useState("");
  const [pendingDelete, setPendingDelete] = useState<ToolkitPreset>();
  const [retryPresetId, setRetryPresetId] = useState<string>();
  const heading = useRef<HTMLHeadingElement>(null);
  const selectedAction = actions.find(action => action.actionId === actionId);
  const capability = document.presetCapability?.actions.find(action => action.actionId === actionId);
  const supportedScopes = supportedScopeKinds(capability, document.presetCapability);
  const binding = useActionSelection(selectedAction?.selectionTableId);
  const selection = binding.selection;
  const selectedBindingAvailable = Boolean(binding && !binding.unavailable && !selection?.queryScope);
  const selectedScopeUnavailable = scopeKind === "selected" && !selectedBindingAvailable;
  const reloadPresets = async () => {
    await api.reloadPresets();
    const result = await presets.refetch();
    if (result.error) throw result.error;
  };
  const loadPreview = async (presetId: string, choices?: Record<string, string>) => {
    try {
      const result = await api.presetPreview(context.workspaceId, context.documentId, presetId, context.store.state.revision, choices);
      setPreview(result); setStale(Boolean(result.preview.stale)); setMessage(undefined);
      setRetryPresetId(undefined);
    } catch (error) {
      const rejectedPreview = previewFromError(error);
      if (rejectedPreview) { setPreview(rejectedPreview); setStale(Boolean(rejectedPreview.preview.stale)); }
      else { setRetryPresetId(presetId); setMessage(error instanceof Error ? error.message : "Could not preview preset."); }
    }
  };
  const create = useMutation({
    mutationFn: () => api.capturePreset(context.workspaceId, context.documentId, name,
      { actionId, parameters: parameters as Record<string, unknown>, scopeKind,
        selectedRowKeys: scopeKind === "selected" ? [...(selection?.selectedKeys ?? [])] : undefined,
        allMatches: scopeKind === "allEligible" || scopeKind === "allCopies" }),
    onSuccess: result => { setName(""); setMessage(`Saved “${result.name}”.`); void client.invalidateQueries({ queryKey: ["presets"] }); },
    onError: error => setMessage(error instanceof Error ? error.message : "Could not save preset."),
  });
  const rename = useMutation({
    mutationFn: ({ id, value }: { id: string; value: string }) => api.renamePreset(id, value),
    onSuccess: result => { setMessage(`Renamed to “${result.name}”.`); void client.invalidateQueries({ queryKey: ["presets"] }); },
    onError: error => setMessage(error instanceof Error ? error.message : "Could not rename preset."),
  });
  const visiblePresets = useMemo(() => (presets.data ?? []).filter(row => `${row.preset.name} ${row.preset.actions.map(action => action.actionId).join(" ")}`.toLowerCase().includes(filter.trim().toLowerCase())), [filter, presets.data]);
  const confirmDelete = async () => {
    if (!pendingDelete) return;
    try { await api.deletePreset(pendingDelete.id); setPendingDelete(undefined); if (preview?.preset.id === pendingDelete.id) setPreview(undefined); void client.invalidateQueries({ queryKey: ["presets"] }); requestAnimationFrame(() => heading.current?.focus()); }
    catch (error) { setMessage(error instanceof Error ? error.message : "Could not delete preset."); }
  };
  const apply = useMutation({
    mutationFn: () => api.presetApply(context.workspaceId, context.documentId, preview!.preset.id, preview!.preview.planId!, preview!.preview.revision),
    onSuccess: async result => { await context.acceptResult(result); if (result.ok) setPreview(undefined); onToast(result.ok ? "Preset applied. Use Undo to reverse this one history step." : "Preset was not applied.", !result.ok); },
    onError: error => { if (isStaleError(error)) setStale(true); onToast(error instanceof Error ? error.message : "Could not apply preset.", true); },
  });
  const selectAction = (id: string) => {
    const next = actions.find(action => action.actionId === id);
    setActionId(id); setParameters(actionParameters(next)); setParameterErrors({});
    setScopeKind(supportedScopeKinds(document.presetCapability?.actions.find(action => action.actionId === id), document.presetCapability)[0] ?? "document");
  };
  return <details className="section presets-panel">
    <summary className="section__summary"><span className="section__title">Toolkit presets</span></summary>
    <div className="section__body" aria-busy={presets.isFetching || create.isPending || apply.isPending}>
      <p>Presets are stored outside save bytes and apply through a revision-bound preview.</p>
      {presets.isPending && actions.length > 0 && <p aria-live="polite" aria-atomic="true">Loading presets…</p>}
      {actions.length === 0 && <p className="notice notice--Muted">No planned preset actions. Import and export are available below.</p>}
      <h3 ref={heading} tabIndex={-1}>Saved presets</h3>
      <PresetTransferToolbar />
      {presets.isError && <><p role="alert">Could not load presets: {presets.error.message}</p><button type="button" className="btn btn--small" onClick={() => void reloadPresets().catch(error => setMessage(error instanceof Error ? error.message : "Could not reload presets."))}>Retry loading presets</button></>}
      {presets.data?.length === 0 && <p className="notice notice--Muted">No presets saved for this toolkit yet.</p>}
      {presets.data && <><label>Filter presets <input className="input" value={filter} onChange={event => setFilter(event.target.value)} placeholder="Name or action" /></label><ul className="presets-list">{visiblePresets.map(row => <PresetRow key={row.preset.id} row={row}
        busy={apply.isPending || rename.isPending} onPreview={() => loadPreview(row.preset.id)} onDelete={() => { setPendingDelete(row.preset); return Promise.resolve(); }} onRename={async value => { await rename.mutateAsync({ id: row.preset.id, value }); }} />)}</ul>{visiblePresets.length === 0 && <p className="notice notice--Muted">No presets match this filter.</p>}</>}
      {pendingDelete && <DeleteDialog preset={pendingDelete} onDelete={() => void confirmDelete()} onCancel={() => setPendingDelete(undefined)} />}
      {retryPresetId && <button type="button" className="btn btn--small" onClick={() => void loadPreview(retryPresetId)}>Retry preview</button>}
      {create.isError && <button type="button" className="btn btn--small" onClick={() => create.mutate()}>Retry saving preset</button>}
      {preview && <PreviewCard preview={preview.preview} busy={apply.isPending} stale={stale} canApply={document.canEdit} onApply={() => void apply.mutate()} onCancel={() => setPreview(undefined)} onPreviewAgain={choices => loadPreview(preview.preset.id, choices)} />}
      {message && <p role={message.startsWith("Could not") ? "alert" : "status"} className="notice notice--Warning">{message}</p>}
      <form className="presets-create" onSubmit={event => { event.preventDefault(); setMessage(undefined); try { create.mutate(); } catch (error) { setMessage(error instanceof Error ? error.message : "Invalid preset JSON."); } }}>
        <h3>Save an action preset</h3>
        {!document.canEdit && <p className="notice notice--Muted">This document is read-only; presets can be reviewed but not created or applied.</p>}
        <label>Name <input className="input" value={name} onChange={event => setName(event.target.value)} required maxLength={100} disabled={!document.canEdit || create.isPending} /></label>
        <p className="presets-meta">The host records the current title, plugin, variant, build and reference-pack boundary when the plugin publishes them.</p>
        <label>Action <select className="input" value={actionId} onChange={event => selectAction(event.target.value)} disabled={!document.canEdit || create.isPending || actions.length === 0}>{actions.map(action => <option key={action.actionId} value={action.actionId}>{action.label}</option>)}</select></label>
        <label>Scope <select className="input" value={scopeKind} onChange={event => setScopeKind(event.target.value)} disabled={!document.canEdit || create.isPending}>{supportedScopes.map(scope => <option key={scope} value={scope}>{scopeLabel(scope)}</option>)}</select></label>
        {scopeKind === "selected" && <p className="presets-meta" role="status">{selectedScopeUnavailable ? (binding.unavailable ? "This action has no reachable selection table." : "Clear the whole-query selection before capturing selected rows.") : `${selection?.count ?? 0} selected row${selection?.count === 1 ? "" : "s"} will be captured explicitly.`}</p>}
        {selectedAction?.parameters?.length ? <ActionParameterFields form={{ parameters: selectedAction.parameters, values: parameters, errors: parameterErrors, setValues: setParameters, setErrors: setParameterErrors }} idPrefix="preset-parameter" actionId={actionId} busy={!document.canEdit || create.isPending} variant="direct" /> : <p className="presets-meta">This action has no parameters.</p>}
        <button className="btn btn--primary btn--small" type="submit" disabled={!document.canEdit || create.isPending || !actionId || selectedScopeUnavailable}>{create.isPending ? "Saving…" : "Save preset"}</button>
      </form>
    </div>
  </details>;
}

function PresetRow({ row, busy, onPreview, onDelete, onRename }: { row: DocumentPreset; busy: boolean; onPreview: () => Promise<void>; onDelete: () => Promise<void>; onRename: (value: string) => Promise<void> }) {
  const [renaming, setRenaming] = useState(false);
  const [name, setName] = useState(row.preset.name);
  const reason = !row.compatibility.compatible ? row.compatibility.reason ?? "Compatibility is unknown."
    : !row.compatibility.canPreview || row.compatibility.unsupportedActions.length > 0
      ? row.compatibility.previewReason ?? "This preset is not supported by the current document."
      : undefined;
  return <li className="presets-list__item"><div>{renaming ? <form onSubmit={event => { event.preventDefault(); void onRename(name).then(() => setRenaming(false)); }}><label>Preset name <input className="input" value={name} onChange={event => setName(event.target.value)} maxLength={100} /></label><button className="btn btn--small" type="submit">Save name</button><button className="btn btn--small" type="button" onClick={() => setRenaming(false)}>Cancel</button></form> : <><strong>{row.preset.name}</strong><span className="presets-meta">{row.preset.compatibility.titleIds.join(", ")} · {row.preset.compatibility.gameBuild ?? "build unknown"} · {row.preset.actions.length} action{row.preset.actions.length === 1 ? "" : "s"}</span>{reason && <span className="presets-meta notice--Warning" role="status">{reason}</span>}</>}</div>
    <div className="bar__actions"><button className="btn btn--small" type="button" disabled={busy || Boolean(reason)} onClick={() => void onPreview()}>{reason ?? "Preview"}</button><button className="btn btn--small" type="button" disabled={busy || row.compatibility.readOnly} onClick={() => setRenaming(true)}>Rename</button>
      <button className="btn btn--small btn--danger" type="button" onClick={() => void onDelete()}>Delete</button></div></li>;
}

function DeleteDialog({ preset, onDelete, onCancel }: { preset: ToolkitPreset; onDelete: () => void; onCancel: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null);
  const cancel = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    const node = dialog.current;
    if (!node) return;
    if (!node.open) {
      if (typeof node.showModal === "function") node.showModal(); else node.setAttribute("open", "");
      cancel.current?.focus();
    }
    return () => { if (node.open && typeof node.close === "function") node.close(); };
  }, []);
  return <dialog ref={dialog} aria-labelledby="preset-delete-title" className="presets-delete" onCancel={event => { event.preventDefault(); onCancel(); }}><h3 id="preset-delete-title">Delete “{preset.name}”?</h3><p>This removes the external preset record. Save bytes are unchanged.</p><div className="bar__actions"><button ref={cancel} type="button" className="btn" onClick={onCancel}>Cancel</button><button type="button" className="btn btn--danger" onClick={onDelete}>Delete preset</button></div></dialog>;
}

function PreviewCard({ preview, busy, stale, canApply, onApply, onCancel, onPreviewAgain }: { preview: ActionPreview & { resolution?: { instances: { requested: { stableId: string; label?: string }; status: string; candidates?: { stableId: string; label?: string; kind?: string; role?: string; path?: string; build?: string; provenance?: string }[]; message?: string }[] } }; busy: boolean; stale: boolean; canApply: boolean; onApply: () => void; onCancel: () => void; onPreviewAgain: (choices: Record<string, string>) => Promise<void> }) {
  const [choices, setChoices] = useState<Record<string, string>>({});
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => { heading.current?.focus(); }, [preview.planId, preview.revision, stale]);
  const ambiguous = preview.resolution?.instances.filter(item => item.status === "Ambiguous") ?? [];
  const unresolved = preview.resolution?.instances.filter(item => item.status !== "Resolved") ?? [];
  const choicesRequired = ambiguous.some(item => (item.candidates?.length ?? 0) > 0 && !choices[item.requested.stableId]);
  const ready = !choicesRequired;
  return <div className="presets-preview" aria-live="polite"><h3 ref={heading} tabIndex={-1}>{stale ? "Preset preview is stale" : "Preset preview"}</h3>{stale && <p className="notice notice--Warning" role="alert">The document changed. Preview again before applying; no preset changes were written.</p>} {!canApply && <p className="notice notice--Muted">You can review this preset, but this document is read-only.</p>}{unresolved.length > 0 && <div className="presets-resolution"><strong>Owned-instance resolution</strong>{unresolved.map(item => <fieldset key={item.requested.stableId}><legend>{item.requested.label ?? "Target"} ({item.status.toLowerCase()})</legend><p>{item.message ?? "This target cannot be resolved on the current document."}</p>{item.status === "Ambiguous" && item.candidates?.length ? <><p className="presets-meta">Choices are revision-bound and are never saved in the preset.</p>{item.candidates.map(candidate => <label key={candidate.stableId}><input type="radio" name={`preset-${item.requested.stableId}`} checked={choices[item.requested.stableId] === candidate.stableId} onChange={() => setChoices(current => ({ ...current, [item.requested.stableId]: candidate.stableId }))} /> {candidate.label ?? "Current candidate"} · {candidate.role ?? candidate.kind ?? "owned"}{candidate.path && ` · ${candidate.path}`} {candidate.build && ` · ${candidate.build}`}<details><summary>Technical provenance</summary><code>{candidate.stableId}</code>{candidate.provenance && <span> · {candidate.provenance}</span>}</details></label>)}</> : <p className="presets-meta">No selectable current candidate is available; applying the preset is blocked.</p>}</fieldset>)}</div>}{preview.resolution && unresolved.length === 0 && <p role="status">All semantic targets resolved.</p>}<p>{preview.plan.changes.length} change{preview.plan.changes.length === 1 ? "" : "s"}; revision {preview.revision}.</p>
    {preview.plan.warnings.map((warning, index) => <p className="notice notice--Warning" key={index}>{warning}</p>)}
    {preview.plan.errors.map(error => <p role="alert" key={error.valueId}>{error.message}</p>)}
    <ul>{preview.plan.changes.slice(0, 50).map(change => <li key={change.valueId}><span>{change.label}</span>: {reviewValue(change.oldValue, change.oldExists)} → {reviewValue(change.newValue, change.newExists)}{change.derived && <span className="presets-meta"> · derived consequence</span>}</li>)}</ul>{preview.plan.changes.length > 50 && <p className="presets-meta">Showing 50 of {preview.plan.changes.length} changes.</p>}
    {preview.plan.skippedRows.length > 0 && <><h4>Skipped rows ({preview.plan.skippedRows.length})</h4><ul>{preview.plan.skippedRows.slice(0, 50).map(row => <li key={row.rowKey}><span>{row.rowKey}</span>: {row.reason}</li>)}</ul>{preview.plan.skippedRows.length > 50 && <p className="presets-meta">Showing 50 of {preview.plan.skippedRows.length} skipped rows.</p>}</>}
    <div className="bar__actions"><button className="btn btn--primary btn--small" type="button" disabled={busy || !canApply || stale || unresolved.length > 0 || !preview.planId || preview.plan.errors.length > 0} onClick={onApply}>{busy ? "Applying…" : "Apply preset"}</button><button className="btn btn--small" type="button" disabled={busy || !ready} onClick={() => void onPreviewAgain(choices)}>{stale || unresolved.length > 0 ? "Preview again" : "Refresh preview"}</button><button className="btn btn--small" type="button" onClick={onCancel}>Cancel</button></div></div>;
}

function supportedScopeKinds(action: PresetActionCapability | undefined, capability: PresetCapability | undefined): string[] {
  if (!action || capability?.actions.every(candidate => candidate.actionId !== action.actionId)) return ["document"];
  const scopes = action.scopeKinds?.length ? action.scopeKinds : action.scopeKind ? [action.scopeKind] : ["document"];
  return [...new Set(scopes)];
}

function scopeLabel(scope: string): string { return ({ document: "Whole document", allEligible: "All eligible", allCopies: "All copies", selected: "Selected rows" } as Record<string, string>)[scope] ?? scope; }

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

function previewFromError(error: unknown): { preset: ToolkitPreset; preview: ActionPreview } | undefined {
  if (!(error instanceof ApiError) || !error.body || typeof error.body !== "object") return undefined;
  const body = error.body as { preset?: ToolkitPreset; preview?: { preset?: ToolkitPreset; preview?: ActionPreview } };
  return body.preview?.preset && body.preview.preview
    ? { preset: body.preview.preset, preview: body.preview.preview }
    : body.preset && body.preview && "plan" in body.preview
      ? { preset: body.preset, preview: body.preview as ActionPreview }
      : undefined;
}
