import { memo, useEffect, useRef } from "react";
import type { Confidence, TableColumn, TableNode, TableRow } from "../../../api/protocol";
import { ReviewPath, useViewJump } from "../review/navigation";
import { rowHasDetails } from "./RowDetails";
import type { TableSelection } from "./selection";
import { TableCell } from "./TableCell";
import { ColumnHeading } from "./ColumnHeading";

interface HeaderRowProps {
  columns: readonly TableColumn[];
  numbers: ReadonlySet<string>;
  confidence: ReadonlyMap<string, Confidence>;
  hasDetails: boolean;
  visible: readonly TableRow[];
  visibleSelected: number;
  selection: TableSelection;
}

export function HeaderRow({ columns, numbers, confidence, hasDetails, visible, visibleSelected, selection }: HeaderRowProps) {
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

export const Row = memo(function Row({ row, emptyRows, rowIndex, label, columnKeys, confidence, hasDetails, detailId, expanded, selected, onToggleSelected, onToggleExpanded }: RowProps) {
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
