import type { Confidence } from "../../../api/protocol";

const confidenceText: Record<Confidence, string> = { Verified: "verified", Inferred: "inferred", Unknown: "meaning unknown" };
const confidenceGlyph: Record<Confidence, string> = { Verified: "✓", Inferred: "≈", Unknown: "?" };

interface CellBadgesProps {
  confidence?: Confidence | undefined;
  description?: string | undefined;
  warning?: string | undefined;
  error?: string | undefined;
}

/**
 * Explicit confidence words and diagnostic labels fit within the fixed-height data row.
 * Accessible names and tooltips retain the full meaning; details retain all supporting text.
 */
export function CellBadges({ confidence, description, warning, error }: CellBadgesProps) {
  const showConfidence = confidence !== undefined;
  if (!showConfidence && !description && !warning && !error) return null;
  return (
    <span className="cell-badges">
      {showConfidence && (
        <Badge kind={confidence.toLowerCase()} glyph={confidenceGlyph[confidence]} text={confidence} label={`Confidence: ${confidenceText[confidence]}`} />
      )}
      {(description || warning || error) && <span className="cell-badges__diagnostics">
        {description && <Badge kind="info" glyph="i" label={`Description: ${description}`} />}
        {warning && <Badge kind="warning" glyph="!" text="Warning" label={`Warning: ${warning}`} />}
        {error && (
          <>
            <Badge kind="error" glyph="×" text="Error" label={`Error: ${error}`} />
            <span className="visually-hidden" role="alert">
              {error}
            </span>
          </>
        )}
      </span>}
    </span>
  );
}

function Badge({ kind, glyph, text, label }: { kind: string; glyph: string; text?: string; label: string }) {
  return (
    <span className={`cell-badge cell-badge--${kind}${text ? " cell-badge--label" : ""}`} tabIndex={kind === "info" ? 0 : undefined} role="img" aria-label={label} title={label}>
      <span aria-hidden="true">{glyph}</span>{text && <span>{text}</span>}
    </span>
  );
}
