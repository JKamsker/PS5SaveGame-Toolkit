import type { FieldSpec, QueryableColumn, TableRow, ViewNode } from "../../../api/protocol";

/** Use published types where available. Raw text counts are numeric only when unambiguous. */
function isNumericSpec(spec: FieldSpec | undefined): boolean {
  if (spec?.kind === "nullable") return isNumericSpec(spec.inner);
  return spec?.kind === "integer" || spec?.kind === "float" || spec?.kind === "exactNumber";
}

export function isNumericCell(node: ViewNode | undefined): boolean {
  if (node?.type === "field") return isNumericSpec(node.spec);
  return node?.type === "text" && /^[-+]?\d+(?:\.\d+)?$/.test(node.text.trim());
}

export function numericColumns(columns: readonly QueryableColumn[], rows: readonly TableRow[]): ReadonlySet<string> {
  return new Set(columns.filter(column => {
    if (column.spec) return isNumericSpec(column.spec);
    const cells = rows.map(row => row.isEmpty ? undefined : row.cells[column.key]).filter(node => node !== undefined);
    return cells.length > 0 && cells.every(isNumericCell);
  }).map(column => column.key));
}

/** Reserve room for diagnostics without letting values or the mounted virtual slice size tracks. */
export function columnTrack(column: QueryableColumn, rows: readonly TableRow[] = []): string {
  const size = column.width?.toLowerCase() ?? "normal";
  const fraction = column.width === "Narrow" ? "0.6fr" : column.width === "Wide" ? "2fr" : "1fr";
  // Paged columns publish specs; eager tables can inspect the complete static row metadata.
  // A warning and a local error must fit together without squeezing the editor out of its cell.
  const diagnostics = column.spec !== undefined || rows.some(row => {
    const cell = row.cells[column.key];
    return cell?.type === "field" && Boolean(cell.warning);
  });
  const minimum = `var(--table-column-${size})`;
  return `minmax(${diagnostics ? `max(${minimum}, var(--table-column-field))` : minimum}, ${fraction})`;
}
