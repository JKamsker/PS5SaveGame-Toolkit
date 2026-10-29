import type { FieldSpec, QueryableColumn, TableColumn, TableRow, ViewNode } from "../../../api/protocol";

/** Use published types where available. Raw text counts are numeric only when unambiguous. */
export function isNumericSpec(spec: FieldSpec | undefined): boolean {
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

/** Share stable tracks between eager and paged tables; never size from a virtual page's contents. */
export function columnTrack(column: TableColumn): string {
  switch (column.width) {
    case "Narrow": return "minmax(var(--table-column-narrow), 0.6fr)";
    case "Wide": return "minmax(var(--table-column-wide), 2fr)";
    default: return "minmax(var(--table-column-normal), 1fr)";
  }
}
