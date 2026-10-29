import { memo, useCallback, useDeferredValue, useEffect, useId, useMemo, useRef, useState } from "react";
import { useVirtualizer } from "@tanstack/react-virtual";
import type { Confidence, TableColumn, TableNode, TableRow } from "../../api/protocol";
import { ReviewPath, useNextReviewPath, useViewJump } from "./review/navigation";
import { rowLabel } from "./table/cellText";
import { RowDetails, rowHasDetails } from "./table/RowDetails";
import { TableSelectionContext, useRowSelection, type TableSelection } from "./table/selection";
import { TableCell } from "./table/TableCell";
import { TableToolbar } from "./table/TableToolbar";
import { useTableRows, type FacetSelection } from "./table/useTableRows";
import { EmptyRowsToggle } from "./table/EmptyRowsToggle";
import { scrollTableByKey } from "./table/keyboardScroll";
import { columnTrack, numericColumns } from "./table/presentation";
import { uniformConfidence } from "./table/columnConfidence";
import { ColumnHeading } from "./table/ColumnHeading";
import { usePublishTableSelection } from "./table/selectionBindings";

/** Height of one data row; must match `.table__cell` height in table.css. Detail rows are measured. */
const ROW_HEIGHT = 41;
const DETAIL_ESTIMATE = 240;
const CONTROL_COLUMN = "36px";

type Entry = { kind: "row"; row: TableRow } | { kind: "detail"; row: TableRow };

/**
 * Searchable, virtualised table. Data rows have a fixed height; long or nested content opens in a
 * measured detail row. Filters run on live values, and the row selection is exposed through
 * {@link TableSelectionContext} for bulk features.
 */
export function TableView({ node }: { node: TableNode }) {
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
  const next = useNextReviewPath();
  useEffect(() => {
    const row = node.rows.find(candidate => next === `row:${candidate.key}`);
    if (!row) return;
    setQuery("");
    setFacets({});
    if (row.isEmpty) setIncludeEmpty(true);
    setExpanded(previous => new Set(previous).add(row.key));
  }, [next, node.rows]);
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
  const visibleSelected = useMemo(() => rows.reduce((n, r) => n + (selection.isSelected(r.key) ? 1 : 0), 0), [rows, selection]);

  return (
    <TableSelectionContext.Provider value={selection}>
      <section className="table">
        <TableToolbar
          node={node}
          shown={rows.length}
          selection={selection}
          visibleSelected={visibleSelected}
          query={query}
          onQuery={setQuery}
          facetOptions={facetOptions}
          facets={facets}
          onFacets={setFacets}
        >
          <EmptyRowsToggle presentation={node.emptyRows} includeEmpty={includeEmpty} hiddenCount={hiddenEmptyCount} onChange={setIncludeEmpty} />
        </TableToolbar>
        {node.description && <p className="section__description">{node.description}</p>}
        {/* eslint-disable-next-line jsx-a11y/no-noninteractive-element-interactions, jsx-a11y/no-noninteractive-tabindex -- The named scroll region handles keyboard paging of virtualized rows. */}
        <div className="table__scroller" ref={scroller} tabIndex={0} role="region" aria-label={`${node.title ?? "Table"} rows`}
          onKeyDown={event => scrollTableByKey(event, virtualizer.getTotalSize(), ROW_HEIGHT)}>
          <div className="table__grid" role="table" aria-label={node.title} aria-rowcount={entries.length + 1} style={{ gridTemplateColumns: template }}>
            <HeaderRow columns={node.columns} numbers={numbers} confidence={confidence} hasDetails={hasDetails} visible={rows} visibleSelected={visibleSelected} selection={selection} />
            {padTop > 0 && <div aria-hidden style={{ gridColumn: "1 / -1", height: padTop }} />}
            {items.map((item) => {
              const entry = entries[item.index];
              if (!entry) return null;
              // Row keys may contain spaces, which would break the aria-controls IDREF list.
              const detailId = `${tableId}-${encodeURIComponent(entry.row.key)}-details`;
              if (entry.kind === "detail") {
                return (
                  <div
                    key={item.key}
                    className="table__detail"
                    role="row"
                    aria-rowindex={item.index + 2}
                    ref={virtualizer.measureElement}
                    data-index={item.index}
                  >
                    <div role="cell" id={detailId} className="table__detail-cell">
                      <ReviewPath part={`row:${entry.row.key}`}><RowDetails row={entry.row} columns={node.columns} /></ReviewPath>
                    </div>
                  </div>
                );
              }
              return (
                <Row
                  key={item.key}
                  row={entry.row}
                  emptyRows={node.emptyRows}
                  rowIndex={item.index + 2}
                  label={rowLabel(entry.row, columnKeys, valueOf)}
                  columnKeys={columnKeys}
                  confidence={confidence}
                  hasDetails={hasDetails}
                  detailId={detailId}
                  expanded={expanded.has(entry.row.key)}
                  selected={selection.isSelected(entry.row.key)}
                  onToggleSelected={selection.toggle}
                  onToggleExpanded={toggleExpanded}
                />
              );
            })}
            {padBottom > 0 && <div aria-hidden style={{ gridColumn: "1 / -1", height: padBottom }} />}
          </div>
          {rows.length === 0 && <p className="table__empty">No rows match the current filters.</p>}
        </div>
      </section>
    </TableSelectionContext.Provider>
  );
}

interface HeaderRowProps {
  columns: readonly TableColumn[];
  numbers: ReadonlySet<string>;
  confidence: ReadonlyMap<string, Confidence>;
  hasDetails: boolean;
  visible: readonly TableRow[];
  visibleSelected: number;
  selection: TableSelection;
}

function HeaderRow({ columns, numbers, confidence, hasDetails, visible, visibleSelected, selection }: HeaderRowProps) {
  const box = useRef<HTMLInputElement>(null);
  const all = visible.length > 0 && visibleSelected === visible.length;
  const some = visibleSelected > 0 && !all;
  useEffect(() => {
    if (box.current) box.current.indeterminate = some;
  }, [some]);

  return (
    <div className="table__row" role="row" aria-rowindex={1}>
      <div className="table__head table__head--control" role="columnheader">
        <input
          ref={box}
          type="checkbox"
          aria-label="Select all shown rows"
          checked={all}
          disabled={visible.length === 0}
          onChange={() => selection.setMany(visible.map((r) => r.key), !all)}
        />
      </div>
      {hasDetails && (
        <div className="table__head table__head--control" role="columnheader">
          <span className="visually-hidden">Details</span>
        </div>
      )}
      {columns.map((c) => (
        <ColumnHeading key={c.key} column={c} numeric={numbers.has(c.key)} confidence={confidence.get(c.key)} />
      ))}
    </div>
  );
}

interface RowProps {
  row: TableRow;
  emptyRows?: TableNode["emptyRows"];
  rowIndex: number;
  /** Accessible row name; computed by the parent so memoisation survives unrelated edits. */
  label: string;
  columnKeys: readonly string[];
  confidence: ReadonlyMap<string, Confidence>;
  hasDetails: boolean;
  detailId: string;
  expanded: boolean;
  selected: boolean;
  onToggleSelected: (key: string) => void;
  onToggleExpanded: (key: string) => void;
}

const Row = memo(function Row({ row, emptyRows, rowIndex, label, columnKeys, confidence, hasDetails, detailId, expanded, selected, onToggleSelected, onToggleExpanded }: RowProps) {
  const ref = useViewJump(row.key, "Row");
  const openDetails = () => {
    if (!expanded) onToggleExpanded(row.key);
  };
  return (
    <div ref={ref} tabIndex={-1} className={`table__row${selected ? " table__row--selected" : ""}${row.isEmpty ? " table__row--empty" : ""}`} role="row" aria-rowindex={rowIndex}>
      <div className="table__cell table__cell--control" role="cell">
        <input type="checkbox" aria-label={`Select ${label}`} checked={selected} onChange={() => onToggleSelected(row.key)} />
      </div>
      {hasDetails && (
        <div className="table__cell table__cell--control" role="cell">
          {rowHasDetails(row) && (
            <button
              type="button"
              className="row-toggle"
              aria-expanded={expanded}
              aria-controls={expanded ? detailId : undefined}
              aria-label={`Details for ${label}`}
              onClick={() => onToggleExpanded(row.key)}
            />
          )}
        </div>
      )}
      <ReviewPath part={`row:${row.key}`}>
        {columnKeys.map((key) => (
          <TableCell key={key} node={row.cells[key]} columnConfidence={confidence.get(key)} emptySlot={row.isEmpty && emptyRows?.labelColumnKey === key} rowLabel={label} onOpenDetails={openDetails} />
        ))}
      </ReviewPath>
    </div>
  );
});
