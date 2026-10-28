import { createContext, useCallback, useContext, useSyncExternalStore } from "react";
import type { DocumentState, EditResult, FieldValue, ValueChange } from "../api/protocol";

type Listener = () => void;

/**
 * Client-side cache of live values for one open document. View nodes carry the value at the time
 * they were fetched; edits, undo and actions report changes which override those snapshots here,
 * so tables, lazy sections and the main view stay consistent without refetching.
 */
export class DocumentStore {
  private values = new Map<string, FieldValue>();
  private original = new Map<string, FieldValue>();
  private errors = new Map<string, string>();
  private listeners = new Map<string, Set<Listener>>();
  private globalListeners = new Set<Listener>();
  state: DocumentState;
  lastMessage: string | null = null;

  constructor(state: DocumentState) {
    this.state = state;
  }

  /** Records the first value seen for an id, which defines "modified in this session". */
  seed(valueId: string, value: FieldValue): void {
    if (!this.original.has(valueId)) this.original.set(valueId, value);
  }

  value(valueId: string, fallback: FieldValue): FieldValue {
    return this.values.has(valueId) ? (this.values.get(valueId) as FieldValue) : fallback;
  }

  isModified(valueId: string): boolean {
    return this.values.has(valueId) && this.original.has(valueId) && this.values.get(valueId) !== this.original.get(valueId);
  }

  error(valueId: string): string | undefined {
    return this.errors.get(valueId);
  }

  setError(valueId: string, message: string | undefined): void {
    if (message === undefined) this.errors.delete(valueId);
    else this.errors.set(valueId, message);
    this.emit([valueId]);
  }

  apply(result: EditResult): void {
    const touched = result.changes.map((c: ValueChange) => {
      this.seed(c.valueId, c.oldValue);
      this.values.set(c.valueId, c.newValue);
      this.errors.delete(c.valueId);
      return c.valueId;
    });
    for (const e of result.errors) {
      this.errors.set(e.valueId, e.message);
      touched.push(e.valueId);
    }
    this.state = result.state;
    this.lastMessage = result.message ?? (result.warnings.length ? result.warnings.join(" ") : null);
    this.emit(touched);
  }

  /** Drops cached values after the view was rebuilt from scratch. */
  reset(state: DocumentState): void {
    this.values.clear();
    this.errors.clear();
    this.state = state;
    this.emit(null);
  }

  subscribe(valueId: string | null, listener: Listener): () => void {
    if (valueId === null) {
      this.globalListeners.add(listener);
      return () => this.globalListeners.delete(listener);
    }
    let set = this.listeners.get(valueId);
    if (!set) this.listeners.set(valueId, (set = new Set()));
    set.add(listener);
    return () => set.delete(listener);
  }

  private revision = 0;
  get version(): number {
    return this.revision;
  }

  private emit(ids: string[] | null): void {
    this.revision++;
    if (ids === null) for (const set of this.listeners.values()) set.forEach((l) => l());
    else for (const id of ids) this.listeners.get(id)?.forEach((l) => l());
    this.globalListeners.forEach((l) => l());
  }
}

export interface DocumentContextValue {
  workspaceId: string;
  documentId: string;
  canEdit: boolean;
  store: DocumentStore;
  /** Bumped when the structure changed; lazy sections refetch on change. */
  structureVersion: number;
  /** Refetches the view (and bumps `structureVersion`), e.g. after a stale-id rejection. */
  refresh: () => Promise<void>;
  commit: (valueId: string, value: FieldValue) => Promise<boolean>;
  acceptResult: (result: EditResult) => Promise<void>;
  runAction: (actionId: string, parameters: Record<string, FieldValue>) => Promise<EditResult>;
}

export const DocumentContext = createContext<DocumentContextValue | null>(null);

export function useDocument(): DocumentContextValue {
  const ctx = useContext(DocumentContext);
  if (!ctx) throw new Error("useDocument must be used inside a DocumentContext provider");
  return ctx;
}

/** Live value of one field; re-renders only when that field changes. */
export function useFieldValue(valueId: string, initial: FieldValue) {
  const { store } = useDocument();
  store.seed(valueId, initial);
  const subscribe = useCallback((l: Listener) => store.subscribe(valueId, l), [store, valueId]);
  const value = useSyncExternalStore(subscribe, () => store.value(valueId, initial));
  const error = useSyncExternalStore(subscribe, () => store.error(valueId));
  const modified = useSyncExternalStore(subscribe, () => store.isModified(valueId));
  return { value, error, modified };
}

/** Document-wide state (undo/redo availability, dirty flag, last message). */
export function useDocumentState(store: DocumentStore) {
  const subscribe = useCallback((l: Listener) => store.subscribe(null, l), [store]);
  const version = useSyncExternalStore(subscribe, () => store.version);
  return { state: store.state, message: store.lastMessage, version };
}
