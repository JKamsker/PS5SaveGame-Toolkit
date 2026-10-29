import type { EmptyRowPresentation } from "../../../api/protocol";

/** One visibility control shared by eager and server-backed inventories. */
export function EmptyRowsToggle({ presentation, includeEmpty, hiddenCount, onChange }: {
  presentation?: EmptyRowPresentation;
  includeEmpty: boolean;
  hiddenCount: number;
  onChange: (includeEmpty: boolean) => void;
}) {
  if (!presentation || (!includeEmpty && hiddenCount === 0)) return null;
  const label = presentation.pluralLabel ?? "empty slots";
  return (
    <span className="table__empty-toggle">
      <span aria-live="polite">{includeEmpty ? `Showing ${label}` : `${hiddenCount.toLocaleString()} ${label} hidden`}</span>
      {" · "}
      <button type="button" className="btn btn--quiet btn--small" aria-label={`${includeEmpty ? "Hide" : "Show"} ${label}`} aria-pressed={includeEmpty} onClick={() => onChange(!includeEmpty)}>
        {includeEmpty ? "Hide" : "Show"}
      </button>
    </span>
  );
}
