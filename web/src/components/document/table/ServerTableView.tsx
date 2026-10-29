import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, ApiError, isStaleError } from "../../../api/client";
import type { QueryablePage, QueryableTableNode, TableColumn } from "../../../api/protocol";
import { useDocument } from "../../../state/documentStore";
import { useDocumentContentVersion } from "../../../state/useDocumentContentVersion";
import { rowHasDetails } from "./RowDetails";
import { TableSelectionContext, useQueryableSelection } from "./selection";
import { uniformConfidence } from "./columnConfidence";
import { collectionQueryKey, DEFAULT_COLLECTION_PAGE_SIZE } from "./collectionQuery";
import { useCollectionQuery } from "./useCollectionQuery";
import { usePublishTableSelection } from "./selectionBindings";
import { ServerTableToolbar } from "./ServerTableToolbar";
import { ServerTableBody, ServerTablePagination, ServerTableReadError } from "./ServerTableBody";

export type ServerTableModel = ReturnType<typeof useServerTable>;

/** Paged table for an SDK IQueryableDocument. Eager TableNode rendering remains in TableView. */
export function ServerTableView({ node }: { node: QueryableTableNode }) {
  const table = useServerTable(node);
  return (
    <TableSelectionContext.Provider value={table.selection}>
      <section className="table">
        <ServerTableToolbar node={node} table={table} />
        {node.description && <p className="section__description">{node.description}</p>}
        <ServerTableReadError table={table} />
        <ServerTableBody node={node} table={table} />
        <ServerTablePagination node={node} table={table} />
      </section>
    </TableSelectionContext.Provider>
  );
}

function useServerPage(node: QueryableTableNode) {
  const { workspaceId, documentId, structureVersion, refresh, store } = useDocument();
  const content = useDocumentContentVersion(store);
  const version = content.version;
  const revision = content.revision ?? 0;
  const { query, update, text, pendingSearch, reviewing } = useCollectionQuery(node);
  const result = useQuery({
    queryKey: collectionQueryKey(workspaceId, documentId, node.collectionId, query, revision, version, structureVersion),
    // Jump has just fetched and checked this exact revision-bound page.
    staleTime: reviewing ? Infinity : 0,
    queryFn: () => api.queryCollection(workspaceId, documentId, node.collectionId, query, revision),
    retry: (count, error) => !(error instanceof ApiError && error.status >= 400 && error.status < 500) && count < 2,
  });
  const stale = result.isError && isStaleError(result.error);
  useStalePageRecovery(stale, result.isSuccess, refresh);

  // Keep one loaded page through read failures, including a same-revision view refresh.
  // Different queries or content revisions must still hide the old interactive rows.
  const pageKey = JSON.stringify([node.collectionId, query, revision, version]);
  const loaded = useRef<{ key: string; generation: number; page: QueryablePage } | undefined>(undefined);
  if (result.data) loaded.current = { key: pageKey, generation: structureVersion, page: result.data };
  const page = pendingSearch ? undefined : result.data ?? (loaded.current?.key === pageKey ? loaded.current.page : undefined);
  const retired = stale || loaded.current?.generation !== structureVersion;
  return { query, update, text, result, stale, refresh, page, retired, revision };
}

function useStalePageRecovery(stale: boolean, successful: boolean, refresh: () => Promise<void>) {
  const recoveryAttempted = useRef(false);
  useEffect(() => {
    if (successful) recoveryAttempted.current = false;
    if (!stale || recoveryAttempted.current) return;
    recoveryAttempted.current = true;
    // Keep the failure visible and allow manual retry if refreshing the document also fails.
    void refresh().catch(() => {});
  }, [stale, successful, refresh]);

}

function useExpandedRows() {
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(() => new Set());
  const expandRow = useCallback((key: string) => setExpanded(previous => previous.has(key) ? previous : new Set([...previous, key])), []);
  const toggleDetails = useCallback((key: string) => setExpanded(previous => {
    const next = new Set(previous);
    if (next.has(key)) next.delete(key);
    else next.add(key);
    return next;
  }), []);
  return { expanded, expandRow, toggleDetails };
}

function useServerTable(node: QueryableTableNode) {
  const loaded = useServerPage(node);
  const { query, update, page, revision } = loaded;
  const { filters = {}, sort = "", descending = false, offset = 0, includeEmpty = false } = query;
  const { expanded, expandRow, toggleDetails } = useExpandedRows();
  const columns = useMemo<TableColumn[]>(
    () => (page?.columns ?? node.columns).map((column) => ({ key: column.key, label: column.label, description: column.description, width: column.width })),
    [node.columns, page?.columns],
  );
  const rows = page?.rows ?? [];
  const limit = page?.limit ?? query.limit ?? DEFAULT_COLLECTION_PAGE_SIZE;
  const confidence = uniformConfidence(columns, rows);
  const hasDetails = rows.some(rowHasDetails);
  const detailCount = rows.filter(row => expanded.has(row.key) && rowHasDetails(row)).length;
  let nextRowIndex = (page?.offset ?? 0) + 2;
  const indexedRows = rows.map(row => {
    const rowIndex = nextRowIndex;
    nextRowIndex += expanded.has(row.key) && rowHasDetails(row) ? 2 : 1;
    return { row, rowIndex };
  });
  const selection = useQueryableSelection(rows, node.collectionId, query, page?.revision ?? revision, page?.filteredCount ?? 0);
  usePublishTableSelection(node.collectionId, node.title, selection);
  const { clear } = selection;
  useEffect(() => { clear(); }, [revision, node.collectionId, clear]);
  const visibleSelected = rows.reduce((count, row) => count + (selection.isSelected(row.key) ? 1 : 0), 0);
  const canNext = page ? Boolean(page.nextCursor) || page.offset + rows.length < page.filteredCount : false;
  const canPrevious = (page?.offset ?? offset) > 0;
  const updateQuery = (patch: Parameters<typeof update>[0], resetPage = true) => {
    update(patch, resetPage);
    if (resetPage) selection.clear();
  };
  const updateFilter = (key: string, value: string) => {
    const next = { ...filters };
    if (value === "") delete next[key];
    else next[key] = value;
    updateQuery({ filters: next });
  };

  return { ...loaded, columns, rows, limit, confidence, hasDetails, detailCount, indexedRows, selection,
    visibleSelected, canNext, canPrevious, updateQuery, updateFilter, filters, sort, descending, offset, includeEmpty,
    expanded, expandRow, toggleDetails };
}
