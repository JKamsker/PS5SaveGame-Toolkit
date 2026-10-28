import { useId, useState, type ReactNode } from "react";
import type { FieldNode, ImageNode, ViewNode } from "../../../api/protocol";
import { api } from "../../../api/client";
import { useDocument } from "../../../state/documentStore";
import { FieldControl } from "../../fields/FieldControl";
import { useLiveField } from "../../fields/FieldRow";
import { ActionButton } from "../ActionButton";
import { CellBadges } from "./CellBadges";
import { fieldSummary, statsSummary } from "./cellText";

export interface TableCellProps {
  node: ViewNode | undefined;
  /** Short name of the row, used to give repeated controls distinct accessible names. */
  rowLabel: string;
  /** Opens the row's detail panel (for content that does not fit a fixed-height cell). */
  onOpenDetails: () => void;
}

/**
 * One fixed-height table cell. Every node type renders something: compact forms inline, large or
 * nested content as a button that opens the row details, unknown types as a visible placeholder.
 */
export function TableCell({ node, rowLabel, onOpenDetails }: TableCellProps) {
  if (!node) return <div className="table__cell" role="cell" />;
  switch (node.type) {
    case "field":
      return <FieldCell node={node} rowLabel={rowLabel} onOpenDetails={onOpenDetails} />;
    case "text":
      return (
        <Cell>
          <span className={`cell-text cell-text--${node.tone ?? "Normal"}${node.monospace ? " mono" : ""}`} title={node.text}>
            {node.text}
          </span>
        </Cell>
      );
    case "image":
      return (
        <Cell>
          <CellImage node={node} />
        </Cell>
      );
    case "action":
      return (
        <Cell>
          <ActionButton node={node} compact ariaLabel={`${node.label}: ${rowLabel}`} />
        </Cell>
      );
    case "stats": {
      const summary = statsSummary(node.items);
      return (
        <Cell>
          <span className="cell-text" title={summary}>
            {summary}
          </span>
        </Cell>
      );
    }
    case "section":
    case "tabs":
    case "table":
    case "hex":
      return (
        <Cell>
          <DetailsChip label={nestedLabel(node)} rowLabel={rowLabel} onOpen={onOpenDetails} />
        </Cell>
      );
    default:
      return (
        <Cell>
          <span className="cell-placeholder">Unsupported: {(node as { type?: string }).type ?? "unknown"}</span>
        </Cell>
      );
  }
}

function Cell({ children, className = "" }: { children: ReactNode; className?: string }) {
  return (
    <div className={`table__cell${className}`} role="cell">
      {children}
    </div>
  );
}

function nestedLabel(node: Extract<ViewNode, { type: "section" | "tabs" | "table" | "hex" }>): string {
  switch (node.type) {
    case "section":
      return node.badge ? `${node.title} (${node.badge})` : node.title;
    case "tabs":
      return `${node.tabs.length} tabs`;
    case "table":
      return node.title ? `${node.title} (${node.rows.length})` : `${node.rows.length} rows`;
    case "hex":
      return node.title ?? `${node.length} bytes`;
  }
}

function DetailsChip({ label, rowLabel, onOpen }: { label: string; rowLabel: string; onOpen: () => void }) {
  return (
    <button type="button" className="cell-chip" onClick={onOpen} aria-label={`Show ${label} for ${rowLabel}`} title={label}>
      {label}
    </button>
  );
}

function CellImage({ node }: { node: ImageNode }) {
  const { workspaceId, documentId } = useDocument();
  return <img className="cell-image" src={api.assetUrl(workspaceId, documentId, node.assetId)} alt={node.alt} title={node.alt} loading="lazy" width={32} height={32} />;
}

/** Fields whose editor cannot fit one line are summarised and edited in the row details. */
function needsDetailsEditor(node: FieldNode): boolean {
  return node.spec.kind === "flags" || (node.spec.kind === "text" && node.spec.multiline === true);
}

function FieldCell({ node, rowLabel, onOpenDetails }: { node: FieldNode; rowLabel: string; onOpenDetails: () => void }) {
  const { canEdit } = useDocument();
  const { value, error, modified, commit } = useLiveField(node);
  const [localError, setLocalError] = useState<string>();
  const labelId = useId();
  const shownError = localError ?? error;
  const name = rowLabel && rowLabel !== node.label ? `${node.label} (${rowLabel})` : node.label;
  const editable = canEdit && !node.spec.readOnly;
  const classes = `${modified ? " table__cell--modified" : ""}${shownError ? " table__cell--error" : ""}`;

  let control: ReactNode;
  if (!needsDetailsEditor(node)) {
    control = (
      <FieldControl id={`${labelId}-control`} labelledBy={labelId} spec={node.spec} value={value} disabled={!canEdit} compact onCommit={commit} onLocalError={setLocalError} />
    );
  } else {
    const summary = fieldSummary(node.spec, value);
    control = editable ? (
      <button type="button" className="cell-chip cell-chip--value" onClick={onOpenDetails} aria-label={`Edit ${name}: ${summary}`} title={summary}>
        {summary}
      </button>
    ) : (
      <span className="cell-text" title={summary}>
        {summary}
      </span>
    );
  }

  return (
    <Cell className={classes}>
      <span id={labelId} className="visually-hidden">
        {name}
      </span>
      <span className="cell-value">{control}</span>
      <CellBadges confidence={node.confidence} description={node.description} warning={node.warning} error={shownError} />
    </Cell>
  );
}
