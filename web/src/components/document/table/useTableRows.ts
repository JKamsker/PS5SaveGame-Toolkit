import { useCallback, useMemo } from "react";
import type { FieldNode, TableNode, TableRow } from "../../../api/protocol";
import { useDocument, useDocumentState } from "../../../state/documentStore";
import { facetValue, rowSearchText, type ValueOf } from "./cellText";

/** Tables with more rows than this get a search box and column filters. */
export const FILTER_THRESHOLD = 8;
const MAX_FACET_VALUES = 12;

export interface ColumnFacet {
  key: string;
  label: string;
  values: string[];
}

/** Column key -> selected facet value. Missing or empty means "any". */
export type FacetSelection = Readonly<Record<string, string>>;

/** Live value resolver bound to the document store; changes identity whenever any value changes. */
export function useLiveValues(): ValueOf {
  const { store } = useDocument();
  const { version } = useDocumentState(store);
  // `version` is not read inside; it is a dependency so memoised filters recompute after every edit.
  return useCallback((node: FieldNode) => store.value(node.valueId, node.value), [store, version]);
}

/**
 * Columns whose live values form a small set of categories: 2..12 distinct values, and values must
 * repeat (at most half as many as rows) so unique names do not become a "filter".
 */
export function computeFacets(node: TableNode, valueOf: ValueOf): ColumnFacet[] {
  if (node.rows.length <= FILTER_THRESHOLD) return [];
  const facets: ColumnFacet[] = [];
  for (const column of node.columns) {
    const values = new Set<string>();
    let categorical = true;
    for (const row of node.rows) {
      const cell = row.cells[column.key];
      if (!cell) continue;
      const value = facetValue(cell, valueOf);
      if (value !== null) values.add(value);
      if (value === null || values.size > MAX_FACET_VALUES) {
        categorical = false;
        break;
      }
    }
    if (categorical && values.size >= 2 && values.size * 2 <= node.rows.length) {
      facets.push({ key: column.key, label: column.label, values: [...values].sort((a, b) => a.localeCompare(b)) });
    }
  }
  return facets;
}

/** Rows matching the free-text query and every active facet, evaluated on live values. */
export function filterRows(rows: readonly TableRow[], query: string, facets: FacetSelection, valueOf: ValueOf): readonly TableRow[] {
  const needle = query.trim().toLowerCase();
  const active = Object.entries(facets).filter(([, v]) => v !== "");
  if (!needle && active.length === 0) return rows;
  return rows.filter(
    (row) =>
      active.every(([key, wanted]) => facetValue(row.cells[key], valueOf) === wanted) &&
      (!needle || rowSearchText(row, valueOf).includes(needle)),
  );
}

/** Filtered rows plus available column facets for a table, following edits immediately. */
export function useTableRows(node: TableNode, query: string, facets: FacetSelection) {
  const valueOf = useLiveValues();
  const facetOptions = useMemo(() => computeFacets(node, valueOf), [node, valueOf]);
  // A facet can vanish after edits (e.g. too many distinct values); never filter by an invisible control.
  const activeFacets = useMemo(
    () => Object.fromEntries(Object.entries(facets).filter(([key]) => facetOptions.some((f) => f.key === key))),
    [facets, facetOptions],
  );
  const rows = useMemo(() => filterRows(node.rows, query, activeFacets, valueOf), [node.rows, query, activeFacets, valueOf]);
  return { rows, facetOptions, valueOf };
}
