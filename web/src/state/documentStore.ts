import { createContext, useCallback, useContext, useSyncExternalStore } from "react";
import type { DocumentState, EditResult, FieldValue, ValueChange } from "../api/protocol";
import { sameValue } from "../lib/values";

type Listener = () => void;

/** Identity of one installed diagnostic, replaced even when a newer message has identical text. */
export interface FieldDiagnosticReceipt { readonly message: string }

export interface FieldCommitOutcome {
  accepted: boolean;
  /** Only a diagnostic installed by this submission's result, never an existing field error. */
  rejection?: FieldDiagnosticReceipt;
}

/**
 * Client-side cache of live values for one open document. View nodes carry the value at the time
 * they were fetched; edits, undo and actions report changes which override those snapshots here,
 * so tables, lazy sections and the main view stay consistent without refetching.
 */
export class DocumentStore {
  private values = new Map<string, FieldValue>();
  private original = new Map<string, FieldValue>();
  private errors = new Map<string, FieldDiagnosticReceipt>();
  private listeners = new Map<string, Set<Listener>>();
  private valueListeners = new Map<string, Set<Listener>>();
  private globalListeners = new Set<Listener>();
  /** Changes only when cached values change, independently of errors and history notifications. */
  valueVersion = 0;
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
    return this.values.has(valueId) && this.original.has(valueId) && !sameValue(this.values.get(valueId)!, this.original.get(valueId)!);
  }

  error(valueId: string): string | undefined {
    return this.errors.get(valueId)?.message;
  }

  diagnostic(valueId: string): FieldDiagnosticReceipt | undefined {
    return this.errors.get(valueId);
  }

  /** Clear only this exact installation; a replacement diagnostic belongs to a different result. */
  clearError(valueId: string, receipt: FieldDiagnosticReceipt): void {
    if (this.errors.get(valueId) === receipt) this.setError(valueId, undefined);
  }

  setError(valueId: string, message: string | undefined): void {
    if (message === undefined) this.errors.delete(valueId);
    else this.errors.set(valueId, { message });
    this.emit([valueId]);
  }

  apply(result: EditResult): void {
    this.applyWithDiagnostics(result);
  }

  /** Apply one result and return its installations, even if a subscriber immediately replaces one. */
  applyWithDiagnostics(result: EditResult): ReadonlyMap<string, FieldDiagnosticReceipt> {
    const diagnostics = new Map<string, FieldDiagnosticReceipt>();
    const touched = result.changes.map((c: ValueChange) => {
      this.seed(c.valueId, c.oldValue);
      this.values.set(c.valueId, c.newValue);
      this.errors.delete(c.valueId);
      return c.valueId;
    });
    for (const e of result.errors) {
      const receipt = { message: e.message };
      this.errors.set(e.valueId, receipt);
      diagnostics.set(e.valueId, receipt);
      touched.push(e.valueId);
    }
    this.state = result.state;
    this.lastMessage = result.message ?? (result.warnings.length ? result.warnings.join(" ") : null);
    if (result.changes.length) this.emitValues(result.changes.map(change => change.valueId));
    this.emit(touched);
    return diagnostics;
  }

  /** Drops cached values after the view was rebuilt from scratch. */
  reset(state: DocumentState): void {
    this.values.clear();
    this.errors.clear();
    this.state = state;
    this.emitValues(null);
    this.emit(null);
  }

  /** Observe all fields used by a table, including rows outside the virtual viewport. */
  subscribeValues(valueIds: readonly string[], listener: Listener): () => void {
    const sets = valueIds.map(id => {
      let listeners = this.valueListeners.get(id);
      if (!listeners) this.valueListeners.set(id, (listeners = new Set()));
      listeners.add(listener);
      return { id, listeners };
    });
    return () => {
      for (const { id, listeners } of sets) {
        listeners.delete(listener);
        if (listeners.size === 0) this.valueListeners.delete(id);
      }
    };
  }

  private emitValues(ids: readonly string[] | null): void {
    this.valueVersion++;
    const listeners = new Set<Listener>();
    const sets = ids === null ? this.valueListeners.values() : ids.map(id => this.valueListeners.get(id));
    for (const set of sets) set?.forEach(listener => listeners.add(listener));
    // A bulk edit can touch many fields in one table; notify that table once.
    listeners.forEach(listener => listener());
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

export interface DocumentMutationOptions {
  /** A dialog can own mutation failures instead of announcing them again in a background toast. */
  failureFeedback?: "toast" | "dialog";
}

/** Client-only outcome; a failed read must never make cached values appear freshly acknowledged. */
export interface DocumentMutationResult extends EditResult {
  snapshotError?: string;
}

export interface DocumentContextValue {
  workspaceId: string;
  documentId: string;
  canEdit: boolean;
  store: DocumentStore;
  /** Bumped when the structure changed; lazy sections refetch on change. */
  structureVersion: number;
  /** A failed snapshot read may leave the previous document cached for inspection. */
  snapshotError?: string;
  /** Cached values cannot be used for a new plan until this explicit read installs a snapshot. */
  snapshotRefresh?: "pending" | "required";
  /** Scoped modal ownership affects announcements only; closing returns feedback to the document. */
  claimSnapshotErrors?: () => () => void;
  snapshotErrorInDialog?: boolean;
  /** Preset callers share the same installed-snapshot requirement as action dialogs. */
  refreshState?: "ready" | "pending" | "failed";
  refreshError?: string;
  /** Refetches the view; dialogs can require success while ordinary refresh keeps its existing quiet failure behavior. */
  refresh: (options?: { throwOnError?: boolean }) => Promise<void>;
  commit: (valueId: string, value: FieldValue) => Promise<boolean>;
  /** Same serialized/deduplicated commit, with proof of any diagnostic that its result installed. */
  commitWithDiagnostics?: (valueId: string, value: FieldValue) => Promise<FieldCommitOutcome>;
  acceptResult: (result: EditResult) => Promise<void>;
  /** Serialize a mutation with other writes/history and accept its result before the next starts. */
  mutate: (call: () => Promise<EditResult>, options?: DocumentMutationOptions) => Promise<DocumentMutationResult>;
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
