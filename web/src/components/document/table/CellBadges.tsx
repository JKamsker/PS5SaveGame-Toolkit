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
 * Compact indicators for a table cell. Each one is a small glyph with an accessible name and a
 * tooltip; the full text is also shown in the row's detail panel. Verified confidence is the
 * default and stays quiet, matching the field rows.
 */
export function CellBadges({ confidence, description, warning, error }: CellBadgesProps) {
  const showConfidence = confidence !== undefined && confidence !== "Verified";
  if (!showConfidence && !description && !warning && !error) return null;
  return (
    <span className="cell-badges">
      {showConfidence && (
        <Badge kind={confidence.toLowerCase()} glyph={confidenceGlyph[confidence]} label={`Confidence: ${confidenceText[confidence]}`} />
      )}
      {description && <Badge kind="info" glyph="i" label={`Description: ${description}`} />}
      {warning && <Badge kind="warning" glyph="!" label={`Warning: ${warning}`} />}
      {error && (
        <>
          <Badge kind="error" glyph="×" label={`Error: ${error}`} />
          <span className="visually-hidden" role="alert">
            {error}
          </span>
        </>
      )}
    </span>
  );
}

function Badge({ kind, glyph, label }: { kind: string; glyph: string; label: string }) {
  return (
    <span className={`cell-badge cell-badge--${kind}`} role="img" aria-label={label} title={label}>
      {glyph}
    </span>
  );
}
