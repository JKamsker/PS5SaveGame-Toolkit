import { useRef } from "react";
import type { QueryableTableNode } from "../../../api/protocol";
import { EmptyRowsToggle } from "./EmptyRowsToggle";
import type { ServerTableModel } from "./ServerTableView";
import { clearTableSelection } from "./clearSelection";

interface ServerTableToolbarProps { node: QueryableTableNode; table: ServerTableModel }

export function ServerTableToolbar({ node, table }: ServerTableToolbarProps) {
  const { page, result, includeEmpty, updateQuery, selection, rows } = table;
  return (
    <div className="table__toolbar">
      <h3 tabIndex={-1} className="table__title">{node.title}</h3>
      <span className="table__count" aria-live="polite">
        {page ? `${page.filteredCount.toLocaleString()} of ${page.totalCount.toLocaleString()} rows` : result.isError ? "Rows unavailable" : "Loading rows…"}
      </span>
      <EmptyRowsToggle presentation={node.emptyRows} includeEmpty={includeEmpty} hiddenCount={page?.hiddenEmptyCount ?? 0} onChange={value => {
        updateQuery({ includeEmpty: value });
      }} />
      <span className="table__selected">
        <span role="status" aria-label={`${node.title} selection`} aria-atomic="true">
          <span className={selection.count === 0 ? "visually-hidden" : undefined}>{selection.count > 0 ? `${selection.count.toLocaleString()} selected` : "No rows selected"}</span>
          {selection.queryScope && <span className="visually-hidden">. All filtered rows selected; edits apply to this query.</span>}
        </span>
        {selection.count > 0 && (
          <button type="button" className="btn btn--quiet btn--small" onClick={event => clearTableSelection(event, selection.clear)}>Clear selection</button>
        )}
      </span>
      <ServerTableFilters node={node} table={table} />
      {selection.queryScope === null && page && page.filteredCount > rows.length && (
        <button type="button" className="btn btn--small" onClick={() => selection.selectFiltered?.()}>
          Select all {page.filteredCount.toLocaleString()} filtered rows
        </button>
      )}
      {selection.queryScope && <span className="table__scope" aria-hidden="true">All filtered rows selected; edits apply to this query.</span>}
    </div>
  );
}

function ServerTableFilters({ node, table }: ServerTableToolbarProps) {
  const { columns, text, sort, descending, filters, updateQuery, updateFilter } = table;
  const search = useRef<HTMLInputElement>(null);
  return (
    <div className="table__filters table__filters--paged">
      <input ref={search} className="table__search" type="search" placeholder="Search all rows" aria-label={`Search ${node.title}`} value={text} onChange={(e) => updateQuery({ q: e.target.value || undefined })} />
      {(text || Object.keys(filters).length > 0) && <button type="button" className="btn btn--quiet btn--small" onClick={() => {
        updateQuery({ q: undefined, filters: {} });
        search.current?.focus();
      }}>Clear search and filters</button>}
      <select className="input input--compact" aria-label="Sort rows" value={sort} onChange={(e) => updateQuery({ sort: e.target.value || undefined })}>
        <option value="">Default order</option>
        {columns.map((column) => <option key={column.key} value={column.key}>{column.label}</option>)}
      </select>
      {sort && <button type="button" className="btn btn--quiet btn--small" onClick={() => updateQuery({ descending: !descending })}>{descending ? "Descending" : "Ascending"}</button>}
      {columns.map((column) => (
        <label key={column.key} className="table__facet">
          <span className="visually-hidden">Filter {column.label}</span>
          <input className="input input--compact" title={`Filter ${column.label}`} placeholder={`Filter ${column.label}`} value={filters[column.key] ?? ""} onChange={(e) => updateFilter(column.key, e.target.value)} />
        </label>
      ))}
    </div>
  );
}
