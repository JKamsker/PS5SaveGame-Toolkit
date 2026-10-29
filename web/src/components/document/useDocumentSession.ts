/* eslint-disable react-hooks/refs, react-hooks/immutability -- Preserve the identity-scoped request controller and render-time store snapshot used by the editor. */
import { useCallback, useContext, useEffect, useMemo, useRef } from "react";
import { useQuery, useQueryClient, type UseQueryResult } from "@tanstack/react-query";
import { api, ApiError } from "../../api/client";
import type { EditResult, FieldValue, SaveDocument, Workspace } from "../../api/protocol";
import { DocumentStore, type DocumentContextValue } from "../../state/documentStore";
import { sameValue } from "../../lib/values";
import { WorkspaceActivityContext } from "../../state/workspaceActivity";

type DocumentQuery = UseQueryResult<SaveDocument, Error>;
type Notify = (message: string, isError?: boolean) => void;

function useDocumentLifetime(workspaceId: string, documentId: string, store: DocumentStore | null) {
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
  return { lifetime, currentStore, pendingEdits, enqueue };
}

type SessionLifetime = ReturnType<typeof useDocumentLifetime>;

function useDocumentResult(workspaceId: string, documentId: string, query: DocumentQuery, session: SessionLifetime, onToast: Notify) {
  const queryClient = useQueryClient();
  const { lifetime, currentStore, enqueue } = session;
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
    [lifetime, currentStore, refetch, queryClient, workspaceId, documentId, onToast],
  );

  return { refresh, handleResult };
}

function useDocumentMutation(workspaceId: string, documentId: string, query: DocumentQuery, store: DocumentStore | null,
  session: SessionLifetime, handleResult: DocumentContextValue["acceptResult"], onToast: Notify) {
  const queryClient = useQueryClient();
  const activity = useContext(WorkspaceActivityContext);
  const { lifetime, currentStore, enqueue } = session;
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
    [handleResult, onToast, queryClient, workspaceId, documentId, store, query.data, enqueue, lifetime, currentStore, activity],
  );

  return run;
}

function useDocumentContext(workspaceId: string, documentId: string, query: DocumentQuery, store: DocumentStore | null,
  pendingEdits: SessionLifetime["pendingEdits"], refresh: DocumentContextValue["refresh"],
  handleResult: DocumentContextValue["acceptResult"], run: DocumentContextValue["mutate"]) {
  const structureVersion = query.dataUpdatedAt;
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
  }, [query.data, store, workspaceId, documentId, structureVersion, refresh, run, handleResult, pendingEdits]);

  return context;
}

/** Owns reads, serialized writes and deduplicated commits for one document identity. */
export function useDocumentSession(workspaceId: string, documentId: string, onToast: Notify) {
  const query = useQuery({ queryKey: ["document", workspaceId, documentId], queryFn: () => api.document(workspaceId, documentId), refetchOnMount: "always" });
  const store = useMemo(() => (query.data ? new DocumentStore(query.data.state) : null), [query.data]);
  const session = useDocumentLifetime(workspaceId, documentId, store);
  const { refresh, handleResult } = useDocumentResult(workspaceId, documentId, query, session, onToast);
  const run = useDocumentMutation(workspaceId, documentId, query, store, session, handleResult, onToast);
  const context = useDocumentContext(workspaceId, documentId, query, store, session.pendingEdits, refresh, handleResult, run);
  // Each successful snapshot replaces the value cache; lazy children share that generation.
  return { query, context, refresh, structureVersion: query.dataUpdatedAt };
}
