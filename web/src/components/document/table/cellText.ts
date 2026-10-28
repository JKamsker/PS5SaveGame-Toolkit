import type { FieldNode, FieldSpec, FieldValue, TableRow, ViewNode } from "../../../api/protocol";
import { displayValue } from "../../../lib/values";

/** Resolves the live value of a field (store override or the snapshot from the view). */
export type ValueOf = (node: FieldNode) => FieldValue;

/** The snapshot value; used where no document store is available. */
export const snapshotValue: ValueOf = (node) => node.value;

/** Labels of the set bits of a flags value, e.g. ["Poisoned", "Burning"]. */
export function setFlagLabels(spec: Extract<FieldSpec, { kind: "flags" }>, value: FieldValue): string[] {
  if (value === null) return [];
  let bits: bigint;
  try {
    bits = BigInt(String(value));
  } catch {
    return [];
  }
  return spec.bits.filter((b) => (bits & (1n << BigInt(b.bit))) !== 0n).map((b) => b.label);
}

/** Short human text for a field value: enum/bool labels, durations, set flag names. */
export function fieldSummary(spec: FieldSpec, value: FieldValue): string {
  if (spec.kind === "flags") {
    const labels = setFlagLabels(spec, value);
    return value === null ? "—" : labels.length ? labels.join(", ") : "None";
  }
  return displayValue(spec, value);
}

/** One-line text for stats nodes, e.g. "Level 12 · HP 340". */
export function statsSummary(items: { label: string; value: string }[]): string {
  return items.map((s) => `${s.label} ${s.value}`).join(" · ");
}

/**
 * Everything a user might type to find a cell: the shown text plus the raw value, so both
 * "Fire Sword" and its id "1204" match. Uses live values so filters follow edits.
 */
export function cellSearchText(node: ViewNode | undefined, valueOf: ValueOf): string {
  if (!node) return "";
  switch (node.type) {
    case "text":
      return node.text;
    case "field": {
      const value = valueOf(node);
      return `${node.label} ${fieldSummary(node.spec, value)} ${String(value ?? "")}`;
    }
    case "image":
      return node.alt;
    case "action":
      return node.label;
    case "stats":
      return statsSummary(node.items);
    case "section":
      return `${node.title} ${node.badge ?? ""}`;
    case "table":
    case "hex":
    case "map":
    case "graph":
      return node.title ?? "";
    default:
      return "";
  }
}

/** Lower-cased search text of a whole row. */
export function rowSearchText(row: TableRow, valueOf: ValueOf): string {
  return Object.values(row.cells)
    .map((c) => cellSearchText(c, valueOf))
    .join("\n")
    .toLowerCase();
}

/**
 * Value used for column (category) filters, or null when the cell is not categorical.
 * Only text cells and enum/bool fields qualify; numbers and free text would make useless facets.
 */
export function facetValue(node: ViewNode | undefined, valueOf: ValueOf): string | null {
  if (!node) return null;
  if (node.type === "text") return node.text;
  if (node.type === "field" && (node.spec.kind === "enum" || node.spec.kind === "bool")) return displayValue(node.spec, valueOf(node));
  return null;
}

/** Short name of a row for accessible labels: the first text cell, else the first non-empty cell, else the row key. */
export function rowLabel(row: TableRow, columnKeys: readonly string[], valueOf: ValueOf): string {
  const cells = columnKeys.map((key) => row.cells[key]);
  const text = cells.find((c) => c?.type === "text" && c.text.trim());
  if (text?.type === "text") return text.text.trim();
  for (const cell of cells) {
    const shown = cell?.type === "field" ? fieldSummary(cell.spec, valueOf(cell)) : cellSearchText(cell, valueOf);
    if (shown.trim()) return shown.trim();
  }
  return row.key;
}
