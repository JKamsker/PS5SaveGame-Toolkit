import type { QueryableTableNode } from "../../../api/protocol";
import { columnTrack } from "./presentation";
import { ServerRow, ServerTableHeader } from "./ServerTableRows";
import type { ServerTableModel } from "./ServerTableView";
import { ROW_HEIGHT, scrollTableByKey } from "./keyboardScroll";

interface ServerTablePartProps { node: QueryableTableNode; table: ServerTableModel }

/** Busy pagers and successful retries retire their active controls; the table title remains. */
function focusTableHeading(trigger: HTMLButtonElement) {
  if (document.activeElement === trigger) {
    trigger.closest(".table")?.querySelector<HTMLElement>(".table__title")?.focus();
  }
}

export function ServerTableReadError({ table }: Pick<ServerTablePartProps, "table">) {
  const { result, stale, refresh } = table;
  if (!result.isError) return null;
  return (
    <div className="read-error">
      <p role="alert" className="notice notice--Danger">{stale ? "This table is out of date. Refresh the document to load current rows." : result.error.message}</p>
      <button type="button" className="btn btn--small" onClick={event => {
        focusTableHeading(event.currentTarget);
        if (stale) void refresh().catch(() => {});
        else void result.refetch();
      }}>{stale ? "Refresh document" : "Retry rows"}</button>
    </div>
  );
}

export function ServerTableBody({ node, table }: ServerTablePartProps) {
  const { retired, result, page, detailCount, hasDetails, columns, rows, confidence, visibleSelected,
    selection, indexedRows, expanded, expandRow, toggleDetails } = table;
  return (
    // eslint-disable-next-line jsx-a11y/no-noninteractive-element-interactions, jsx-a11y/no-noninteractive-tabindex -- The named scroll region uses the eager table's keyboard policy; retired rows remain inert.
    <div className="table__scroller" inert={retired} tabIndex={0} role="region" aria-label={`${node.title ?? "Table"} rows`}
      onKeyDown={event => scrollTableByKey(event, event.currentTarget.scrollHeight, ROW_HEIGHT)}>
      <div className="table__grid" role="table" aria-label={node.title} aria-busy={result.isFetching} aria-rowcount={(page?.filteredCount ?? node.totalCount) + detailCount + 1} style={{ gridTemplateColumns: `36px ${hasDetails ? "36px " : ""}${columns.map(column => columnTrack(column)).join(" ")}` }}>
        <ServerTableHeader columns={columns} rows={rows} confidence={confidence} hasDetails={hasDetails} selected={visibleSelected} selection={selection} />
        {indexedRows.map(({ row, rowIndex }) => <ServerRow key={row.key} row={row} emptyRows={node.emptyRows} columns={columns} showDetailsColumn={hasDetails}
          confidence={confidence} rowIndex={rowIndex} selected={selection.isSelected(row.key)} onToggle={selection.toggle} expanded={expanded.has(row.key)} onExpand={expandRow} onToggleDetails={toggleDetails} />)}
      </div>
      {!page && !result.isError && <p role="status" aria-label={`${node.title} loading`} className="table__empty">Loading rows…</p>}
      {page && rows.length === 0 && <p className="table__empty">No rows match the current filters.</p>}
    </div>
  );
}

export function ServerTablePagination({ node, table }: ServerTablePartProps) {
  const { canPrevious, result, updateQuery, page, offset, limit, canNext } = table;
  return (
    <nav className="table__pagination" aria-label={`${node.title} pages`}>
      <button type="button" className="btn btn--small" disabled={!canPrevious || result.isFetching} onClick={event => {
        focusTableHeading(event.currentTarget);
        updateQuery({ offset: Math.max(0, (page?.offset ?? offset) - limit), cursor: undefined }, false);
      }}>Previous</button>
      <span>Page {Math.floor((page?.offset ?? offset) / limit) + 1}</span>
      <button type="button" className="btn btn--small" disabled={!canNext || result.isFetching} onClick={event => {
        focusTableHeading(event.currentTarget);
        updateQuery({ offset: page?.nextCursor ? 0 : (page?.offset ?? offset) + limit, cursor: page?.nextCursor }, false);
      }}>Next</button>
    </nav>
  );
}
