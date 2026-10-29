import { reviewValue } from "../../../api/actionReview";
import type { usePlannedAction } from "./usePlannedAction";
import { ReviewPager } from "./paging";

export function ActionPreviewChanges({ action }: { action: ReturnType<typeof usePlannedAction> }) {
  const { preview, changesPage, skippedPage } = action;
  if (!preview) return null;
  return <>
    {preview.plan.errors.map((error, index) => <p role="alert" tabIndex={-1} key={index}>{error.valueId}: {error.message}</p>)}
    {preview.plan.warnings.map((warning, index) => <p className="notice notice--Warning" key={index}>{warning}</p>)}
    {/* eslint-disable-next-line jsx-a11y/no-noninteractive-tabindex -- Keyboard users must be able to scroll the preview table. */}
    <div className="review-scroll" role="region" aria-label="Preview changes" tabIndex={0}><table className="review-table" aria-label="Preview changes">
      <thead><tr><th>Field</th><th>Before</th><th>After</th></tr></thead>
      <tbody>{changesPage.items.map((change, index) => <tr key={index}>
        <th scope="row">{change.label}{change.derived && <small> (derived)</small>}</th>
        <td>{reviewValue(change.oldValue, change.oldExists)}</td><td>{reviewValue(change.newValue, change.newExists)}</td>
      </tr>)}</tbody>
    </table></div>
    <ReviewPager {...changesPage} label="Preview changes pages" />
    {preview.plan.skippedRows.length > 0 && <details><summary>Skipped rows</summary>
      <ul>{skippedPage.items.map((row, index) => <li key={index}>{row.rowKey}: {row.reason}</li>)}</ul>
      <ReviewPager {...skippedPage} label="Skipped rows pages" />
    </details>}
  </>;
}
