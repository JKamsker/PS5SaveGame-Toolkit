import { useEffect, useId } from "react";
import type { Confidence, QueryableTableNode, TableColumn, TableRow } from "../../../api/protocol";
import { ReviewPath, useNextReviewPath, useViewJump } from "../review/navigation";
import { RowDetails, rowHasDetails } from "./RowDetails";
import { rowLabel } from "./cellText";
import { TableCell } from "./TableCell";
import type { useQueryableSelection } from "./selection";
import { numericColumns } from "./presentation";
import { ColumnHeading } from "./ColumnHeading";

export function ServerTableHeader({ columns, rows, confidence, hasDetails, selected, selection }: { columns: readonly TableColumn[]; rows: readonly TableRow[]; confidence: ReadonlyMap<string, Confidence>; hasDetails: boolean; selected: number; selection: ReturnType<typeof useQueryableSelection> }) {
  const all = rows.length > 0 && selected === rows.length;
  const numbers = numericColumns(columns, rows);
  return (
    <div className="table__row" role="row" aria-rowindex={1}>
      <div className="table__head table__head--control" role="columnheader"><input type="checkbox" aria-label="Select all rows on this page" checked={all} disabled={rows.length === 0} onChange={() => selection.setMany(rows.map((row) => row.key), !all)} /></div>
      {hasDetails && <div className="table__head table__head--control" role="columnheader"><span className="visually-hidden">Details</span></div>}
      {columns.map((column) => <ColumnHeading key={column.key} column={column} numeric={numbers.has(column.key)} confidence={confidence.get(column.key)} />)}
    </div>
  );
}

interface ServerRowProps {
  row: TableRow;
  emptyRows?: QueryableTableNode["emptyRows"];
  columns: readonly TableColumn[];
  confidence: ReadonlyMap<string, Confidence>;
  showDetailsColumn: boolean;
  rowIndex: number;
  selected: boolean;
  expanded: boolean;
  onToggle: (key: string) => void;
  onExpand: (key: string) => void;
  onToggleDetails: (key: string) => void;
}

export function ServerRow({ row, emptyRows, columns, confidence, showDetailsColumn, rowIndex, selected, onToggle, expanded, onExpand, onToggleDetails }: ServerRowProps) {
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
          {columns.map((column) => <TableCell key={column.key} node={row.cells[column.key]} columnConfidence={confidence.get(column.key)} emptySlot={row.isEmpty && emptyRows?.labelColumnKey === column.key} rowLabel={label} onOpenDetails={() => onExpand(row.key)} />)}
        </ReviewPath>
      </div>
      {expanded && hasDetails && <div className="table__detail" role="row" aria-rowindex={rowIndex + 1}><div id={detailId} role="cell" aria-colspan={columns.length + 2} className="table__detail-cell"><ReviewPath part={`row:${row.key}`}><RowDetails row={row} columns={columns} /></ReviewPath></div></div>}
    </>
  );
}
