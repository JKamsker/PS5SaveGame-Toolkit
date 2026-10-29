import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useLocation, useNavigate, useParams, useSearchParams } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { api, ApiError } from "../../api/client";
import { DocumentEditor } from "../document/DocumentEditor";
import { Toast, type ToastMessage } from "../ui/Toast";
import { ExportDialog } from "./ExportDialog";
import { SlotRail } from "./SlotRail";
import { useEntryFocus } from "../ui/useEntryFocus";
import { useAsyncTask } from "../ui/useAsyncTask";
import { useLatestRequest } from "../ui/useLatestRequest";
import { WorkspaceSelectionContext } from "../../state/workspaceSelection";
import { useOpenDocument } from "./useOpenDocument";
import { WorkspaceActivity, WorkspaceActivityContext } from "../../state/workspaceActivity";

export function WorkspacePage() {
  const { workspaceId = "" } = useParams();
  const [search, setSearch] = useSearchParams();
  const documentId = search.get("doc") ?? undefined;
  const navigate = useNavigate();
  const location = useLocation();
  const beginSelection = useLatestRequest(`${workspaceId}:${location.key}`);
  const activity = useMemo(() => new WorkspaceActivity(), [workspaceId]);
  const exportDialog = useRef<HTMLDialogElement>(null);
  const [exportOpen, setExportOpen] = useState(false);
  const { busy: closing, run: closeTask } = useAsyncTask(workspaceId);
  const [toast, setToast] = useState<ToastMessage | null>(null);
  const workspace = useQuery({ queryKey: ["workspace", workspaceId], queryFn: () => api.workspace(workspaceId), retry: false });
  const focusHeading = useEntryFocus(`${workspaceId}:${documentId ?? "picker"}:${Boolean(workspace.data)}`);

  const notify = useCallback((text: string, isError = false) => setToast({ text, isError, at: Date.now() }), []);
  const select = useCallback((id: string) => setSearch({ doc: id }), [setSearch]);
  const open = useOpenDocument(workspaceId, beginSelection, select, notify);

  const close = () => closeTask(async isCurrent => {
    const hasEdits = activity.hasPendingMutations || workspace.data?.documents.some(document => document.isDirty);
    if (hasEdits && !window.confirm("Discard the edits in this dump and close it? Export a zip first if you want to keep them.")) return;
    const selectionIsCurrent = beginSelection();
    try {
      await api.deleteWorkspace(workspaceId);
      if (isCurrent() && selectionIsCurrent()) navigate("/");
    } catch (error) {
      if (isCurrent() && selectionIsCurrent()) notify(error instanceof ApiError ? error.message : "Could not close the dump.", true);
    }
  });

  useEffect(() => {
    if (exportOpen) exportDialog.current?.showModal();
    else exportDialog.current?.close();
  }, [exportOpen]);

  if (workspace.isError && !workspace.data) {
    return (
      <div className="empty">
        <h2 ref={focusHeading} tabIndex={-1}>{workspace.error instanceof ApiError && workspace.error.status === 404 ? "This workspace is gone" : "Could not load this workspace"}</h2>
        <p role="alert">{workspace.error.message}</p>
        <button type="button" className="btn btn--small" disabled={workspace.isFetching} onClick={() => void workspace.refetch()}>Retry workspace</button>
        <p>
          <Link to="/">Import a save dump</Link>
        </p>
      </div>
    );
  }
  if (!workspace.data) return <div role="status" className="empty">Loading…</div>;

  const ws = workspace.data;
  const activeDoc = ws.documents.find((d) => d.id === documentId);
  const activeFile = ws.titles.flatMap((t) => t.directories).flatMap((d) => d.files).find((f) => f.id === activeDoc?.fileId);
  const dirtyCount = ws.documents.filter((d) => d.isDirty).length;

  return (
    <WorkspaceActivityContext.Provider value={activity}><WorkspaceSelectionContext.Provider value={beginSelection}><div className="shell">
      <header className="bar">
        <Link to="/" className="bar__brand">
          <span className="bar__mark" aria-hidden="true" />
          Savebench
        </Link>
        <span className="bar__workspace">{ws.name}</span>
        <span className="bar__spacer" />
        <div className="bar__actions">
          <button
            type="button"
            className="btn btn--quiet btn--small"
            disabled={closing}
            onClick={() => void close()}
          >
            {closing ? "Closing…" : "Close dump"}
          </button>
          <button type="button" className="btn btn--primary" onClick={() => setExportOpen(true)}>
            Export
            {dirtyCount > 0 && <span className="btn__count" aria-label={`${dirtyCount} edited files`}>{dirtyCount}</span>}
          </button>
        </div>
      </header>
      <SlotRail workspace={ws} activeFileId={activeDoc?.fileId} onOpen={(f) => { if (!closing) void open(f); }} />
      <main className="main" inert={closing}>
        {workspace.isError && <div>
          <p role="alert" className="notice notice--Danger">Could not refresh the workspace: {workspace.error.message}</p>
          <button type="button" className="btn btn--small" disabled={workspace.isFetching} onClick={() => void workspace.refetch()}>Retry workspace</button>
        </div>}
        {activeDoc ? (
          <DocumentEditor
            key={`${ws.id}:${activeDoc.id}`}
            workspaceId={ws.id}
            documentId={activeDoc.id}
            file={activeFile}
            onSwitchPlugin={(pluginId) => { if (activeFile && !closing) void open(activeFile, pluginId); }}
            onToast={notify}
          />
        ) : (
          <div className="empty">
            <h2 ref={focusHeading} tabIndex={-1}>Pick a save file</h2>
            <p>Choose a slot on the left, then a file. Files a game plugin understands are marked in blue.</p>
          </div>
        )}
      </main>
      <ExportDialog ref={exportDialog} workspaceId={ws.id} open={exportOpen} onClose={() => setExportOpen(false)} onToast={notify} />
      {toast && <Toast message={toast} onDone={() => setToast(null)} />}
    </div></WorkspaceSelectionContext.Provider></WorkspaceActivityContext.Provider>
  );
}
