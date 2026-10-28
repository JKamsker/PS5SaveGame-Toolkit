import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError } from "../../api/client";
import type { SaveFileInfo } from "../../api/protocol";
import { DocumentEditor } from "../document/DocumentEditor";
import { Toast, type ToastMessage } from "../ui/Toast";
import { ExportDialog } from "./ExportDialog";
import { SlotRail } from "./SlotRail";

export function WorkspacePage() {
  const { workspaceId = "" } = useParams();
  const [search, setSearch] = useSearchParams();
  const documentId = search.get("doc") ?? undefined;
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const exportDialog = useRef<HTMLDialogElement>(null);
  const [exportOpen, setExportOpen] = useState(false);
  const [toast, setToast] = useState<ToastMessage | null>(null);
  const workspace = useQuery({ queryKey: ["workspace", workspaceId], queryFn: () => api.workspace(workspaceId), retry: false });

  const notify = useCallback((text: string, isError = false) => setToast({ text, isError, at: Date.now() }), []);

  const open = useCallback(
    async (file: SaveFileInfo, pluginId?: string) => {
      try {
        if (file.openDocumentId && !pluginId) {
          setSearch({ doc: file.openDocumentId });
          return;
        }
        const doc = await api.open(workspaceId, file.id, pluginId);
        queryClient.setQueryData(["document", workspaceId, doc.id], doc);
        setSearch({ doc: doc.id });
        void queryClient.invalidateQueries({ queryKey: ["workspace", workspaceId] });
      } catch (error) {
        notify(error instanceof ApiError ? error.message : "Could not open the file.", true);
      }
    },
    [workspaceId, queryClient, setSearch, notify],
  );

  useEffect(() => {
    if (exportOpen) exportDialog.current?.showModal();
    else exportDialog.current?.close();
  }, [exportOpen]);

  if (workspace.isError) {
    return (
      <div className="empty">
        <h2>This workspace is gone</h2>
        <p>{workspace.error.message}</p>
        <p>
          <Link to="/">Import a save dump</Link>
        </p>
      </div>
    );
  }
  if (!workspace.data) return <div className="empty">Loading…</div>;

  const ws = workspace.data;
  const activeDoc = ws.documents.find((d) => d.id === documentId);
  const activeFile = ws.titles.flatMap((t) => t.directories).flatMap((d) => d.files).find((f) => f.id === activeDoc?.fileId);
  const dirtyCount = ws.documents.filter((d) => d.isDirty).length;

  return (
    <div className="shell">
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
            onClick={async () => {
              await api.deleteWorkspace(ws.id);
              navigate("/");
            }}
          >
            Close dump
          </button>
          <button type="button" className="btn btn--primary" onClick={() => setExportOpen(true)}>
            Export
            {dirtyCount > 0 && <span className="btn__count" aria-label={`${dirtyCount} edited files`}>{dirtyCount}</span>}
          </button>
        </div>
      </header>
      <SlotRail workspace={ws} activeFileId={activeDoc?.fileId} onOpen={(f) => void open(f)} />
      <main className="main">
        {activeDoc ? (
          <DocumentEditor
            key={activeDoc.id}
            workspaceId={ws.id}
            documentId={activeDoc.id}
            file={activeFile}
            onSwitchPlugin={(pluginId) => activeFile && void open(activeFile, pluginId)}
            onToast={notify}
          />
        ) : (
          <div className="empty">
            <h2>Pick a save file</h2>
            <p>Choose a slot on the left, then a file. Files a game plugin understands are marked in blue.</p>
          </div>
        )}
      </main>
      <ExportDialog ref={exportDialog} workspaceId={ws.id} open={exportOpen} onClose={() => setExportOpen(false)} onToast={notify} />
      {toast && <Toast message={toast} onDone={() => setToast(null)} />}
    </div>
  );
}
