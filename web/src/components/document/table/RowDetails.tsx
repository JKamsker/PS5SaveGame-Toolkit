import type { TableColumn, TableRow, ViewNode } from "../../../api/protocol";
import { ViewRenderer } from "../ViewRenderer";

/** Text longer than this is likely truncated in a cell, so the row offers its details. */
const LONG_TEXT = 60;

function cellHasDetails(cell: ViewNode): boolean {
  switch (cell.type) {
    case "field":
      return (
        Boolean(cell.description || cell.warning) ||
        (cell.confidence !== undefined && cell.confidence !== "Verified") ||
        cell.spec.kind === "flags" ||
        (cell.spec.kind === "text" && cell.spec.multiline === true)
      );
    case "text":
      return cell.text.length > LONG_TEXT;
    case "action":
      return Boolean(cell.description);
    case "image":
    case "stats":
    case "section":
    case "tabs":
    case "table":
    case "hex":
      return true;
    default:
      return false;
  }
}

/** Whether a row has anything the compact cells cannot show in full. */
export function rowHasDetails(row: TableRow): boolean {
  return Object.values(row.cells).some(cellHasDetails);
}

/**
 * Full rendering of one row: fields as complete field rows (description, warning, error, confidence,
 * full editor) and every other cell through the regular view renderer, labelled by its column.
 */
export function RowDetails({ row, columns }: { row: TableRow; columns: readonly TableColumn[] }) {
  return (
    <div className="row-details">
      {columns.map((column) => {
        const cell = row.cells[column.key];
        if (!cell) return null;
        if (cell.type === "field") return <ViewRenderer key={column.key} node={cell} />;
        return (
          <div key={column.key} className="row-details__item">
            <div className="row-details__label">{column.label}</div>
            <div className="row-details__body">
              <ViewRenderer node={cell} />
            </div>
          </div>
        );
      })}
    </div>
  );
}
