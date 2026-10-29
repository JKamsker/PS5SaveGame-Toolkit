import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useLocation, useNavigate, useParams, useSearchParams } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { api, ApiError } from "../../api/client";
import type { ToastMessage } from "../ui/Toast";
import { useEntryFocus } from "../ui/useEntryFocus";
import { useAsyncTask } from "../ui/useAsyncTask";
import { useLatestRequest } from "../ui/useLatestRequest";
import { useOpenDocument } from "./useOpenDocument";
import { WorkspaceActivity } from "../../state/workspaceActivity";

/** Route-scoped selection and close requests share the workspace's export/mutation barrier. */
export function useWorkspaceController() {
  const { workspaceId = "" } = useParams();
  const [search, setSearch] = useSearchParams();
  const documentId = search.get("doc") ?? undefined;
  const navigate = useNavigate();
  const location = useLocation();
  const beginSelection = useLatestRequest(`${workspaceId}:${location.key}`);
  // Pending mutation/export coordination belongs to one workspace, independent of document selection.
  const activity = useMemo(() => new WorkspaceActivity(), [workspaceId]);
  const exportDialog = useRef<HTMLDialogElement>(null);
  const [exportOpen, setExportOpen] = useState(false);
  const { busy: closing, run: closeTask } = useAsyncTask(workspaceId);
  const [toast, setToast] = useState<ToastMessage | null>(null);
  const workspace = useQuery({ queryKey: ["workspace", workspaceId], queryFn: () => api.workspace(workspaceId), retry: false });
  const entryState = workspace.data ? "loaded" : workspace.status;
  const focusHeading = useEntryFocus(`${workspaceId}:${documentId ?? "picker"}:${entryState}`);

  const notify = useCallback((text: string, isError = false) => setToast({ text, isError, at: Date.now() }), []);
  const select = useCallback((id: string) => setSearch({ doc: id }), [setSearch]);
  const open = useOpenDocument(workspaceId, beginSelection, select, notify);

  const close = () => closeTask(async isCurrent => {
    const hasEdits = activity.hasPendingMutations || workspace.data?.documents.some(document => document.isDirty);
    if (hasEdits && !window.confirm("Discard the edits in this dump and close it? Export a zip first if you want to keep them.")) return;
    const selectionIsCurrent = beginSelection();
    try {
      await api.deleteWorkspace(workspaceId);
      if (isCurrent() && selectionIsCurrent()) void navigate("/");
    } catch (error) {
      if (isCurrent() && selectionIsCurrent()) notify(error instanceof ApiError ? error.message : "Could not close the dump.", true);
    }
  });

  useEffect(() => {
    if (exportOpen) exportDialog.current?.showModal();
    else exportDialog.current?.close();
  }, [exportOpen]);

  return { workspace, documentId, activity, beginSelection, exportDialog, exportOpen, setExportOpen,
    closing, toast, setToast, focusHeading, notify, open, close, clearSelection: () => setSearch({}), select };
}
