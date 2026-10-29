import { useCallback, useEffect, useMemo, useSyncExternalStore } from "react";
import { useDocument, type DocumentStore } from "../../../state/documentStore";
import { useTableSelection, type TableSelection } from "./selection";

interface Binding { title: string; selection: TableSelection }

/** One registry per document view generation. Only mounted tables can supply a current selection. */
class SelectionBindings {
  private tables = new Map<string, Map<symbol, Binding>>();
  private listeners = new Set<() => void>();
  subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => { this.listeners.delete(listener); };
  };
  get(id: string): Binding | undefined {
    const tables = this.tables.get(id);
    return tables?.size === 1 ? tables.values().next().value : undefined;
  }
  register(id: string, owner: symbol, binding: Binding): () => void {
    let tables = this.tables.get(id);
    if (!tables) this.tables.set(id, tables = new Map());
    tables.set(owner, binding);
    this.emit();
    return () => {
      tables.delete(owner);
      if (!tables.size) this.tables.delete(id);
      this.emit();
    };
  }
  private emit() { for (const listener of this.listeners) listener(); }
}

const registries = new WeakMap<DocumentStore, { version: number; bindings: SelectionBindings }>();
function useBindings() {
  const { store, structureVersion } = useDocument();
  let registry = registries.get(store);
  if (!registry || registry.version !== structureVersion) {
    registry = { version: structureVersion, bindings: new SelectionBindings() };
    registries.set(store, registry);
  }
  return registry.bindings;
}

export function usePublishTableSelection(id: string | null | undefined, title: string, selection: TableSelection) {
  const bindings = useBindings();
  const owner = useMemo(() => Symbol("table"), []);
  useEffect(() => {
    if (id) return bindings.register(id, owner, { title, selection });
  }, [bindings, id, owner, title, selection]);
}

export function useActionSelection(id: string | null | undefined) {
  const enclosing = useTableSelection();
  const bindings = useBindings();
  const snapshot = useCallback(() => id ? bindings.get(id) : undefined, [bindings, id]);
  const binding = useSyncExternalStore(bindings.subscribe, snapshot, snapshot);
  return { selection: id == null ? enclosing : binding?.selection ?? null,
    title: binding?.title ?? id, unavailable: id != null && !binding };
}
