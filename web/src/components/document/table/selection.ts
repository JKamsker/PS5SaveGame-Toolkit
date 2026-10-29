import { createContext, useCallback, useContext, useMemo, useState } from "react";
import type { TableRow } from "../../../api/protocol";
import type { CollectionQuery } from "../../../api/protocol";

/**
 * Row selection of one table. Kept in a small hook + context so bulk features (F6 action preview)
 * can read the selection from anywhere inside the table without the table knowing about them.
 */
export interface TableSelection {
  /** Explicit selected keys, including unloaded pages; query-wide scopes expose loaded keys. */
  selectedKeys: ReadonlySet<string>;
  /** Selected rows in table order. */
  selectedRows: readonly TableRow[];
  count: number;
  /** Query scope used when the user selected the complete filtered collection. */
  queryScope: QuerySelectionScope | null;
  isSelected: (key: string) => boolean;
  toggle: (key: string) => void;
  /** Selects (or deselects) all given keys at once, e.g. every visible row. */
  setMany: (keys: readonly string[], selected: boolean) => void;
  /** Selects every row in the current filtered collection when supported by the table. */
  selectFiltered?: () => void;
  clear: () => void;
}

/** Explicit, serialisable selection scope for F6 bulk planning. */
interface QuerySelectionScope {
  collectionId: string;
  query: CollectionQuery;
  revision: number;
  totalCount: number;
  excludedRowKeys: readonly string[];
}

export const TableSelectionContext = createContext<TableSelection | null>(null);

/** Selection of the enclosing table, or null outside a table. */
export function useTableSelection(): TableSelection | null {
  return useContext(TableSelectionContext);
}

/** Owns the selection state for `rows`. Keys of rows that disappear (structure changes) are ignored. */
export function useRowSelection(rows: readonly TableRow[]): TableSelection {
  const [raw, setRaw] = useState<ReadonlySet<string>>(() => new Set());

  const selectedRows = useMemo(() => (raw.size ? rows.filter((r) => raw.has(r.key)) : []), [rows, raw]);
  const selectedKeys = useMemo(() => new Set(selectedRows.map((r) => r.key)), [selectedRows]);

  const toggle = useCallback((key: string) => {
    setRaw((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }, []);

  const setMany = useCallback((keys: readonly string[], selected: boolean) => {
    setRaw((prev) => {
      const next = new Set(prev);
      for (const key of keys) {
        if (selected) next.add(key);
        else next.delete(key);
      }
      return next;
    });
  }, []);

  const clear = useCallback(() => setRaw(new Set()), []);
  const isSelected = useCallback((key: string) => selectedKeys.has(key), [selectedKeys]);

  return useMemo(
    () => ({ selectedKeys, selectedRows, count: selectedRows.length, queryScope: null, isSelected, toggle, setMany, clear }),
    [selectedKeys, selectedRows, isSelected, toggle, setMany, clear],
  );
}

/** Selection for a paged collection. The scope represents all matching rows, including unloaded pages. */
export function useQueryableSelection(rows: readonly TableRow[], collectionId: string, query: CollectionQuery, revision: number, filteredCount: number): TableSelection {
  const [selected, setSelected] = useState<ReadonlySet<string>>(() => new Set());
  const [scope, setScope] = useState<QuerySelectionScope | null>(null);
  const selectedRows = useMemo(() => rows.filter((row) => isSelectedKey(row.key, selected, scope)), [rows, selected, scope]);
  const selectedKeys = useMemo(() => scope ? new Set(selectedRows.map((row) => row.key)) : selected, [selectedRows, selected, scope]);
  const isSelected = useCallback((key: string) => isSelectedKey(key, selected, scope), [selected, scope]);
  const toggle = useCallback((key: string) => {
    setSelected((previous) => {
      const next = new Set(previous);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }, []);
  const setMany = useCallback((keys: readonly string[], value: boolean) => {
    setSelected((previous) => {
      const next = new Set(previous);
      for (const key of keys) {
        // A query-wide selection stores exclusions; an explicit selection stores inclusions.
        if (scope ? !value : value) next.add(key);
        else next.delete(key);
      }
      return next;
    });
  }, [scope]);
  const selectFiltered = useCallback(() => {
    setSelected(new Set());
    setScope({ collectionId, query: { ...query, offset: 0 }, revision, totalCount: filteredCount, excludedRowKeys: [] });
  }, [collectionId, query, revision, filteredCount]);
  const clear = useCallback(() => {
    setSelected(new Set());
    setScope(null);
  }, []);
  const count = scope ? Math.max(0, scope.totalCount - selected.size) : selected.size;
  const queryScope = scope ? { ...scope, excludedRowKeys: [...selected].sort() } : null;
  return useMemo(
    () => ({ selectedKeys, selectedRows, count, queryScope, isSelected, toggle, setMany, clear, selectFiltered }),
    [selectedKeys, selectedRows, count, queryScope, isSelected, toggle, setMany, clear, selectFiltered],
  );
}

function isSelectedKey(key: string, selected: ReadonlySet<string>, scope: QuerySelectionScope | null): boolean {
  return scope ? !selected.has(key) : selected.has(key);
}
