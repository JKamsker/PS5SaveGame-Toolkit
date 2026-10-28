import type { TableNode } from "../../../api/protocol";
import type { TableSelection } from "./selection";
import { FILTER_THRESHOLD, type ColumnFacet, type FacetSelection } from "./useTableRows";

interface TableToolbarProps {
  node: TableNode;
  /** Number of rows passing the filters. */
  shown: number;
  selection: TableSelection;
  query: string;
  onQuery: (query: string) => void;
  facetOptions: readonly ColumnFacet[];
  facets: FacetSelection;
  onFacets: (facets: FacetSelection) => void;
}

/** Title, row/selection counts, free-text search and per-column category filters. */
export function TableToolbar({ node, shown, selection, query, onQuery, facetOptions, facets, onFacets }: TableToolbarProps) {
  const total = node.rows.length;
  const filtersActive = Object.values(facets).some((v) => v !== "");
  return (
    <div className="table__toolbar">
      {node.title && <h3 className="table__title">{node.title}</h3>}
      <span className="table__count" aria-live="polite">
        {shown === total ? `${total} rows` : `${shown} of ${total} rows`}
      </span>
      {selection.count > 0 && (
        <span className="table__selected">
          <span aria-live="polite">{selection.count} selected</span>
          <button type="button" className="btn btn--quiet btn--small" onClick={selection.clear}>
            Clear selection
          </button>
        </span>
      )}
      {total > FILTER_THRESHOLD && (
        <div className="table__filters">
          {facetOptions.map((facet) => (
            <label key={facet.key} className="table__facet">
              <span className="visually-hidden">Filter by {facet.label}</span>
              <select
                className="input input--compact"
                value={facets[facet.key] ?? ""}
                onChange={(e) => onFacets({ ...facets, [facet.key]: e.target.value })}
              >
                <option value="">All {facet.label}</option>
                {facet.values.map((v) => (
                  <option key={v} value={v}>
                    {v}
                  </option>
                ))}
              </select>
            </label>
          ))}
          {filtersActive && (
            <button type="button" className="btn btn--quiet btn--small" onClick={() => onFacets({})}>
              Reset filters
            </button>
          )}
          <input className="table__search" type="search" placeholder="Filter rows" aria-label="Filter rows" value={query} onChange={(e) => onQuery(e.target.value)} />
        </div>
      )}
    </div>
  );
}
