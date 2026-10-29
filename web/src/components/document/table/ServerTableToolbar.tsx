import type { QueryableTableNode } from "../../../api/protocol";
import { EmptyRowsToggle } from "./EmptyRowsToggle";
import type { ServerTableModel } from "./ServerTableView";

interface ServerTableToolbarProps { node: QueryableTableNode; table: ServerTableModel }

export function ServerTableToolbar({ node, table }: ServerTableToolbarProps) {
  const { page, result, includeEmpty, updateQuery, selection, rows } = table;
  return (
    <div className="table__toolbar">
      <h3 className="table__title">{node.title}</h3>
      <span className="table__count" aria-live="polite">
        {page ? `${page.filteredCount.toLocaleString()} of ${page.totalCount.toLocaleString()} rows` : result.isError ? "Rows unavailable" : "Loading rows…"}
      </span>
      <EmptyRowsToggle presentation={node.emptyRows} includeEmpty={includeEmpty} hiddenCount={page?.hiddenEmptyCount ?? 0} onChange={value => {
        updateQuery({ includeEmpty: value });
      }} />
      {selection.count > 0 && (
        <span className="table__selected" aria-live="polite">
          {selection.count.toLocaleString()} selected
          <button type="button" className="btn btn--quiet btn--small" onClick={selection.clear}>Clear selection</button>
        </span>
      )}
      <ServerTableFilters node={node} table={table} />
      {selection.queryScope === null && page && page.filteredCount > rows.length && (
        <button type="button" className="btn btn--small" onClick={() => selection.selectFiltered?.()}>
          Select all {page.filteredCount.toLocaleString()} filtered rows
        </button>
      )}
      {selection.queryScope && <span className="table__scope">All filtered rows selected; edits apply to this query.</span>}
    </div>
  );
}

function ServerTableFilters({ node, table }: ServerTableToolbarProps) {
  const { columns, text, sort, descending, filters, updateQuery, updateFilter } = table;
  return (
    <div className="table__filters table__filters--paged">
      <input className="table__search" type="search" placeholder="Search all rows" aria-label={`Search ${node.title}`} value={text} onChange={(e) => updateQuery({ q: e.target.value || undefined })} />
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
