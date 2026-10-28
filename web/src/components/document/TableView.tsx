import { memo, useCallback, useDeferredValue, useEffect, useId, useMemo, useRef, useState } from "react";
import { useVirtualizer } from "@tanstack/react-virtual";
import type { TableColumn, TableNode, TableRow } from "../../api/protocol";
import { ReviewPath, useNextReviewPath } from "./review/navigation";
import { rowLabel } from "./table/cellText";
import { RowDetails, rowHasDetails } from "./table/RowDetails";
import { TableSelectionContext, useRowSelection, type TableSelection } from "./table/selection";
import { TableCell } from "./table/TableCell";
import { TableToolbar } from "./table/TableToolbar";
import { useTableRows, type FacetSelection } from "./table/useTableRows";

/** Height of one data row; must match `.table__cell` height in view.css. Detail rows are measured. */
const ROW_HEIGHT = 41;
const DETAIL_ESTIMATE = 240;
const CONTROL_COLUMN = "36px";
/** Content-independent tracks, so columns do not jump as virtual rows mount and unmount. */
const widths = { Narrow: "minmax(96px, 0.6fr)", Normal: "minmax(160px, 1fr)", Wide: "minmax(280px, 2fr)" } as const;

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
  const { rows, facetOptions, valueOf } = useTableRows(node, deferredQuery, facets);
  const selection = useRowSelection(node.rows);
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
    setExpanded(previous => new Set(previous).add(row.key));
  }, [next, node.rows]);
  useEffect(() => {
    if (!next) return;
    const index = entries.findIndex(entry => entry.kind === "row" && next === `row:${entry.row.key}`);
    if (index >= 0) virtualizer.scrollToIndex(index, { align: "center" });
  }, [next, entries, virtualizer]);
  const template = [CONTROL_COLUMN, ...(hasDetails ? [CONTROL_COLUMN] : []), ...node.columns.map((c) => widths[c.width ?? "Normal"])].join(" ");
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
        />
        {node.description && <p className="section__description">{node.description}</p>}
        <div className="table__scroller" ref={scroller}>
          <div className="table__grid" role="table" aria-label={node.title} aria-rowcount={entries.length + 1} style={{ gridTemplateColumns: template }}>
            <HeaderRow columns={node.columns} hasDetails={hasDetails} visible={rows} visibleSelected={visibleSelected} selection={selection} />
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
                  rowIndex={item.index + 2}
                  label={rowLabel(entry.row, columnKeys, valueOf)}
                  columnKeys={columnKeys}
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
  hasDetails: boolean;
  visible: readonly TableRow[];
  visibleSelected: number;
  selection: TableSelection;
}

function HeaderRow({ columns, hasDetails, visible, visibleSelected, selection }: HeaderRowProps) {
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
        <div key={c.key} className="table__head" role="columnheader" title={c.description}>
          {c.label}
          {c.description && <span className="visually-hidden">: {c.description}</span>}
        </div>
      ))}
    </div>
  );
}

interface RowProps {
  row: TableRow;
  rowIndex: number;
  /** Accessible row name; computed by the parent so memoisation survives unrelated edits. */
  label: string;
  columnKeys: readonly string[];
  hasDetails: boolean;
  detailId: string;
  expanded: boolean;
  selected: boolean;
  onToggleSelected: (key: string) => void;
  onToggleExpanded: (key: string) => void;
}

const Row = memo(function Row({ row, rowIndex, label, columnKeys, hasDetails, detailId, expanded, selected, onToggleSelected, onToggleExpanded }: RowProps) {
  const openDetails = () => {
    if (!expanded) onToggleExpanded(row.key);
  };
  return (
    <div className={`table__row${selected ? " table__row--selected" : ""}`} role="row" aria-rowindex={rowIndex}>
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
          <TableCell key={key} node={row.cells[key]} rowLabel={label} onOpenDetails={openDetails} />
        ))}
      </ReviewPath>
    </div>
  );
});
