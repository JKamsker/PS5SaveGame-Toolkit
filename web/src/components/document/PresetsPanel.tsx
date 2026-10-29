import { lazy, Suspense, useEffect, useMemo, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ApiError, api, isStaleError } from "../../api/client";
import type { ActionPreview } from "../../api/actionReview";
import type { ActionNode, FieldValue, PresetActionCapability, PresetCapability, SaveDocument, ToolkitPreset } from "../../api/protocol";
import { useDocument } from "../../state/documentStore";
import { ActionParameterFields } from "./ActionParameterFields";
import { useActionSelection } from "./table/selectionBindings";
import "./presets.css";

const PresetRows = lazy(() => import("./PresetRows").then(module => ({ default: module.PresetRows })));
const PresetPreview = lazy(() => import("./PresetPreview").then(module => ({ default: module.PresetPreview })));

const filterReferenceTime = Date.now();

interface PresetsPanelProps { document: SaveDocument; onToast: (message: string, isError?: boolean) => void }

export function PresetsPanel({ document, onToast }: PresetsPanelProps) {
  const context = useDocument();
  const client = useQueryClient();
  const actions = useMemo(() => collectActions(document.view), [document.view]);
  const captureActions = useMemo(() => document.presetCapability
    ? actions.filter(action => document.presetCapability?.actions.some(candidate => candidate.actionId === action.actionId && candidate.supportsCapture !== false))
    : actions, [actions, document.presetCapability]);
  // A view without planned actions has no meaningful preset surface. Keep the
  // panel mounted for consistent layout, but avoid an unnecessary global store
  // request (and its error state) in read-only/document-only views.
  const presets = useQuery({ queryKey: ["presets", context.workspaceId, context.documentId], queryFn: () => api.documentPresets(context.workspaceId, context.documentId), enabled: actions.length > 0 });
  const [name, setName] = useState("");
  const [actionId, setActionId] = useState(captureActions[0]?.actionId ?? "");
  const [scopeKind, setScopeKind] = useState(() => firstScope(captureActions[0], document.presetCapability));
  const [parameters, setParameters] = useState<Record<string, FieldValue>>(() => actionParameters(captureActions[0]));
  const [parameterErrors, setParameterErrors] = useState<Record<string, string | undefined>>({});
  const [preview, setPreview] = useState<{ preset: ToolkitPreset; preview: ActionPreview }>();
  const [stale, setStale] = useState(false);
  const [message, setMessage] = useState<string>();
  const [messageIsError, setMessageIsError] = useState(false);
  const [filter, setFilter] = useState("");
  const [titleFilter, setTitleFilter] = useState("");
  const [buildFilter, setBuildFilter] = useState("");
  const [pluginFilter, setPluginFilter] = useState("");
  const [compatFilter, setCompatFilter] = useState("all");
  const [dateFilter, setDateFilter] = useState("all");
  const [pendingDelete, setPendingDelete] = useState<ToolkitPreset>();
  const [retryPresetId, setRetryPresetId] = useState<string>();
  const showMessage = (text: string, isError = false) => { setMessage(text); setMessageIsError(isError); };
  const clearMessage = () => { setMessage(undefined); setMessageIsError(false); };
  const heading = useRef<HTMLHeadingElement>(null);
  const activeActionId = captureActions.some(action => action.actionId === actionId) ? actionId : captureActions[0]?.actionId ?? "";
  const selectedAction = captureActions.find(action => action.actionId === activeActionId);
  const capability = document.presetCapability?.actions.find(action => action.actionId === activeActionId);
  const supportedScopes = supportedScopeKinds(capability, document.presetCapability);
  const descriptorSignature = useMemo(() => JSON.stringify({
    actions: captureActions.map(action => ({
      actionId: action.actionId,
      label: action.label,
      selectionTableId: action.selectionTableId,
      parameters: (action.parameters ?? []).map(parameter => ({ valueId: parameter.valueId, spec: parameter.spec })),
    })),
    capability: document.presetCapability?.actions.map(action => ({
      actionId: action.actionId,
      label: action.label,
      parameterIds: action.parameterIds,
      parameterSpecs: action.parameterSpecs,
      scopeKinds: action.scopeKinds,
      supportsCapture: action.supportsCapture,
    })),
  }), [captureActions, document.presetCapability]);
  const previousDescriptorSignature = useRef<string | undefined>(undefined);
  const binding = useActionSelection(selectedAction?.selectionTableId);
  const selection = binding.selection;
  const selectedBindingAvailable = Boolean(binding && !binding.unavailable && !selection?.queryScope);
  const selectedScopeUnavailable = scopeKind === "selected" && !selectedBindingAvailable;
  const scopeIsAdvertised = supportedScopes.includes(scopeKind);
  const parameterIds = selectedAction?.parameters?.map(parameter => parameter.valueId) ?? [];
  const parametersMatchDescriptor = parameterIds.length === Object.keys(parameters).length
    && parameterIds.every(id => Object.prototype.hasOwnProperty.call(parameters, id));
  const reloadPresets = async () => {
    await api.reloadPresets();
    const result = await presets.refetch();
    if (result.error) throw result.error;
  };
  const loadPreview = async (presetId: string, choices?: Record<string, string>) => {
    try {
      const result = await api.presetPreview(context.workspaceId, context.documentId, presetId, context.store.state.revision, choices);
      setPreview(result); setStale(Boolean(result.preview.stale)); clearMessage();
      setRetryPresetId(undefined);
    } catch (error) {
      const rejectedPreview = previewFromError(error);
      if (rejectedPreview) { setPreview(rejectedPreview); setStale(Boolean(rejectedPreview.preview.stale)); }
      else { setRetryPresetId(presetId); showMessage(error instanceof Error ? error.message : "Could not preview preset.", true); }
    }
  };
  const create = useMutation({
    mutationFn: () => api.capturePreset(context.workspaceId, context.documentId, name,
      { actionId: activeActionId, parameters: parameters as Record<string, unknown>, scopeKind,
        selectedRowKeys: scopeKind === "selected" ? [...(selection?.selectedKeys ?? [])] : undefined,
        allMatches: scopeKind === "allEligible" || scopeKind === "allCopies" }),
    onSuccess: result => { setName(""); showMessage(`Saved “${result.name}”.`); void client.invalidateQueries({ queryKey: ["presets"] }); },
    onError: error => showMessage(error instanceof Error ? error.message : "Could not save preset.", true),
  });
  const rename = useMutation({
    mutationFn: ({ id, value }: { id: string; value: string }) => api.renamePreset(id, value),
    onSuccess: result => { showMessage(`Renamed to “${result.name}”.`); void client.invalidateQueries({ queryKey: ["presets"] }); },
    onError: error => showMessage(error instanceof Error ? error.message : "Could not rename preset.", true),
  });
  const filteredPresets = useMemo(() => (presets.data ?? []).filter(row => {
    const text = `${row.preset.name} ${row.preset.actions.map(action => action.actionId).join(" ")}`.toLowerCase();
    const title = (row.compatibility.savedTitleIds ?? row.preset.compatibility.titleIds).join(" ").toLowerCase();
    const plugin = `${row.preset.compatibility.pluginId} ${row.compatibility.savedPluginVersion}`.toLowerCase();
    const build = (row.preset.compatibility.gameBuild ?? "").toLowerCase();
    const age = filterReferenceTime - Date.parse(row.preset.updatedAt);
    return text.includes(filter.trim().toLowerCase()) && title.includes(titleFilter.trim().toLowerCase())
      && build.includes(buildFilter.trim().toLowerCase()) && plugin.includes(pluginFilter.trim().toLowerCase())
      && (compatFilter === "all" || (compatFilter === "ready" ? Boolean(row.compatibility.canPreview && row.compatibility.canApply) : !row.compatibility.canPreview))
      && (dateFilter === "all" || (dateFilter === "7" ? age <= 7 * 86400000 : age <= 30 * 86400000));
  }), [buildFilter, compatFilter, dateFilter, filter, pluginFilter, presets.data, titleFilter]);
  const visiblePresets = filteredPresets.slice(0, 50);
  const confirmDelete = async () => {
    if (!pendingDelete) return;
    try { await api.deletePreset(pendingDelete.id); setPendingDelete(undefined); if (preview?.preset.id === pendingDelete.id) setPreview(undefined); void client.invalidateQueries({ queryKey: ["presets"] }); requestAnimationFrame(() => heading.current?.focus()); }
    catch (error) { showMessage(error instanceof Error ? error.message : "Could not delete preset.", true); }
  };
  const apply = useMutation({
    mutationFn: () => api.presetApply(context.workspaceId, context.documentId, preview!.preset.id, preview!.preview.planId!, preview!.preview.revision),
    onSuccess: async result => {
      await context.acceptResult(result);
      if (result.stale) {
        setStale(true);
        try { await context.refresh(); }
        catch (error) { showMessage(error instanceof Error ? `The current document could not be refreshed: ${error.message}` : "The current document could not be refreshed.", true); }
        onToast("Preset preview is stale. Review the current document and preview again.", true);
        return;
      }
      if (result.ok) { setPreview(undefined); onToast("Preset applied. Use Undo to reverse this one history step."); }
      else onToast(result.message ?? "Preset was not applied.", true);
    },
    onError: error => { if (isStaleError(error)) { setStale(true); void context.refresh().catch(refreshError => showMessage(refreshError instanceof Error ? `The current document could not be refreshed: ${refreshError.message}` : "The current document could not be refreshed.", true)); onToast("Preset preview is stale. Review the current document and preview again.", true); } else onToast(error instanceof Error ? error.message : "Could not apply preset.", true); },
  });
  const selectAction = (id: string) => {
    const next = captureActions.find(action => action.actionId === id);
    setActionId(id); setParameters(actionParameters(next)); setParameterErrors({});
    setScopeKind(supportedScopeKinds(document.presetCapability?.actions.find(action => action.actionId === id), document.presetCapability)[0] ?? "document");
  };
  useEffect(() => {
    if (previousDescriptorSignature.current === descriptorSignature) return;
    previousDescriptorSignature.current = descriptorSignature;
    const nextAction = captureActions[0];
    setActionId(nextAction?.actionId ?? "");
    setScopeKind(firstScope(nextAction, document.presetCapability));
    setParameters(actionParameters(nextAction));
    setParameterErrors({});
    setPreview(undefined);
    setStale(false);
  }, [captureActions, descriptorSignature, document.presetCapability]);
  return <details className="section presets-panel">
    <summary className="section__summary"><span className="section__title">Toolkit presets</span></summary>
    <div className="section__body" aria-busy={presets.isFetching || create.isPending || apply.isPending}>
      <p>Presets are stored outside save bytes and apply through a revision-bound preview.</p>
      {presets.isPending && actions.length > 0 && <p aria-live="polite" aria-atomic="true">Loading presets…</p>}
      {actions.length === 0 && <p className="notice notice--Muted">This document has no planned preset actions. Import and export remain available from the document toolbar.</p>}
      <h3 ref={heading} tabIndex={-1}>Saved presets</h3>
      {presets.isError && <><p role="alert">{messageIsError && message ? message : `Could not load presets: ${presets.error.message}`}</p><button type="button" className="btn btn--small" onClick={() => void reloadPresets().catch(error => showMessage(error instanceof Error ? error.message : "Could not reload presets.", true))}>Retry loading presets</button></>}
      {presets.data?.length === 0 && <p className="notice notice--Muted">No presets saved for this toolkit yet.</p>}
      {presets.data && <><div className="presets-filters"><label>Filter presets <input className="input" value={filter} onChange={event => setFilter(event.target.value)} placeholder="Name or action" /></label><label>Title <input className="input" value={titleFilter} onChange={event => setTitleFilter(event.target.value)} placeholder="Title id" /></label><label>Build <input className="input" value={buildFilter} onChange={event => setBuildFilter(event.target.value)} placeholder="Build" /></label><label>Plugin <input className="input" value={pluginFilter} onChange={event => setPluginFilter(event.target.value)} placeholder="Plugin id or version" /></label><label>Compatibility <select className="input" value={compatFilter} onChange={event => setCompatFilter(event.target.value)}><option value="all">All</option><option value="ready">Ready to apply</option><option value="attention">Needs attention</option></select></label><label>Updated <select className="input" value={dateFilter} onChange={event => setDateFilter(event.target.value)}><option value="all">Any date</option><option value="7">Last 7 days</option><option value="30">Last 30 days</option></select></label></div><p className="presets-meta" role="status" aria-live="polite">{filteredPresets.length} matching preset{filteredPresets.length === 1 ? "" : "s"}{filteredPresets.length > 50 ? "; showing the first 50" : ""}.</p><Suspense fallback={<p aria-live="polite">Loading saved preset rows…</p>}><PresetRows rows={visiblePresets} busy={apply.isPending || rename.isPending} onPreview={loadPreview} onDelete={preset => setPendingDelete(preset)} onRename={(id, value) => rename.mutateAsync({ id, value }).then(() => undefined)} /></Suspense>{visiblePresets.length === 0 && <p className="notice notice--Muted">No presets match this filter.</p>}</>}
      {pendingDelete && <DeleteDialog preset={pendingDelete} onDelete={() => void confirmDelete()} onCancel={() => setPendingDelete(undefined)} />}
      {retryPresetId && <button type="button" className="btn btn--small" onClick={() => void loadPreview(retryPresetId)}>Retry preview</button>}
      {create.isError && <button type="button" className="btn btn--small" onClick={() => create.mutate()}>Retry saving preset</button>}
      {preview && <Suspense fallback={<p aria-live="polite">Loading preset preview…</p>}><PresetPreview preview={preview.preview} busy={apply.isPending} stale={stale} canApply={document.canEdit} onApply={() => void apply.mutate()} onCancel={() => setPreview(undefined)} onPreviewAgain={choices => loadPreview(preview.preset.id, choices)} /></Suspense>}
      {message && !presets.isError && <p role={messageIsError ? "alert" : "status"} className="notice notice--Warning">{message}</p>}
      <form className="presets-create" onSubmit={event => { event.preventDefault(); clearMessage(); try { create.mutate(); } catch (error) { showMessage(error instanceof Error ? error.message : "Invalid preset JSON.", true); } }}>
        <h3>Save an action preset</h3>
        {!document.canEdit && <p className="notice notice--Muted">This document is read-only; presets can be reviewed but not created or applied.</p>}
        <label>Name <input className="input" value={name} onChange={event => setName(event.target.value)} required maxLength={100} disabled={!document.canEdit || create.isPending} /></label>
        <p className="presets-meta">The host records the current title, plugin, variant, build and reference-pack boundary when the plugin publishes them.</p>
        {document.presetCapability && captureActions.length === 0 && <p className="notice notice--Muted">No published capture actions are available for this document.</p>}
        <label>Action <select className="input" value={activeActionId} onChange={event => selectAction(event.target.value)} disabled={!document.canEdit || create.isPending || captureActions.length === 0}>{captureActions.map(action => <option key={action.actionId} value={action.actionId}>{action.label}</option>)}</select></label>
        <label>Scope <select className="input" value={scopeKind} onChange={event => setScopeKind(event.target.value)} disabled={!document.canEdit || create.isPending || captureActions.length === 0}>{supportedScopes.map(scope => <option key={scope} value={scope}>{scopeLabel(scope)}</option>)}</select></label>
        {scopeKind === "selected" && <p className="presets-meta" role="status">{selectedScopeUnavailable ? (binding.unavailable ? "This action has no reachable selection table." : "Clear the whole-query selection before capturing selected rows.") : `${selection?.count ?? 0} selected row${selection?.count === 1 ? "" : "s"} will be captured explicitly.`}</p>}
        {selectedAction?.parameters?.length ? <ActionParameterFields form={{ parameters: selectedAction.parameters, values: parameters, errors: parameterErrors, setValues: setParameters, setErrors: setParameterErrors }} idPrefix="preset-parameter" actionId={actionId} busy={!document.canEdit || create.isPending} variant="direct" /> : <p className="presets-meta">This action has no parameters.</p>}
        <button className="btn btn--primary btn--small" type="submit" disabled={!document.canEdit || create.isPending || !activeActionId || captureActions.length === 0 || selectedScopeUnavailable || !scopeIsAdvertised || !parametersMatchDescriptor}>{create.isPending ? "Saving…" : "Save preset"}</button>
      </form>
    </div>
  </details>;
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

function supportedScopeKinds(action: PresetActionCapability | undefined, capability: PresetCapability | undefined): string[] {
  if (!action) return capability ? [] : ["document"];
  const scopes = action.scopeKinds?.length ? action.scopeKinds : action.scopeKind ? [action.scopeKind] : ["document"];
  return [...new Set(scopes)];
}

function firstScope(action: ActionNode | undefined, capability: PresetCapability | undefined): string {
  const published = capability?.actions.find(candidate => candidate.actionId === action?.actionId);
  return supportedScopeKinds(published, capability)[0] ?? "document";
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
