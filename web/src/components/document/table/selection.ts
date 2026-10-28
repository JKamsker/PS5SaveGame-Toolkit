import { createContext, useCallback, useContext, useMemo, useState } from "react";
import type { TableRow } from "../../../api/protocol";

/**
 * Row selection of one table. Kept in a small hook + context so bulk features (F6 action preview)
 * can read the selection from anywhere inside the table without the table knowing about them.
 */
export interface TableSelection {
  /** Keys of selected rows that still exist in the table. */
  selectedKeys: ReadonlySet<string>;
  /** Selected rows in table order. */
  selectedRows: readonly TableRow[];
  count: number;
  isSelected: (key: string) => boolean;
  toggle: (key: string) => void;
  /** Selects (or deselects) all given keys at once, e.g. every visible row. */
  setMany: (keys: readonly string[], selected: boolean) => void;
  clear: () => void;
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
    () => ({ selectedKeys, selectedRows, count: selectedRows.length, isSelected, toggle, setMany, clear }),
    [selectedKeys, selectedRows, isSelected, toggle, setMany, clear],
  );
}
