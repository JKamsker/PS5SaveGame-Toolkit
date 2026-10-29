import { useCallback, useContext, useEffect, useMemo, useRef } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError } from "../../api/client";
import type { EditResult, FieldValue, SaveFileInfo, Workspace } from "../../api/protocol";
import { DocumentContext, DocumentStore, useDocumentState, type DocumentContextValue } from "../../state/documentStore";
import { SourcesPanel } from "./SourcesPanel";
import { ChangesPanel } from "./review/ChangesPanel";
import { ReviewNavigationProvider } from "./review/navigation";
import { ViewRenderer } from "./ViewRenderer";
import { DocumentGroupControls } from "./DocumentGroupControls";
import { useEntryFocus } from "../ui/useEntryFocus";
import { useAsyncTask } from "../ui/useAsyncTask";
import { sameValue } from "../../lib/values";
import { WorkspaceActivityContext } from "../../state/workspaceActivity";

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
  const activity = useContext(WorkspaceActivityContext);
  const query = useQuery({ queryKey: ["document", workspaceId, documentId], queryFn: () => api.document(workspaceId, documentId), refetchOnMount: "always" });
  const store = useMemo(() => (query.data ? new DocumentStore(query.data.state) : null), [query.data]);
  // Document identity deliberately starts a new request lifetime even when the initial values match.
  const lifetime = useMemo(() => ({ active: true, generation: 0, tail: Promise.resolve() }), [workspaceId, documentId]);
  const currentStore = useRef(store);
  const pendingEdits = useRef(new Map<string, { value: FieldValue; promise: Promise<boolean> }>());
  currentStore.current = store;
  useEffect(() => {
    lifetime.active = true;
    return () => { lifetime.active = false; };
  }, [lifetime]);
  const enqueue = useCallback(<T,>(call: () => Promise<T>): Promise<T> => {
    const next = lifetime.tail.then(call);
    lifetime.tail = next.then(() => {}, () => {});
    return next;
  }, [lifetime]);
  // Every successful snapshot replaces the store's value overrides. Its lazy children must use
  // that same generation, including reconnects and returning to a recently edited document.
  const structureVersion = query.dataUpdatedAt;
  const focusHeading = useEntryFocus(`${workspaceId}:${documentId}:${Boolean(query.data)}`);

  /** Refetches the view after a structural change; the new view data creates a fresh value cache. */
  const { refetch } = query;
  const refresh = useCallback(async () => {
    await enqueue(async () => { if (lifetime.active) await refetch(); });
  }, [refetch, enqueue, lifetime]);

  const handleResult = useCallback(
    async (result: EditResult) => {
      if (!lifetime.active || !currentStore.current) return;
      if (result.stale) {
        // Nothing changed on the server; the ids we sent belong to a replaced view.
        lifetime.generation++;
        await refetch();
        if (lifetime.active) onToast("The document changed. Please try again with the current view.", true);
        return;
      }
      currentStore.current.apply(result);
      if (result.structureChanged) { lifetime.generation++; await refetch(); }
      if (!lifetime.active) return;
      if (result.message) onToast(result.message);
      for (const w of result.warnings) onToast(w);
      // Close/export controls need the acknowledged dirty flag before the background read finishes.
      queryClient.setQueryData<Workspace>(["workspace", workspaceId], previous => previous && ({ ...previous,
        documents: previous.documents.map(document => document.id === documentId ? { ...document, isDirty: result.state.isDirty } : document),
      }));
      void queryClient.invalidateQueries({ queryKey: ["workspace", workspaceId] });
      void queryClient.invalidateQueries({ queryKey: ["export-plan", workspaceId] });
    },
    [lifetime, refetch, queryClient, workspaceId, documentId, onToast],
  );

  const run = useCallback(
    (call: () => Promise<EditResult>): Promise<EditResult> => {
      const generation = lifetime.generation;
      const rejected = (message: string): EditResult => ({ ok: false, errors: [{ valueId: "", message }], changes: [], warnings: [],
        structureChanged: false, state: currentStore.current?.state ?? query.data!.state });
      const request = enqueue(async () => {
        if (!lifetime.active) return rejected("The document is no longer open.");
        if (generation !== lifetime.generation || store !== currentStore.current) {
          return rejected("The document changed. Please try again with the current view.");
        }
        try {
          // A snapshot started before this mutation must not later replace its live values/history.
          await queryClient.cancelQueries({ queryKey: ["document", workspaceId, documentId] });
          if (!lifetime.active) return rejected("The document is no longer open.");
          const result = await call();
          // Also retire snapshots that began while this write was in flight.
          await queryClient.cancelQueries({ queryKey: ["document", workspaceId, documentId] });
          if (!lifetime.active) return rejected("The document is no longer open.");
          await handleResult(result);
          const globalError = result.errors.find(error => error.valueId === "");
          if (lifetime.active && !result.ok && !result.stale && globalError) onToast(globalError.message, true);
          return result;
        } catch (error) {
          // A failed/ambiguous write must not release already queued intents against an uncertain state.
          lifetime.generation++;
          const message = error instanceof ApiError ? error.message : "The server could not be reached.";
          if (lifetime.active) onToast(message, true);
          // Action dialogs need this diagnostic inside the modal, where background toasts are inert.
          return rejected(message);
        }
      });
      return activity?.track(request) ?? request;
    },
    [handleResult, onToast, queryClient, workspaceId, documentId, store, query.data, enqueue, lifetime, activity],
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
      mutate: run,
      commit: (valueId: string, value: FieldValue) => {
        const pending = pendingEdits.current.get(valueId);
        if (pending && sameValue(pending.value, value)) return pending.promise;
        const promise = run(() => api.edit(workspaceId, documentId, [{ valueId, value }], store.state.revision)).then(result => result.ok);
        const entry = { value, promise };
        pendingEdits.current.set(valueId, entry);
        void promise.finally(() => { if (pendingEdits.current.get(valueId) === entry) pendingEdits.current.delete(valueId); });
        return promise;
      },
      runAction: async (actionId: string, parameters: Record<string, FieldValue>) =>
        run(() => api.action(workspaceId, documentId, actionId, parameters, store.state.revision)),
    };
  }, [query.data, store, workspaceId, documentId, structureVersion, refresh, run, handleResult]);

  // A cached snapshot may predate edits made before switching files. Wait for the mount refresh
  // before exposing controls, so its response cannot overwrite a newly submitted edit.
  if (query.isPending || (!query.isFetchedAfterMount && query.isFetching)) return <div role="status" className="empty">Opening…</div>;
  if (!context || !query.data) {
    return (
      <div className="empty">
        <h2 ref={focusHeading} tabIndex={-1}>This file could not be opened</h2>
        <p role="alert">{query.error?.message ?? "Unknown error."}</p>
        <button type="button" className="btn btn--small" disabled={query.isFetching} onClick={() => void refresh()}>Retry document</button>
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
            <h1 ref={focusHeading} tabIndex={-1} className="editor__title">{doc.title}</h1>
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
        {query.isError && <div className="read-error">
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

function HistoryButtons({ store, onUndo, onRedo }: { store: DocumentStore; onUndo: () => Promise<unknown>; onRedo: () => Promise<unknown> }) {
  const { state } = useDocumentState(store);
  const { busy, run } = useAsyncTask(store);
  const undo = () => run(async () => { await onUndo(); });
  const redo = () => run(async () => { await onRedo(); });
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (document.querySelector("dialog[open]")) return;
      const target = e.target as HTMLElement | null;
      if (target && (target.tagName === "INPUT" || target.tagName === "TEXTAREA")) return;
      if (!(e.ctrlKey || e.metaKey) || e.key.toLowerCase() !== "z") return;
      e.preventDefault();
      if (e.shiftKey) {
        if (store.state.canRedo) void redo();
      } else if (store.state.canUndo) void undo();
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  });

  return (
    <div className="bar__actions">
      <button type="button" className="btn btn--small" disabled={busy || !state.canUndo} onClick={() => void undo()} title="Undo (Ctrl+Z)">
        Undo
      </button>
      <button type="button" className="btn btn--small" disabled={busy || !state.canRedo} onClick={() => void redo()} title="Redo (Ctrl+Shift+Z)">
        Redo
      </button>
    </div>
  );
}
