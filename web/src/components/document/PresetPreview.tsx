import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { reviewValue, type ActionPreview } from "../../api/actionReview";
import { useDocument } from "../../state/documentStore";

type Preview = ActionPreview & { resolution?: { instances: { requested: { stableId: string; label?: string }; status: string; candidates?: { stableId: string; label?: string; kind?: string; role?: string; path?: string; build?: string; provenance?: string }[]; message?: string }[] } };

export interface PresetPreviewProps {
  preview: Preview;
  busy: boolean;
  stale: boolean;
  refreshError?: string;
  refreshPending?: boolean;
  applyError?: string;
  canApply: boolean;
  onApply: () => void;
  onCancel: () => void;
  onPreviewAgain: (choices: Record<string, string>) => Promise<void>;
}

export function PresetPreview({ preview, busy, stale, refreshError, refreshPending = false, applyError, canApply, onApply, onCancel, onPreviewAgain }: PresetPreviewProps) {
  const context = useDocument();
  useLayoutEffect(() => context.claimSnapshotErrors?.(), [context.claimSnapshotErrors]);
  const [choices, setChoices] = useState<Record<string, string>>({});
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    heading.current?.focus();
  }, []);
  const ambiguous = preview.resolution?.instances.filter(item => item.status === "Ambiguous") ?? [];
  const unresolved = preview.resolution?.instances.filter(item => item.status !== "Resolved") ?? [];
  const currentChoices = Object.fromEntries(ambiguous.flatMap(item => {
    const choice = choices[item.requested.stableId];
    return choice && item.candidates?.some(candidate => candidate.stableId === choice) ? [[item.requested.stableId, choice]] : [];
  }));
  const choicesRequired = ambiguous.some(item => (item.candidates?.length ?? 0) > 0 && !currentChoices[item.requested.stableId]);
  const ready = !choicesRequired;
  // eslint-disable-next-line jsx-a11y/no-noninteractive-element-interactions
  return <div className="presets-preview" role="region" tabIndex={-1} aria-label="Preset preview" aria-live="polite" onKeyDown={event => { if (event.key === "Escape") { event.preventDefault(); onCancel(); } }}><h3 ref={heading} tabIndex={-1}>{stale ? "Preset preview is stale" : "Preset preview"}</h3>{stale && <p className="notice notice--Warning" role="alert">{refreshError ?? "The document changed. Preview again before applying; no preset changes were written."}</p>} {!canApply && <p className="notice notice--Muted">You can review this preset, but this document is read-only.</p>}{unresolved.length > 0 && <div className="presets-resolution"><strong>Owned-instance resolution</strong>{unresolved.map(item => <fieldset key={item.requested.stableId}><legend>{item.requested.label ?? "Target"} ({item.status.toLowerCase()})</legend><p>{item.message ?? "This target cannot be resolved on the current document."}</p>{item.status === "Ambiguous" && item.candidates?.length ? <><p className="presets-meta">Choices are revision-bound and are never saved in the preset.</p>{item.candidates.map(candidate => <label key={candidate.stableId}><input type="radio" name={`preset-${item.requested.stableId}`} checked={choices[item.requested.stableId] === candidate.stableId} onChange={() => setChoices(current => ({ ...current, [item.requested.stableId]: candidate.stableId }))} /> {candidate.label ?? "Current candidate"} · {candidate.role ?? candidate.kind ?? "owned"}{candidate.path && ` · ${candidate.path}`} {candidate.build && ` · ${candidate.build}`}<details><summary>Technical provenance</summary><code>{candidate.stableId}</code>{candidate.provenance && <span> · {candidate.provenance}</span>}</details></label>)}</> : <p className="presets-meta">No selectable current candidate is available; applying the preset is blocked.</p>}</fieldset>)}</div>}{preview.resolution && unresolved.length === 0 && <p role="status">All semantic targets resolved.</p>}<p>{preview.plan.changes.length} change{preview.plan.changes.length === 1 ? "" : "s"}; revision {preview.revision}.</p>
    {preview.plan.warnings.map((warning, index) => <p className="notice notice--Warning" key={index}>{warning}</p>)}
    {!stale && refreshError && <p role="alert" className="notice notice--Danger">{refreshError}</p>}
    {!stale && preview.plan.errors.map(error => <p role="alert" key={error.valueId}>{error.message}</p>)}
    {applyError && <p role="alert" className="notice notice--Danger">{applyError}</p>}
    {/* The bounded region is intentionally focusable so keyboard users can scroll the review. */}
    {/* eslint-disable-next-line jsx-a11y/no-noninteractive-tabindex */}
    <div className="presets-preview__scroll" role="region" tabIndex={0} aria-label="Preset preview changes and skipped rows"><ul>{preview.plan.changes.slice(0, 50).map(change => <li key={change.valueId}><span>{change.label}</span>: {reviewValue(change.oldValue, change.oldExists)} → {reviewValue(change.newValue, change.newExists)}{change.derived && <span className="presets-meta"> · derived consequence</span>}</li>)}</ul>{preview.plan.changes.length > 50 && <p className="presets-meta">Showing 50 of {preview.plan.changes.length} changes.</p>}
    {preview.plan.skippedRows.length > 0 && <><h4>Skipped rows ({preview.plan.skippedRows.length})</h4><ul>{preview.plan.skippedRows.slice(0, 50).map(row => <li key={row.rowKey}><span>{row.rowKey}</span>: {row.reason}</li>)}</ul>{preview.plan.skippedRows.length > 50 && <p className="presets-meta">Showing 50 of {preview.plan.skippedRows.length} skipped rows.</p>}</>}</div>
    <div className="bar__actions"><button className="btn btn--primary btn--small" type="button" disabled={busy || !canApply || refreshPending || Boolean(refreshError) || stale || unresolved.length > 0 || !preview.planId || preview.plan.errors.length > 0 || Boolean(applyError)} onClick={onApply}>{busy ? "Applying…" : "Apply preset"}</button><button className="btn btn--small" type="button" disabled={busy || !ready || refreshPending || Boolean(refreshError)} onClick={() => void onPreviewAgain(currentChoices)}>{stale || unresolved.length > 0 ? "Preview again" : "Refresh preview"}</button><button className="btn btn--small" type="button" onClick={onCancel}>Cancel</button></div></div>;
}
