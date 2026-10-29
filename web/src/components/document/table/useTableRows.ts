import { useMemo } from "react";
import type { TableNode, TableRow } from "../../../api/protocol";
import { facetValue, rowSearchText, type ValueOf } from "./cellText";
import { useTableValues } from "./useTableValues";

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
      // Facet labels are a human menu; their localized order never changes protocol row keys.
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
export function useTableRows(node: TableNode, query: string, facets: FacetSelection, includeEmpty = true) {
  const valueOf = useTableValues(node.rows);
  const eligibleRows = useMemo(() => node.emptyRows && !includeEmpty ? node.rows.filter(row => !row.isEmpty) : node.rows, [node, includeEmpty]);
  const facetOptions = useMemo(() => computeFacets({ ...node, rows: eligibleRows }, valueOf), [node, eligibleRows, valueOf]);
  // A facet or one of its values can vanish after edits; never filter by something the controls do not show.
  const activeFacets = useMemo(
    () => Object.fromEntries(Object.entries(facets).filter(([key, value]) => facetOptions.some((f) => f.key === key && f.values.includes(value)))),
    [facets, facetOptions],
  );
  const rows = useMemo(() => filterRows(eligibleRows, query, activeFacets, valueOf), [eligibleRows, query, activeFacets, valueOf]);
  const hiddenEmptyCount = useMemo(() => node.emptyRows && !includeEmpty
    ? filterRows(node.rows.filter(row => row.isEmpty), query, activeFacets, valueOf).length : 0,
  [node, includeEmpty, query, activeFacets, valueOf]);
  return { rows, eligibleRows, hiddenEmptyCount, facetOptions, valueOf };
}
