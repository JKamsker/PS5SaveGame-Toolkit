import { useCallback, useEffect, useMemo } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError } from "../../api/client";
import type { EditResult, FieldValue, SaveFileInfo } from "../../api/protocol";
import { DocumentContext, DocumentStore, useDocumentState, type DocumentContextValue } from "../../state/documentStore";
import { SourcesPanel } from "./SourcesPanel";
import { ChangesPanel } from "./review/ChangesPanel";
import { ReviewNavigationProvider } from "./review/navigation";
import { ViewRenderer } from "./ViewRenderer";
import { DocumentGroupControls } from "./DocumentGroupControls";

interface DocumentEditorProps {
  workspaceId: string;
  documentId: string;
  file: SaveFileInfo | undefined;
  onSwitchPlugin: (pluginId: string) => void;
  onToast: (message: string, isError?: boolean) => void;
}

const maturityText = { ReadOnly: "read-only", Experimental: "experimental", Stable: "tested in-game" } as const;

export function DocumentEditor({ workspaceId, documentId, file, onSwitchPlugin, onToast }: DocumentEditorProps) {
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: ["document", workspaceId, documentId], queryFn: () => api.document(workspaceId, documentId), refetchOnMount: "always" });
  const store = useMemo(() => (query.data ? new DocumentStore(query.data.state) : null), [query.data]);
  // Every successful snapshot replaces the store's value overrides. Its lazy children must use
  // that same generation, including reconnects and returning to a recently edited document.
  const structureVersion = query.dataUpdatedAt;

  /** Refetches the view after a structural change; the new view data creates a fresh value cache. */
  const { refetch } = query;
  const refresh = useCallback(async () => {
    await refetch();
  }, [refetch]);

  const handleResult = useCallback(
    async (result: EditResult) => {
      if (!store) return;
      if (result.stale) {
        // Nothing changed on the server; the ids we sent belong to a replaced view.
        await refresh();
        onToast("The document changed. Please try again with the current view.", true);
        return;
      }
      store.apply(result);
      if (result.structureChanged) await refresh();
      if (result.message) onToast(result.message);
      for (const w of result.warnings) onToast(w);
      void queryClient.invalidateQueries({ queryKey: ["workspace", workspaceId] });
    },
    [store, refresh, queryClient, workspaceId, onToast],
  );

  const run = useCallback(
    async (call: () => Promise<EditResult>): Promise<EditResult | null> => {
      try {
        // A snapshot started before this mutation must not later replace its live values/history.
        await queryClient.cancelQueries({ queryKey: ["document", workspaceId, documentId] });
        const result = await call();
        await handleResult(result);
        if (!result.ok && !result.stale && result.errors.some((e) => e.valueId === "")) onToast(result.errors[0]?.message ?? "Rejected", true);
        return result;
      } catch (error) {
        onToast(error instanceof ApiError ? error.message : "The server could not be reached.", true);
        return null;
      }
    },
    [handleResult, onToast, queryClient, workspaceId, documentId],
  );

  const context = useMemo<DocumentContextValue | null>(() => {
    if (!query.data || !store) return null;
    return {
      workspaceId,
      documentId,
      canEdit: query.data.canEdit,
      store,
      structureVersion,
      refresh,
      acceptResult: handleResult,
      commit: async (valueId: string, value: FieldValue) =>
        (await run(() => api.edit(workspaceId, documentId, [{ valueId, value }], store.state.revision)))?.ok ?? false,
      runAction: async (actionId: string, parameters: Record<string, FieldValue>) =>
        (await run(() => api.action(workspaceId, documentId, actionId, parameters, store.state.revision))) ?? {
          ok: false,
          errors: [],
          changes: [],
          warnings: [],
          structureChanged: false,
          state: store.state,
        },
    };
  }, [query.data, store, workspaceId, documentId, structureVersion, refresh, run, handleResult]);

  // A cached snapshot may predate edits made before switching files. Wait for the mount refresh
  // before exposing controls, so its response cannot overwrite a newly submitted edit.
  if (query.isPending || (!query.isFetchedAfterMount && query.isFetching)) return <div className="empty">Opening…</div>;
  if (!context || !query.data) {
    return (
      <div className="empty">
        <h2>This file could not be opened</h2>
        <p>{query.error?.message ?? "Unknown error."}</p>
      </div>
    );
  }

  const doc = query.data;
  return (
    <DocumentContext.Provider value={context}>
      <ReviewNavigationProvider>
        <article className="editor">
        <header className="editor__head">
          <div>
            <h1 className="editor__title">{doc.title}</h1>
            <span className="editor__file mono">{doc.filePath}</span>
          </div>
          <HistoryButtons store={context.store} onUndo={() => run(() => api.undo(workspaceId, documentId))} onRedo={() => run(() => api.redo(workspaceId, documentId))} />
          {!doc.groupMembers && <a className="btn btn--small btn--quiet" href={api.downloadUrl(workspaceId, documentId)} download>
            Download file
          </a>}
          <div className="editor__plugin">
            <span>
              {doc.plugin.name} <span className={`tag tag--${doc.plugin.maturity.toLowerCase()}`}>{maturityText[doc.plugin.maturity]}</span>
            </span>
            {!doc.groupMembers && file && file.matches.length > 1 && (
              <label>
                <span className="visually-hidden">Open with</span>
                <select value={doc.plugin.id} onChange={(e) => onSwitchPlugin(e.target.value)}>
                  {file.matches.map((m) => (
                    <option key={m.pluginId} value={m.pluginId}>
                      Open with {m.pluginName}
                    </option>
                  ))}
                </select>
              </label>
            )}
          </div>
        </header>
        {query.isError && <div>
          <p role="alert" className="notice notice--Danger">Could not refresh the document: {query.error.message}</p>
          <button type="button" className="btn btn--small" disabled={query.isFetching} onClick={() => void refresh()}>Refresh document</button>
        </div>}
        {(doc.plugin.supportsGroups || doc.groupMembers) && <DocumentGroupControls document={doc} />}
        <SourcesPanel
          workspaceId={workspaceId}
          documentId={documentId}
          pluginReferences={doc.plugin.references}
          refreshKey={structureVersion}
          onToast={onToast}
        />
        {!doc.canEdit && <p className="notice notice--Muted">This plugin shows the data but cannot write it.</p>}
        <ChangesPanel />
        <ViewRenderer node={doc.view} />
        </article>
      </ReviewNavigationProvider>
    </DocumentContext.Provider>
  );
}

function HistoryButtons({ store, onUndo, onRedo }: { store: DocumentStore; onUndo: () => void; onRedo: () => void }) {
  const { state } = useDocumentState(store);
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null;
      if (target && (target.tagName === "INPUT" || target.tagName === "TEXTAREA")) return;
      if (!(e.ctrlKey || e.metaKey) || e.key.toLowerCase() !== "z") return;
      e.preventDefault();
      if (e.shiftKey) {
        if (store.state.canRedo) onRedo();
      } else if (store.state.canUndo) onUndo();
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [store, onUndo, onRedo]);

  return (
    <div className="bar__actions">
      <button type="button" className="btn btn--small" disabled={!state.canUndo} onClick={onUndo} title="Undo (Ctrl+Z)">
        Undo
      </button>
      <button type="button" className="btn btn--small" disabled={!state.canRedo} onClick={onRedo} title="Redo (Ctrl+Shift+Z)">
        Redo
      </button>
    </div>
  );
}
