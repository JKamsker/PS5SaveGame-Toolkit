import { useCallback, useSyncExternalStore } from "react";
import { useQueryClient, type QueryClient } from "@tanstack/react-query";

interface CloseState { readonly pending: boolean; readonly error: string | null }
const idle: CloseState = { pending: false, error: null };

/** A server close outlives selection changes and editor remounts, but not its application client. */
class DocumentClosures {
  private readonly states = new Map<string, CloseState>();
  private readonly listeners = new Set<() => void>();
  private key(workspaceId: string, documentId: string) { return JSON.stringify([workspaceId, documentId]); }
  get(workspaceId: string, documentId: string): CloseState { return this.states.get(this.key(workspaceId, documentId)) ?? idle; }
  subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => { this.listeners.delete(listener); };
  };
  private set(workspaceId: string, documentId: string, state: CloseState) {
    const key = this.key(workspaceId, documentId);
    if (state === idle) this.states.delete(key); else this.states.set(key, state);
    this.listeners.forEach(listener => listener());
  }
  async run(workspaceId: string, documentId: string, validate: () => void, close: () => Promise<void>): Promise<void> {
    if (this.get(workspaceId, documentId).pending) return;
    try {
      // Refused closes must not unmount a dirty editor or an editor with pending writes.
      validate();
      this.set(workspaceId, documentId, { pending: true, error: null });
      await close();
      this.set(workspaceId, documentId, idle);
    } catch (failure) {
      this.set(workspaceId, documentId, { pending: false,
        error: failure instanceof Error ? failure.message : "Could not close the document. Try again." });
    }
  }
}

const clients = new WeakMap<QueryClient, DocumentClosures>();
export function documentClosures(client: QueryClient): DocumentClosures {
  let closures = clients.get(client);
  if (!closures) { closures = new DocumentClosures(); clients.set(client, closures); }
  return closures;
}

export function useDocumentCloseState(workspaceId: string, documentId: string): CloseState {
  const closures = documentClosures(useQueryClient());
  const snapshot = useCallback(() => closures.get(workspaceId, documentId), [closures, workspaceId, documentId]);
  return useSyncExternalStore(closures.subscribe, snapshot);
}
