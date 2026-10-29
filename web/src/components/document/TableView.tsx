import { useMemo } from "react";
import type { TableNode } from "../../api/protocol";
import { TableSelectionContext } from "./table/selection";
import { TableToolbar } from "./table/TableToolbar";
import { EmptyRowsToggle } from "./table/EmptyRowsToggle";
import { EagerTableBody } from "./table/EagerTableBody";
import { useEagerTable, useEagerTableLayout } from "./table/useEagerTable";

/** Searchable, virtualised table with live-value filtering and stable-key row selection. */
export function TableView({ node }: { node: TableNode }) {
  const table = useEagerTable(node);
  const layout = useEagerTableLayout(node, table);
  const { rows, selection, query, setQuery, facets, setFacets, facetOptions, includeEmpty, setIncludeEmpty, hiddenEmptyCount } = table;
  const visibleSelected = useMemo(() => rows.reduce((n, r) => n + (selection.isSelected(r.key) ? 1 : 0), 0), [rows, selection]);
  return (
    <TableSelectionContext.Provider value={selection}>
      <section className="table">
        <TableToolbar node={node} shown={rows.length} selection={selection} visibleSelected={visibleSelected}
          query={query} onQuery={setQuery} facetOptions={facetOptions} facets={facets} onFacets={setFacets}>
          <EmptyRowsToggle presentation={node.emptyRows} includeEmpty={includeEmpty} hiddenCount={hiddenEmptyCount} onChange={setIncludeEmpty} />
        </TableToolbar>
        {node.description && <p className="section__description">{node.description}</p>}
        <EagerTableBody node={node} table={table} layout={layout} visibleSelected={visibleSelected} />
      </section>
    </TableSelectionContext.Provider>
  );
}
