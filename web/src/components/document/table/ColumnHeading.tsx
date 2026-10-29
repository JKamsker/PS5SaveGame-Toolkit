import { useId, useState } from "react";
import type { Confidence, TableColumn } from "../../../api/protocol";
import { CellBadges } from "./CellBadges";

const explanation: Record<Confidence, string> = {
  Verified: "The plugin labels this field's meaning as verified.",
  Inferred: "The plugin infers this field's meaning; it has not been fully verified.",
  Unknown: "The plugin does not know this field's meaning. Treat its value as raw data.",
};

export function ColumnHeading({ column, numeric, confidence }: { column: TableColumn; numeric: boolean; confidence: Confidence | undefined }) {
  const tooltipId = useId();
  const [dismissed, setDismissed] = useState(false);
  return (
    <div className={`table__head${numeric ? " table__head--number" : ""}`} role="columnheader" title={column.description ? `${column.label}: ${column.description}` : column.label}>
      <span className="table__head-label">{column.label}</span>
      {column.description && <span className="visually-hidden">: {column.description}</span>}
      {/* eslint-disable-next-line jsx-a11y/no-static-element-interactions, jsx-a11y/no-noninteractive-tabindex -- Focus exposes the descriptive tooltip; Escape dismisses it without adding a button action. */}
      {confidence && <span className={`table__confidence${dismissed ? " table__confidence--dismissed" : ""}`} tabIndex={0} aria-describedby={tooltipId}
        onMouseEnter={() => setDismissed(false)} onFocus={() => setDismissed(false)}
        onKeyDown={event => { if (event.key === "Escape") setDismissed(true); }}>
        <CellBadges confidence={confidence} />
        <span id={tooltipId} role="tooltip" className="table__confidence-tooltip">
          {explanation[confidence]} Applies to all currently shown rows in this column.
        </span>
      </span>}
    </div>
  );
}
