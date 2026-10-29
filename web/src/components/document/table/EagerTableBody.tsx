import type { TableNode } from "../../../api/protocol";
import { ReviewPath } from "../review/navigation";
import { RowDetails } from "./RowDetails";
import { rowLabel } from "./cellText";
import { HeaderRow, Row } from "./EagerTableRows";
import { scrollTableByKey } from "./keyboardScroll";
import { ROW_HEIGHT, type useEagerTable, type useEagerTableLayout } from "./useEagerTable";

interface EagerTableBodyProps {
  node: TableNode;
  table: ReturnType<typeof useEagerTable>;
  layout: ReturnType<typeof useEagerTableLayout>;
  visibleSelected: number;
}

export function EagerTableBody({ node, table, layout, visibleSelected }: EagerTableBodyProps) {
  const { rows, selection, entries, hasDetails } = table;
  const { scroller, virtualizer, template, numbers, confidence, items, padTop, padBottom } = layout;
  return (
    // eslint-disable-next-line jsx-a11y/no-noninteractive-element-interactions, jsx-a11y/no-noninteractive-tabindex -- The named scroll region handles keyboard paging of virtualized rows.
    <div className="table__scroller" ref={scroller} tabIndex={0} role="region" aria-label={`${node.title ?? "Table"} rows`}
      onKeyDown={event => scrollTableByKey(event, virtualizer.getTotalSize(), ROW_HEIGHT)}>
      <div className="table__grid" role="table" aria-label={node.title} aria-rowcount={entries.length + 1} style={{ gridTemplateColumns: template }}>
        <HeaderRow columns={node.columns} numbers={numbers} confidence={confidence} hasDetails={hasDetails} visible={rows} visibleSelected={visibleSelected} selection={selection} />
        {padTop > 0 && <div aria-hidden style={{ gridColumn: "1 / -1", height: padTop }} />}
        {items.map(item => <VirtualTableEntry key={item.key} node={node} table={table} layout={layout} index={item.index} />)}
        {padBottom > 0 && <div aria-hidden style={{ gridColumn: "1 / -1", height: padBottom }} />}
      </div>
      {rows.length === 0 && <p className="table__empty">No rows match the current filters.</p>}
    </div>
  );
}

function VirtualTableEntry({ node, table, layout, index }: Omit<EagerTableBodyProps, "visibleSelected"> & { index: number }) {
  const { entries, columnKeys, valueOf, expanded, selection, hasDetails, toggleExpanded } = table;
  const { tableId, virtualizer, confidence } = layout;
  const entry = entries[index];
  if (!entry) return null;
  // Row keys may contain spaces, which would break the aria-controls IDREF list.
  const detailId = `${tableId}-${encodeURIComponent(entry.row.key)}-details`;
  if (entry.kind === "detail") return (
    // eslint-disable-next-line react-hooks/refs -- TanStack exposes a stable measurement callback; this reads the callback, not the scroll ref's current value.
    <div className="table__detail" role="row" aria-rowindex={index + 2} ref={virtualizer.measureElement} data-index={index}>
      <div role="cell" id={detailId} className="table__detail-cell">
        <ReviewPath part={`row:${entry.row.key}`}><RowDetails row={entry.row} columns={node.columns} /></ReviewPath>
      </div>
    </div>
  );
  return <Row row={entry.row} emptyRows={node.emptyRows} rowIndex={index + 2}
    label={rowLabel(entry.row, columnKeys, valueOf)} columnKeys={columnKeys} confidence={confidence} hasDetails={hasDetails}
    detailId={detailId} expanded={expanded.has(entry.row.key)} selected={selection.isSelected(entry.row.key)}
    onToggleSelected={selection.toggle} onToggleExpanded={toggleExpanded} />;
}
