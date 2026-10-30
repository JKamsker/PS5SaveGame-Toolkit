import { useEffect, useId, useMemo, useRef, useState } from "react";
import { actionReview, type ActionPreview, type ActionScope } from "../../../api/actionReview";
import { isStaleError } from "../../../api/client";
import type { ActionNode, FieldValue } from "../../../api/protocol";
import { useDocument, useDocumentState } from "../../../state/documentStore";
import { useActionDialog } from "../../ui/useActionDialog";
import { useAsyncTask } from "../../ui/useAsyncTask";
import { actionParameterDefaults, useWritableActionParameters } from "../actionParameters";
import { useActionSelection } from "../table/selectionBindings";
import { useReviewPage } from "./paging";

function usePlannedActionState(node: ActionNode, claimSnapshotErrors?: () => () => void) {
  const id = useId();
  const parameters = useWritableActionParameters(node);
  const [values, setValues] = useState<Record<string, FieldValue>>(() => actionParameterDefaults(parameters));
  const [preview, setPreview] = useState<ActionPreview | null>(null);
  const [dialogVersion, setDialogVersion] = useState(0);
  const taskOwner = useMemo(() => ({ actionId: node.actionId, dialogVersion }), [node.actionId, dialogVersion]);
  const { busy, run: submit } = useAsyncTask(taskOwner);
  const modal = useActionDialog(busy);
  const releaseReadErrors = useRef<(() => void) | undefined>(undefined);
  useEffect(() => () => { releaseReadErrors.current?.(); }, [claimSnapshotErrors]);
  const releaseSnapshotErrors = () => {
    releaseReadErrors.current?.();
    releaseReadErrors.current = undefined;
  };
  const [refreshError, setRefreshError] = useState("");
  const onClose = () => {
    // Native Escape can close a busy dialog. Retire its UI observer without cancelling
    // a mutation: DocumentSession still accepts any server result for the document.
    setDialogVersion(previous => previous + 1);
    setPreview(null);
    setRefreshError("");
    releaseSnapshotErrors();
    modal.onClose();
  };
  const [error, setError] = useState("");
  const [errors, setErrors] = useState<Record<string, string | undefined>>({});
  const [formVersion, setFormVersion] = useState(0);
  const [previewScope, setPreviewScope] = useState("");
  const [rejectedStale, setRejectedStale] = useState(false);
  // Keep pagination mounted when the preview is hidden for parameter editing or reopening.
  const changesPage = useReviewPage(preview?.plan.changes ?? []);
  const effectsPage = useReviewPage((preview?.plan.changes ?? []).filter(change => change.effect));
  const skippedPage = useReviewPage(preview?.plan.skippedRows ?? []);
  const form = { parameters, values, setValues, errors, setErrors };
  const openDialog = () => {
    releaseSnapshotErrors();
    releaseReadErrors.current = claimSnapshotErrors?.();
    modal.openDialog();
  };
  return { id, form, preview, setPreview, busy, submit, ...modal, openDialog, onClose, error, setError,
    formVersion, setFormVersion, previewScope, setPreviewScope, rejectedStale, setRejectedStale, refreshError, setRefreshError, changesPage, effectsPage, skippedPage,
    invalid: Object.values(errors).some(Boolean) };
}

function useActionScope(node: ActionNode) {
  const doc = useDocument();
  const { state } = useDocumentState(doc.store);
  const binding = useActionSelection(node.selectionTableId);
  const selection = binding.selection;
  const documentScope = node.selectionTableId != null && selection?.count === 0 && !selection.queryScope;
  const queryScope = selection?.queryScope;
  const scope: ActionScope = queryScope ? { queryScope: {
    collectionId: queryScope.collectionId,
    query: { text: queryScope.query.q, filters: queryScope.query.filters, sortColumn: queryScope.query.sort, descending: queryScope.query.descending, includeEmpty: queryScope.query.includeEmpty },
    revision: queryScope.revision,
    excludedRowKeys: queryScope.excludedRowKeys,
  } } : selection && !documentScope ? { rowKeys: [...selection.selectedKeys] } : {};
  const scopeKey = JSON.stringify(scope);
  return { doc, binding, scope, scopeKey, revision: state.revision, selectedCount: documentScope ? undefined : selection?.count };
}

type PlannedState = ReturnType<typeof usePlannedActionState>;
type PlannedScope = ReturnType<typeof useActionScope> & { stale: boolean; refreshError?: string };

function createActionPlan(node: ActionNode, state: PlannedState, selection: PlannedScope) {
  const { doc, binding, scope, scopeKey } = selection;
  return (params = state.form.values) => state.submit(async isCurrent => {
    if (state.invalid || binding.unavailable || state.refreshError || doc.snapshotError || doc.snapshotRefresh) return;
    state.focusHeading();
    state.setError("");
    state.setRejectedStale(false);
    state.setPreview(null);
    state.form.setErrors({});
    try {
      const result = await actionReview.plan(doc.workspaceId, doc.documentId, node.actionId, params, scope, doc.store.state.revision);
      if (!isCurrent()) return;
      state.setPreview(result);
      state.setRejectedStale(Boolean(result.stale));
      state.setPreviewScope(scopeKey);
      if (result.stale) {
        try { await doc.refresh({ throwOnError: true }); }
        catch (failure) {
          if (!isCurrent()) return;
          state.setPreview(null);
          state.setRefreshError(failure instanceof Error ? failure.message : "The document snapshot could not be refreshed.");
          state.requestFocus("error");
          return;
        }
      }
      if (!isCurrent()) return;
      state.requestFocus(result.plan.errors.length ? "error" : "heading");
    } catch (failure) {
      if (!isCurrent()) return;
      state.setError(failure instanceof Error ? failure.message : "Could not preview this action.");
      state.requestFocus("error");
    }
  });
}

function createActionApply(state: PlannedState, selection: PlannedScope) {
  const { doc, stale, binding, refreshError } = selection;
  const { preview } = state;
  return () => state.submit(async isCurrent => {
    if (!preview?.planId || !preview.plan.changes.length || stale || refreshError || doc.snapshotRefresh || binding.unavailable || !doc.canEdit) return;
    state.focusHeading();
    state.setError("");
    try {
      const result = await doc.mutate(() => actionReview.apply(doc.workspaceId, doc.documentId, preview.planId!, preview.revision), { failureFeedback: "dialog" });
      if (!isCurrent()) return;
      if (result.ok) state.closeDialog();
      else {
        state.setPreview(null);
        state.setRejectedStale(Boolean(result.stale));
        state.setRefreshError(result.snapshotError ?? "");
        state.setError([...result.errors.map(error => error.message), result.message, ...result.warnings]
          .filter(Boolean).join(" ") || "Apply failed. Preview the action again.");
        state.requestFocus("error");
      }
    } catch (failure) {
      if (!isCurrent()) return;
      state.setPreview(null);
      state.setRejectedStale(isStaleError(failure));
      state.setError(failure instanceof Error ? failure.message : "Could not apply this action.");
      state.requestFocus("error");
    }
  });
}

function createSnapshotRetry(state: PlannedState, { doc }: PlannedScope) {
  return () => state.submit(async isCurrent => {
    if (doc.snapshotRefresh === "pending") return;
    state.focusHeading();
    state.setPreview(null);
    state.setError("");
    try {
      // The mutation has completed. This read can now safely use the ordinary document queue.
      await doc.refresh({ throwOnError: true });
      if (!isCurrent()) return;
      state.setRefreshError("");
      state.requestFocus("heading");
    } catch (failure) {
      if (!isCurrent()) return;
      state.setRefreshError(failure instanceof Error ? failure.message : "The document snapshot could not be refreshed.");
      state.requestFocus("error");
    }
  });
}

export function usePlannedAction(node: ActionNode) {
  const scope = useActionScope(node);
  const state = usePlannedActionState(node, scope.doc.claimSnapshotErrors);
  const { snapshotRefresh, snapshotError } = scope.doc;
  const { dialog, requestFocus } = state;
  useEffect(() => {
    // A read may finish after Escape/reopen retired the mutation's original UI observer.
    if (snapshotRefresh === "required" && dialog.current?.open) requestFocus("error");
  }, [snapshotRefresh, snapshotError, dialog, requestFocus]);
  const selectedCount = state.preview?.scope.rowKeys?.length ?? scope.selectedCount;
  const scopeLabel = selectedCount === undefined ? "Whole document" : `${selectedCount} selected ${selectedCount === 1 ? "row" : "rows"}`;
  const stale = state.rejectedStale || Boolean(state.preview && (state.preview.stale || state.preview.revision !== scope.revision || state.previewScope !== scope.scopeKey));
  const selection = { ...scope, scopeLabel, stale, refreshError: state.refreshError || scope.doc.snapshotError };
  const plan = createActionPlan(node, state, selection);
  const apply = createActionApply(state, selection);
  const retrySnapshot = createSnapshotRetry(state, selection);
  const open = () => {
    const defaults = actionParameterDefaults(state.form.parameters);
    if (!snapshotRefresh && !snapshotError) state.form.setValues(defaults);
    state.form.setErrors({});
    state.setFormVersion(previous => previous + 1);
    state.setError("");
    state.setRejectedStale(Boolean(snapshotRefresh));
    state.setRefreshError("");
    state.setPreview(null);
    state.openDialog();
    if (snapshotRefresh === "required") state.requestFocus("error");
    if (!state.form.parameters.length && !snapshotRefresh && !snapshotError) void plan(defaults);
  };
  const changeParameters = () => {
    state.setPreview(null);
    state.setError("");
    state.requestFocus("parameters");
  };
  return { ...state, ...selection, plan, apply, retrySnapshot, open, changeParameters };
}
