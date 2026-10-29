import type { Confidence, TableColumn, TableRow } from "../../../api/protocol";

/** Use the filtered rows (or current server page), never only the mounted virtual slice.
 * Missing confidence or non-field cells cannot establish a column-wide claim.
 */
export function uniformConfidence(columns: readonly TableColumn[], rows: readonly TableRow[]): ReadonlyMap<string, Confidence> {
  const result = new Map<string, Confidence>();
  for (const column of columns) {
    const first = rows[0]?.cells[column.key];
    if (first?.type !== "field" || first.confidence === undefined) continue;
    const confidence = first.confidence;
    if (rows.every(row => {
      const cell = row.cells[column.key];
      return cell?.type === "field" && cell.confidence === confidence;
    })) result.set(column.key, confidence);
  }
  return result;
}
