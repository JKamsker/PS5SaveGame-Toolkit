import { useCallback, useDeferredValue, useEffect, useId, useMemo, useRef, useState } from "react";
import { useVirtualizer } from "@tanstack/react-virtual";
import type { TableNode, TableRow } from "../../../api/protocol";
import { useNextReviewPath } from "../review/navigation";
import { rowHasDetails } from "./RowDetails";
import { useRowSelection } from "./selection";
import { usePublishTableSelection } from "./selectionBindings";
import { useTableRows, type FacetSelection } from "./useTableRows";
import { columnTrack, numericColumns } from "./presentation";
import { uniformConfidence } from "./columnConfidence";
import { ROW_HEIGHT } from "./keyboardScroll";

export { ROW_HEIGHT } from "./keyboardScroll";
const DETAIL_ESTIMATE = 240;
const CONTROL_COLUMN = "36px";
type Entry = { kind: "row"; row: TableRow } | { kind: "detail"; row: TableRow };

export function useEagerTable(node: TableNode) {
  const [query, setQuery] = useState("");
  const [facets, setFacets] = useState<FacetSelection>({});
  const deferredQuery = useDeferredValue(query);
  const [includeEmpty, setIncludeEmpty] = useState(!(node.emptyRows?.hideByDefault ?? true));
  const { rows, eligibleRows, hiddenEmptyCount, facetOptions, valueOf } = useTableRows(node, deferredQuery, facets, includeEmpty);
  const selection = useRowSelection(eligibleRows);
  usePublishTableSelection(node.selectionId, node.title ?? "Table", selection);
  const { setMany } = selection;
  useEffect(() => {
    if (node.emptyRows && !includeEmpty) setMany(node.rows.filter(row => row.isEmpty).map(row => row.key), false);
  }, [node, includeEmpty, setMany]);
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(() => new Set());

  const hasDetails = useMemo(() => node.rows.some(rowHasDetails), [node.rows]);
  const columnKeys = useMemo(() => node.columns.map((c) => c.key), [node.columns]);
  const entries = useMemo(() => {
    const out: Entry[] = [];
    for (const row of rows) {
      out.push({ kind: "row", row });
      if (expanded.has(row.key)) out.push({ kind: "detail", row });
    }
    return out;
  }, [rows, expanded]);

  const toggleExpanded = useCallback((key: string) => {
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }, []);

  const next = useNextReviewPath();
  useEffect(() => {
    const row = node.rows.find(candidate => next === `row:${candidate.key}`);
    if (!row) return;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- A review jump must reveal its row by clearing filters at the established effect timing.
    setQuery("");
    setFacets({});
    if (row.isEmpty) setIncludeEmpty(true);
    setExpanded(previous => new Set(previous).add(row.key));
  }, [next, node.rows]);
  return { query, setQuery, facets, setFacets, includeEmpty, setIncludeEmpty, rows, hiddenEmptyCount, facetOptions,
    valueOf, selection, expanded, hasDetails, columnKeys, entries, toggleExpanded, next };
}

export function useEagerTableLayout(node: TableNode, table: ReturnType<typeof useEagerTable>) {
  const { entries, next, hasDetails, rows } = table;
  const scroller = useRef<HTMLDivElement>(null);
  // Stable callbacks: new identities would make the virtualiser rebuild every measurement per render.
  const estimateSize = useCallback((i: number) => (entries[i]?.kind === "detail" ? DETAIL_ESTIMATE : ROW_HEIGHT), [entries]);
  const getItemKey = useCallback(
    (i: number) => {
      const e = entries[i];
      return e ? `${e.kind}:${e.row.key}` : i;
    },
    [entries],
  );
  const virtualizer = useVirtualizer({ count: entries.length, getScrollElement: () => scroller.current, estimateSize, getItemKey, overscan: 12 });
  useEffect(() => {
    if (!next) return;
    const index = entries.findIndex(entry => entry.kind === "row" && next === `row:${entry.row.key}`);
    if (index >= 0) virtualizer.scrollToIndex(index, { align: "center" });
  }, [next, entries, virtualizer]);
  const template = useMemo(() => [CONTROL_COLUMN, ...(hasDetails ? [CONTROL_COLUMN] : []),
    ...node.columns.map(column => columnTrack(column, node.rows))].join(" "), [node.columns, node.rows, hasDetails]);
  const numbers = useMemo(() => numericColumns(node.columns, node.rows), [node.columns, node.rows]);
  const confidence = useMemo(() => uniformConfidence(node.columns, rows), [node.columns, rows]);
  const items = virtualizer.getVirtualItems();
  const padTop = items[0]?.start ?? 0;
  const padBottom = virtualizer.getTotalSize() - (items.at(-1)?.end ?? 0);
  const tableId = useId();

  return { scroller, virtualizer, template, numbers, confidence, items, padTop, padBottom, tableId };
}
