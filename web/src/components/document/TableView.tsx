import { useDeferredValue, useMemo, useRef, useState } from "react";
import { useVirtualizer } from "@tanstack/react-virtual";
import type { FieldNode, TableNode, TableRow, ViewNode } from "../../api/protocol";
import { displayValue } from "../../lib/values";
import { useDocument } from "../../state/documentStore";
import { FieldControl } from "../fields/FieldControl";
import { useLiveField } from "../fields/FieldRow";

const ROW_HEIGHT = 41;
const widths = { Narrow: "minmax(90px, auto)", Normal: "minmax(140px, 1fr)", Wide: "minmax(240px, 2fr)" } as const;

function cellText(node: ViewNode | undefined): string {
  if (!node) return "";
  if (node.type === "text") return node.text;
  if (node.type === "field") return `${displayValue(node.spec, node.value)} ${String(node.value ?? "")}`;
  return "";
}

/** Searchable, virtualised table; editable cells reuse the field controls in compact form. */
export function TableView({ node }: { node: TableNode }) {
  const [query, setQuery] = useState("");
  const deferred = useDeferredValue(query.trim().toLowerCase());
  const rows = useMemo(
    () => (deferred ? node.rows.filter((r) => Object.values(r.cells).some((c) => cellText(c).toLowerCase().includes(deferred))) : node.rows),
    [node.rows, deferred],
  );
  const scroller = useRef<HTMLDivElement>(null);
  const virtualizer = useVirtualizer({ count: rows.length, getScrollElement: () => scroller.current, estimateSize: () => ROW_HEIGHT, overscan: 12 });
  const template = node.columns.map((c) => widths[c.width ?? "Normal"]).join(" ");
  const items = virtualizer.getVirtualItems();
  const padTop = items[0]?.start ?? 0;
  const padBottom = virtualizer.getTotalSize() - (items.at(-1)?.end ?? 0);

  return (
    <section className="table">
      <div className="table__toolbar">
        {node.title && <h3 className="table__title">{node.title}</h3>}
        <span className="table__count">
          {rows.length === node.rows.length ? `${rows.length} rows` : `${rows.length} of ${node.rows.length} rows`}
        </span>
        {node.rows.length > 8 && (
          <input className="table__search" type="search" placeholder="Filter rows" aria-label="Filter rows" value={query} onChange={(e) => setQuery(e.target.value)} />
        )}
      </div>
      {node.description && <p className="section__description">{node.description}</p>}
      <div className="table__scroller" ref={scroller}>
        <div className="table__grid" role="table" style={{ gridTemplateColumns: template }}>
          <div className="table__row" role="row">
            {node.columns.map((c) => (
              <div key={c.key} className="table__head" role="columnheader" title={c.description}>
                {c.label}
              </div>
            ))}
          </div>
          {padTop > 0 && <div style={{ gridColumn: "1 / -1", height: padTop }} />}
          {items.map((item) => {
            const row = rows[item.index];
            return row ? <Row key={row.key} row={row} columns={node.columns.map((c) => c.key)} /> : null;
          })}
          {padBottom > 0 && <div style={{ gridColumn: "1 / -1", height: padBottom }} />}
        </div>
      </div>
    </section>
  );
}

function Row({ row, columns }: { row: TableRow; columns: string[] }) {
  return (
    <div className="table__row" role="row">
      {columns.map((key) => {
        const cell = row.cells[key];
        return cell?.type === "field" ? (
          <FieldCell key={key} node={cell} />
        ) : (
          <div key={key} className={`table__cell${cell?.type === "text" && cell.monospace ? " mono" : ""}`} role="cell">
            {cell?.type === "text" ? cell.text : ""}
          </div>
        );
      })}
    </div>
  );
}

function FieldCell({ node }: { node: FieldNode }) {
  const { canEdit } = useDocument();
  const { value, error, modified, commit } = useLiveField(node);
  return (
    <div className={`table__cell${modified ? " table__cell--modified" : ""}`} role="cell" title={error ?? node.warning}>
      <FieldControl id={`cell-${node.valueId}`} spec={node.spec} value={value} disabled={!canEdit} compact onCommit={commit} />
    </div>
  );
}
