import { useCallback, useDeferredValue, useEffect, useId, useMemo, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, ApiError, isStaleError } from "../../../api/client";
import type { CollectionQuery, QueryablePage, QueryableTableNode, TableColumn, TableRow } from "../../../api/protocol";
import { useDocument } from "../../../state/documentStore";
import { useDocumentContentVersion } from "../../../state/useDocumentContentVersion";
import { RowDetails, rowHasDetails } from "./RowDetails";
import { rowLabel } from "./cellText";
import { TableCell } from "./TableCell";
import { EmptyRowsToggle } from "./EmptyRowsToggle";
import { TableSelectionContext, useQueryableSelection } from "./selection";
import { ReviewPath, useNextReviewPath, useViewJump } from "../review/navigation";

const PAGE_SIZE = 50;

/** Paged table for an SDK IQueryableDocument. Eager TableNode rendering remains in TableView. */
export function ServerTableView({ node }: { node: QueryableTableNode }) {
  const { workspaceId, documentId, structureVersion, refresh, store } = useDocument();
  const content = useDocumentContentVersion(store);
  const version = content.version;
  const revision = content.revision ?? 0;
  const [text, setText] = useState("");
  const [filters, setFilters] = useState<Record<string, string>>({});
  const [sort, setSort] = useState("");
  const [descending, setDescending] = useState(false);
  const [offset, setOffset] = useState(0);
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(() => new Set());
  const expandRow = useCallback((key: string) => setExpanded(previous => previous.has(key) ? previous : new Set([...previous, key])), []);
  const toggleDetails = useCallback((key: string) => setExpanded(previous => {
    const next = new Set(previous);
    if (next.has(key)) next.delete(key);
    else next.add(key);
    return next;
  }), []);
  const [includeEmpty, setIncludeEmpty] = useState(!(node.emptyRows?.hideByDefault ?? true));
  const deferredText = useDeferredValue(text);
  const query = useMemo<CollectionQuery>(
    () => ({ q: deferredText || undefined, filters, sort: sort || undefined, descending, offset, limit: PAGE_SIZE,
      includeEmpty: node.emptyRows ? includeEmpty : undefined }),
    [deferredText, filters, sort, descending, offset, includeEmpty, node.emptyRows],
  );
  const result = useQuery({
    queryKey: ["collection", workspaceId, documentId, node.collectionId, query, revision, version, structureVersion],
    queryFn: () => api.queryCollection(workspaceId, documentId, node.collectionId, query, revision),
    retry: (count, error) => !(error instanceof ApiError && error.status >= 400 && error.status < 500) && count < 2,
  });
  const stale = result.isError && isStaleError(result.error);
  const recoveryAttempted = useRef(false);
  useEffect(() => {
    if (result.isSuccess) recoveryAttempted.current = false;
    if (!stale || recoveryAttempted.current) return;
    recoveryAttempted.current = true;
    // Keep the failure visible and allow manual retry if refreshing the document also fails.
    void refresh().catch(() => {});
  }, [stale, result.isSuccess, refresh]);

  // Keep one loaded page through read failures, including a same-revision view refresh.
  // Different queries or content revisions must still hide the old interactive rows.
  const pageKey = JSON.stringify([node.collectionId, query, revision, version]);
  const loaded = useRef<{ key: string; generation: number; page: QueryablePage } | undefined>(undefined);
  if (result.data) loaded.current = { key: pageKey, generation: structureVersion, page: result.data };
  const page = text !== deferredText ? undefined : result.data ?? (loaded.current?.key === pageKey ? loaded.current.page : undefined);
  const retired = stale || loaded.current?.generation !== structureVersion;
  const columns = useMemo<TableColumn[]>(
    () => (page?.columns ?? node.columns).map((column) => ({ key: column.key, label: column.label, description: column.description, width: column.width })),
    [node.columns, page?.columns],
  );
  const rows = page?.rows ?? [];
  const hasDetails = rows.some(rowHasDetails);
  const detailCount = rows.filter(row => expanded.has(row.key) && rowHasDetails(row)).length;
  let nextRowIndex = (page?.offset ?? 0) + 2;
  const indexedRows = rows.map(row => {
    const rowIndex = nextRowIndex;
    nextRowIndex += expanded.has(row.key) && rowHasDetails(row) ? 2 : 1;
    return { row, rowIndex };
  });
  const selection = useQueryableSelection(rows, node.collectionId, query, page?.revision ?? revision, page?.filteredCount ?? 0);
  const { clear } = selection;
  useEffect(() => { clear(); }, [revision, node.collectionId, clear]);
  const visibleSelected = rows.reduce((count, row) => count + (selection.isSelected(row.key) ? 1 : 0), 0);
  const canNext = page ? Boolean(page.nextCursor) || page.offset + rows.length < page.filteredCount : false;
  const canPrevious = offset > 0;
  const updateQuery = (change: Partial<{ text: string; filters: Record<string, string>; sort: string; descending: boolean }>) => {
    if (change.text !== undefined) setText(change.text);
    if (change.filters !== undefined) setFilters(change.filters);
    if (change.sort !== undefined) setSort(change.sort);
    if (change.descending !== undefined) setDescending(change.descending);
    setOffset(0);
    selection.clear();
  };

  return (
    <TableSelectionContext.Provider value={selection}>
      <section className="table">
        <div className="table__toolbar">
          <h3 className="table__title">{node.title}</h3>
          <span className="table__count" aria-live="polite">
            {page ? `${page.filteredCount.toLocaleString()} of ${page.totalCount.toLocaleString()} rows` : result.isError ? "Rows unavailable" : "Loading rows…"}
          </span>
          <EmptyRowsToggle presentation={node.emptyRows} includeEmpty={includeEmpty} hiddenCount={page?.hiddenEmptyCount ?? 0} onChange={value => {
            setIncludeEmpty(value);
            setOffset(0);
            selection.clear();
          }} />
          {selection.count > 0 && (
            <span className="table__selected" aria-live="polite">
              {selection.count.toLocaleString()} selected
              <button type="button" className="btn btn--quiet btn--small" onClick={selection.clear}>Clear selection</button>
            </span>
          )}
          <div className="table__filters">
            <input className="table__search" type="search" placeholder="Search all rows" aria-label={`Search ${node.title}`} value={text} onChange={(e) => updateQuery({ text: e.target.value })} />
            <select className="input input--compact" aria-label="Sort rows" value={sort} onChange={(e) => updateQuery({ sort: e.target.value })}>
              <option value="">Default order</option>
              {columns.map((column) => <option key={column.key} value={column.key}>{column.label}</option>)}
            </select>
            {sort && <button type="button" className="btn btn--quiet btn--small" onClick={() => updateQuery({ descending: !descending })}>{descending ? "Descending" : "Ascending"}</button>}
            {columns.map((column) => (
              <label key={column.key} className="table__facet">
                <span className="visually-hidden">Filter {column.label}</span>
                <input className="input input--compact" placeholder={`Filter ${column.label}`} value={filters[column.key] ?? ""} onChange={(e) => updateQuery({ filters: { ...filters, [column.key]: e.target.value } })} />
              </label>
            ))}
          </div>
          {selection.queryScope === null && page && page.filteredCount > rows.length && (
            <button type="button" className="btn btn--small" onClick={() => selection.selectFiltered?.()}>
              Select all {page.filteredCount.toLocaleString()} filtered rows
            </button>
          )}
          {selection.queryScope && <span className="table__scope">All filtered rows selected; edits apply to this query.</span>}
        </div>
        {node.description && <p className="section__description">{node.description}</p>}
        {result.isError && <div>
          <p role="alert" className="notice notice--Danger">{stale ? "This table is out of date. Refresh the document to load current rows." : result.error.message}</p>
          <button type="button" className="btn btn--small" onClick={() => {
            if (stale) void refresh().catch(() => {});
            else void result.refetch();
          }}>{stale ? "Refresh document" : "Retry rows"}</button>
        </div>}
        <div className="table__scroller" inert={retired}>
          <div className="table__grid" role="table" aria-label={node.title} aria-busy={result.isFetching} aria-rowcount={(page?.filteredCount ?? node.totalCount) + detailCount + 1} style={{ gridTemplateColumns: `36px ${hasDetails ? "36px " : ""}repeat(${columns.length}, minmax(160px, 1fr))` }}>
            <Header columns={columns} rows={rows} hasDetails={hasDetails} selected={visibleSelected} selection={selection} />
            {indexedRows.map(({ row, rowIndex }) => <ServerRow key={row.key} row={row} emptyRows={node.emptyRows} columns={columns} showDetailsColumn={hasDetails}
              rowIndex={rowIndex} selected={selection.isSelected(row.key)} onToggle={selection.toggle} expanded={expanded.has(row.key)} onExpand={expandRow} onToggleDetails={toggleDetails} />)}
          </div>
          {page && rows.length === 0 && <p className="table__empty">No rows match the current filters.</p>}
        </div>
        <nav className="table__pagination" aria-label={`${node.title} pages`}>
          <button type="button" className="btn btn--small" disabled={!canPrevious || result.isFetching} onClick={() => setOffset(Math.max(0, offset - PAGE_SIZE))}>Previous</button>
          <span>Page {Math.floor((page?.offset ?? offset) / PAGE_SIZE) + 1}</span>
          <button type="button" className="btn btn--small" disabled={!canNext || result.isFetching} onClick={() => setOffset(offset + PAGE_SIZE)}>Next</button>
        </nav>
      </section>
    </TableSelectionContext.Provider>
  );
}

function Header({ columns, rows, hasDetails, selected, selection }: { columns: readonly TableColumn[]; rows: readonly TableRow[]; hasDetails: boolean; selected: number; selection: ReturnType<typeof useQueryableSelection> }) {
  const all = rows.length > 0 && selected === rows.length;
  return (
    <div className="table__row" role="row" aria-rowindex={1}>
      <div className="table__head table__head--control" role="columnheader"><input type="checkbox" aria-label="Select all rows on this page" checked={all} disabled={rows.length === 0} onChange={() => selection.setMany(rows.map((row) => row.key), !all)} /></div>
      {hasDetails && <div className="table__head table__head--control" role="columnheader"><span className="visually-hidden">Details</span></div>}
      {columns.map((column) => <div key={column.key} className="table__head" role="columnheader">{column.label}</div>)}
    </div>
  );
}

interface ServerRowProps {
  row: TableRow;
  emptyRows?: QueryableTableNode["emptyRows"];
  columns: readonly TableColumn[];
  showDetailsColumn: boolean;
  rowIndex: number;
  selected: boolean;
  expanded: boolean;
  onToggle: (key: string) => void;
  onExpand: (key: string) => void;
  onToggleDetails: (key: string) => void;
}

function ServerRow({ row, emptyRows, columns, showDetailsColumn, rowIndex, selected, onToggle, expanded, onExpand, onToggleDetails }: ServerRowProps) {
  const ref = useViewJump(row.key, "Row");
  const next = useNextReviewPath();
  const label = rowLabel(row, columns.map((column) => column.key), (node) => node.value);
  const detailId = useId();
  useEffect(() => { if (next === `row:${row.key}`) onExpand(row.key); }, [next, row.key, onExpand]);
  const hasDetails = rowHasDetails(row);
  return (
    <>
      <div ref={ref} tabIndex={-1} className={`table__row${selected ? " table__row--selected" : ""}${row.isEmpty ? " table__row--empty" : ""}`} role="row" aria-rowindex={rowIndex}>
        <div className="table__cell table__cell--control" role="cell"><input type="checkbox" aria-label={`Select ${label}`} checked={selected} onChange={() => onToggle(row.key)} /></div>
        {showDetailsColumn && <div className="table__cell table__cell--control" role="cell">
          {hasDetails && <button type="button" className="row-toggle" aria-label={`Details for ${label}`}
            aria-expanded={expanded} aria-controls={expanded ? detailId : undefined} onClick={() => onToggleDetails(row.key)} />}
        </div>}
        <ReviewPath part={`row:${row.key}`}>
          {columns.map((column) => <TableCell key={column.key} node={row.cells[column.key]} emptySlot={row.isEmpty && emptyRows?.labelColumnKey === column.key} rowLabel={label} onOpenDetails={() => onExpand(row.key)} />)}
        </ReviewPath>
      </div>
      {expanded && hasDetails && <div className="table__detail" role="row" aria-rowindex={rowIndex + 1}><div id={detailId} role="cell" aria-colspan={columns.length + 2} className="table__detail-cell"><ReviewPath part={`row:${row.key}`}><RowDetails row={row} columns={columns} /></ReviewPath></div></div>}
    </>
  );
}
