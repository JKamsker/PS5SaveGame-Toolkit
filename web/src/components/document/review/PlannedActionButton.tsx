import { useId, useMemo, useRef, useState } from "react";
import { actionReview, reviewValue, type ActionPreview, type ActionScope } from "../../../api/actionReview";
import type { ActionNode, FieldValue } from "../../../api/protocol";
import { useDocument, useDocumentState } from "../../../state/documentStore";
import { FieldControl } from "../../fields/FieldControl";
import { useTableSelection } from "../table/selection";
import { ReviewPager, useReviewPage } from "./paging";
import "./review.css";

export function PlannedActionButton({ node, compact, ariaLabel }: { node: ActionNode; compact?: boolean; ariaLabel?: string }) {
  const doc = useDocument();
  const { state } = useDocumentState(doc.store);
  const selection = useTableSelection();
  const dialog = useRef<HTMLDialogElement>(null);
  const id = useId();
  const parameters = useMemo(() => (node.parameters ?? []).map(parameter => ({
    ...parameter, spec: { ...parameter.spec, readOnly: false },
  })), [node.parameters]);
  const [values, setValues] = useState<Record<string, FieldValue>>(() => Object.fromEntries(parameters.map(p => [p.valueId, p.value])));
  const [preview, setPreview] = useState<ActionPreview | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [parameterErrors, setParameterErrors] = useState<Record<string, string | undefined>>({});
  const [formVersion, setFormVersion] = useState(0);
  const invalid = Object.values(parameterErrors).some(Boolean);
  const queryScope = selection?.queryScope;
  const scope: ActionScope = queryScope ? { queryScope: {
    collectionId: queryScope.collectionId,
    query: { text: queryScope.query.q, filters: queryScope.query.filters, sortColumn: queryScope.query.sort, descending: queryScope.query.descending, includeEmpty: queryScope.query.includeEmpty },
    revision: queryScope.revision,
    excludedRowKeys: queryScope.excludedRowKeys,
  } } : selection ? { rowKeys: [...selection.selectedKeys] } : {};
  const scopeKey = JSON.stringify(scope);
  const [previewScope, setPreviewScope] = useState("");
  const changesPage = useReviewPage(preview?.plan.changes ?? []);
  const skippedPage = useReviewPage(preview?.plan.skippedRows ?? []);
  const selectedCount = preview?.scope.rowKeys?.length ?? selection?.count;
  const scopeLabel = selectedCount === undefined ? "Whole document" : `${selectedCount} selected ${selectedCount === 1 ? "row" : "rows"}`;
  const stale = Boolean(preview && (preview.revision !== state.revision || previewScope !== scopeKey));

  const plan = async (params = values) => {
    if (busy || invalid) return;
    setBusy(true);
    setError("");
    setPreview(null);
    setParameterErrors({});
    try {
      const result = await actionReview.plan(doc.workspaceId, doc.documentId, node.actionId, params, scope, doc.store.state.revision);
      setPreview(result);
      setPreviewScope(scopeKey);
      if (result.stale) await doc.refresh();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "Could not preview this action.");
    } finally {
      setBusy(false);
    }
  };

  const apply = async () => {
    if (!preview?.planId || stale) return;
    setBusy(true);
    setError("");
    try {
      const result = await actionReview.apply(doc.workspaceId, doc.documentId, preview.planId, preview.revision);
      await doc.acceptResult(result);
      if (result.ok) dialog.current?.close();
      else {
        setPreview(null);
        setError(result.errors.map(e => e.message).join(" ") || "Apply failed. Preview the action again.");
      }
    } catch (failure) {
      setPreview(null);
      setError(failure instanceof Error ? failure.message : "Could not apply this action.");
    } finally {
      setBusy(false);
    }
  };

  const open = () => {
    const defaults = Object.fromEntries(parameters.map(p => [p.valueId, p.value]));
    setValues(defaults);
    setParameterErrors({});
    setFormVersion(previous => previous + 1);
    setError("");
    setPreview(null);
    dialog.current?.showModal();
    if (!parameters.length) void plan(defaults);
  };

  return <div className={compact ? "action action--compact" : "action"}>
    <button type="button" className={`btn${compact ? " btn--small" : ""}${node.tone === "Danger" ? " btn--danger" : ""}`} aria-label={ariaLabel} title={compact ? node.description : undefined} disabled={busy} onClick={open}>{node.label}</button>
    {node.description && !compact && <span className="action__description">{node.description}</span>}
    <dialog ref={dialog} className="dialog review-dialog" aria-labelledby={`${id}-title`}>
      <form onSubmit={event => { event.preventDefault(); if (!busy && !invalid) void (preview?.planId && !stale ? apply() : plan()); }}>
        <div className="dialog__body">
          <h2 id={`${id}-title`} className="dialog__title">Preview: {node.label}</h2>
          <p>{scopeLabel}</p>
          {node.confirm && <p className="notice notice--Warning">{node.confirm}</p>}
          {!preview && parameters.map(p => {
            const value = values[p.valueId];
            return (
              <div className="field" key={`${formVersion}-${p.valueId}`}>
                <label className="field__label" htmlFor={`${id}-${p.valueId}`}>{p.label}</label>
                <FieldControl
                  id={`${id}-${p.valueId}`}
                  valueId={p.valueId}
                  actionId={node.actionId}
                  spec={p.spec}
                  value={value === undefined ? p.value : value}
                  disabled={busy}
                  onLocalError={message => setParameterErrors(previous => ({ ...previous, [p.valueId]: message }))}
                  onCommit={next => {
                    setValues(previous => ({ ...previous, [p.valueId]: next }));
                    return true;
                  }}
                />
                {parameterErrors[p.valueId] && <span className="field__error" role="alert">{parameterErrors[p.valueId]}</span>}
                {p.description && <span className="field__description">{p.description}</span>}
              </div>
            );
          })}
          {busy && <p role="status">Working…</p>}
          {error && <p role="alert" className="notice notice--Danger">{error}</p>}
          {stale && <p role="alert">The document or selection changed. Preview again before applying.</p>}
          {preview && <>
            <p>{preview.plan.changes.length} field changes · {preview.plan.skippedRows.length} skipped rows · one undo step</p>
            {preview.plan.errors.map((e, i) => <p role="alert" key={i}>{e.valueId}: {e.message}</p>)}
            {preview.plan.warnings.map((warning, i) => <p className="notice notice--Warning" key={i}>{warning}</p>)}
            <div className="review-scroll"><table className="review-table">
              <thead><tr><th>Field</th><th>Before</th><th>After</th></tr></thead>
              <tbody>{changesPage.items.map((change, i) => <tr key={i}>
                <th scope="row">{change.label}{change.derived && <small> (derived)</small>}</th>
                <td>{reviewValue(change.oldValue, change.oldExists)}</td><td>{reviewValue(change.newValue, change.newExists)}</td>
              </tr>)}</tbody>
            </table></div>
            <ReviewPager {...changesPage} label="Preview changes pages" />
            {preview.plan.skippedRows.length > 0 && <details><summary>Skipped rows</summary><ul>{skippedPage.items.map((row, i) => <li key={i}>{row.rowKey}: {row.reason}</li>)}</ul><ReviewPager {...skippedPage} label="Skipped rows pages" /></details>}
          </>}
        </div>
        <div className="dialog__footer">
          <button type="button" className="btn btn--quiet" disabled={busy} onClick={() => dialog.current?.close()}>Cancel</button>
          {preview && <button type="button" className="btn" disabled={busy} onClick={() => { setPreview(null); setError(""); }}>Change parameters</button>}
          <button type="submit" className={`btn ${node.tone === "Danger" ? "btn--danger" : "btn--primary"}`} disabled={busy || invalid || Boolean(preview?.planId && !stale && !doc.canEdit)}>
            {preview?.planId && !stale ? "Apply" : "Preview"}
          </button>
        </div>
      </form>
    </dialog>
  </div>;
}
