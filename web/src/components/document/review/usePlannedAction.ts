import { useId, useState } from "react";
import { actionReview, type ActionPreview, type ActionScope } from "../../../api/actionReview";
import type { ActionNode, FieldValue } from "../../../api/protocol";
import { useDocument, useDocumentState } from "../../../state/documentStore";
import { useActionDialog } from "../../ui/useActionDialog";
import { useAsyncTask } from "../../ui/useAsyncTask";
import { actionParameterDefaults, useWritableActionParameters } from "../actionParameters";
import { useActionSelection } from "../table/selectionBindings";
import { useReviewPage } from "./paging";

function usePlannedActionState(node: ActionNode) {
  const id = useId();
  const parameters = useWritableActionParameters(node);
  const [values, setValues] = useState<Record<string, FieldValue>>(() => actionParameterDefaults(parameters));
  const [preview, setPreview] = useState<ActionPreview | null>(null);
  const { busy, run: submit } = useAsyncTask(node.actionId);
  const modal = useActionDialog(busy);
  const [error, setError] = useState("");
  const [errors, setErrors] = useState<Record<string, string | undefined>>({});
  const [formVersion, setFormVersion] = useState(0);
  const [previewScope, setPreviewScope] = useState("");
  // Keep pagination mounted when the preview is hidden for parameter editing or reopening.
  const changesPage = useReviewPage(preview?.plan.changes ?? []);
  const skippedPage = useReviewPage(preview?.plan.skippedRows ?? []);
  const form = { parameters, values, setValues, errors, setErrors };
  return { id, form, preview, setPreview, busy, submit, ...modal, error, setError,
    formVersion, setFormVersion, previewScope, setPreviewScope, changesPage, skippedPage,
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
type PlannedScope = ReturnType<typeof useActionScope> & { stale: boolean };

function createActionPlan(node: ActionNode, state: PlannedState, selection: PlannedScope) {
  const { doc, binding, scope, scopeKey } = selection;
  return (params = state.form.values) => state.submit(async isCurrent => {
    if (state.invalid || binding.unavailable) return;
    state.focusHeading();
    state.setError("");
    state.setPreview(null);
    state.form.setErrors({});
    try {
      const result = await actionReview.plan(doc.workspaceId, doc.documentId, node.actionId, params, scope, doc.store.state.revision);
      if (!isCurrent()) return;
      state.setPreview(result);
      state.setPreviewScope(scopeKey);
      if (result.stale) await doc.refresh();
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
  const { doc, stale, binding } = selection;
  const { preview } = state;
  return () => state.submit(async isCurrent => {
    if (!preview?.planId || stale || binding.unavailable) return;
    state.focusHeading();
    state.setError("");
    try {
      const result = await doc.mutate(() => actionReview.apply(doc.workspaceId, doc.documentId, preview.planId!, preview.revision));
      if (!isCurrent()) return;
      if (result.ok) state.closeDialog();
      else {
        state.setPreview(null);
        state.setError(result.errors.map(error => error.message).join(" ") || "Apply failed. Preview the action again.");
        state.requestFocus("error");
      }
    } catch (failure) {
      if (!isCurrent()) return;
      state.setPreview(null);
      state.setError(failure instanceof Error ? failure.message : "Could not apply this action.");
      state.requestFocus("error");
    }
  });
}

export function usePlannedAction(node: ActionNode) {
  const scope = useActionScope(node);
  const state = usePlannedActionState(node);
  const selectedCount = state.preview?.scope.rowKeys?.length ?? scope.selectedCount;
  const scopeLabel = selectedCount === undefined ? "Whole document" : `${selectedCount} selected ${selectedCount === 1 ? "row" : "rows"}`;
  const stale = Boolean(state.preview && (state.preview.revision !== scope.revision || state.previewScope !== scope.scopeKey));
  const selection = { ...scope, scopeLabel, stale };
  const plan = createActionPlan(node, state, selection);
  const apply = createActionApply(state, selection);
  const open = () => {
    const defaults = actionParameterDefaults(state.form.parameters);
    state.form.setValues(defaults);
    state.form.setErrors({});
    state.setFormVersion(previous => previous + 1);
    state.setError("");
    state.setPreview(null);
    state.openDialog();
    if (!state.form.parameters.length) void plan(defaults);
  };
  const changeParameters = () => {
    state.setPreview(null);
    state.setError("");
    state.requestFocus("parameters");
  };
  return { ...state, ...selection, plan, apply, open, changeParameters };
}
