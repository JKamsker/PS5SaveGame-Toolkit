import { lazy, Suspense } from "react";
import { Link } from "react-router";
import type { Workspace } from "../../api/protocol";
import { ApiError } from "../../api/client";
import { WorkspaceActivityContext } from "../../state/workspaceActivity";
import { WorkspaceSelectionContext } from "../../state/workspaceSelection";
import { Toast } from "../ui/Toast";
import { ExportDialog } from "./ExportDialog";
import { SlotRail } from "./SlotRail";
import { WorkspaceHeader } from "./WorkspaceHeader";
import { useWorkspaceController } from "./useWorkspaceController";

const DocumentEditor = lazy(() => import("../document/DocumentEditor").then(module => ({ default: module.DocumentEditor })));

type Controller = ReturnType<typeof useWorkspaceController>;

function findActiveFile(workspace: Workspace, fileId: string | undefined) {
  return workspace.titles.flatMap(title => title.directories).flatMap(directory => directory.files).find(file => file.id === fileId);
}

export function WorkspacePage() {
  const controller = useWorkspaceController();
  const { workspace, activity, beginSelection, closing, close, open, exportDialog, exportOpen, setExportOpen, notify, toast, setToast, focusHeading } = controller;
  if (workspace.isError && !workspace.data) return <WorkspaceError controller={controller} />;
  if (!workspace.data) return <div ref={focusHeading} tabIndex={-1} role="status" className="empty">Loading…</div>;
  const ws = workspace.data;
  const activeDoc = ws.documents.find(document => document.id === controller.documentId);
  const dirtyCount = ws.documents.filter(document => document.isDirty).length;
  return (
    <WorkspaceActivityContext.Provider value={activity}><WorkspaceSelectionContext.Provider value={beginSelection}><div className="shell">
      <WorkspaceHeader name={ws.name} dirtyCount={dirtyCount} closing={closing} onClose={close} onExport={() => setExportOpen(true)} />
      <SlotRail workspace={ws} activeFileId={activeDoc?.fileId} onOpen={file => { if (!closing) void open(file); }} />
      <WorkspaceMain controller={controller} workspace={ws} />
      <ExportDialog ref={exportDialog} workspaceId={ws.id} open={exportOpen} onClose={() => setExportOpen(false)} onToast={notify} />
      {toast && <Toast message={toast} onDone={() => setToast(null)} />}
    </div></WorkspaceSelectionContext.Provider></WorkspaceActivityContext.Provider>
  );
}

function WorkspaceError({ controller: { workspace, focusHeading } }: { controller: Controller }) {
  return (
    <div className="empty">
      <h2 ref={focusHeading} tabIndex={-1}>{workspace.error instanceof ApiError && workspace.error.status === 404 ? "This workspace is gone" : "Could not load this workspace"}</h2>
      <p role="alert">{workspace.error?.message}</p>
      <button type="button" className="btn btn--small" disabled={workspace.isFetching} onClick={() => void workspace.refetch()}>Retry workspace</button>
      <p>
        <Link to="/">Import a save dump</Link>
      </p>
    </div>
  );
}

function WorkspaceMain({ controller, workspace: ws }: { controller: Controller; workspace: Workspace }) {
  const { workspace, documentId, closing, open, notify, focusHeading } = controller;
  const activeDoc = ws.documents.find(document => document.id === documentId);
  const activeFile = findActiveFile(ws, activeDoc?.fileId);
  return (
    <main className="main" inert={closing}>
      {workspace.isError && <div className="read-error">
        <p role="alert" className="notice notice--Danger">Could not refresh the workspace: {workspace.error?.message}</p>
        <button type="button" className="btn btn--small" disabled={workspace.isFetching} onClick={() => void workspace.refetch()}>Retry workspace</button>
      </div>}
      {activeDoc ? (
        <Suspense fallback={<div role="status" className="empty">Opening…</div>}><DocumentEditor
          key={`${ws.id}:${activeDoc.id}`}
          workspaceId={ws.id}
          documentId={activeDoc.id}
          file={activeFile}
          onSwitchPlugin={(pluginId) => { if (activeFile && !closing) void open(activeFile, pluginId); }}
          onToast={notify}
          onClosed={controller.clearSelection}
          onOpenGroup={controller.select}
        /></Suspense>
      ) : (
        <div className="empty">
          <h2 ref={focusHeading} tabIndex={-1}>Pick a save file</h2>
          <p>Choose a slot on the left, then a file. Files a game plugin understands are marked in blue.</p>
        </div>
      )}
    </main>
  );
}
