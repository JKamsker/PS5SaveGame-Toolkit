import { reviewValue, type SemanticChange } from "../../../api/actionReview";
import { ReviewPager } from "./paging";
import type { useChangesPanel } from "./useChangesPanel";

type ChangeReview = ReturnType<typeof useChangesPanel>;
interface ReviewProps { review: ChangeReview; focusSummary: () => void }

export function ChangedFieldsTable({ review, focusSummary }: ReviewProps) {
  return <div className="review-scroll" aria-busy={review.busy || review.query.isFetching}><table className="review-table">
    <thead><tr><th>Field</th><th>Original</th><th>Current</th><th>Review</th></tr></thead>
    <tbody>{review.page.items.map(change => <ChangedFieldRow key={change.valueId} change={change} review={review} focusSummary={focusSummary} />)}</tbody>
  </table><ReviewPager {...review.page} label="Changed fields pages" /></div>;
}

function ChangedFieldRow({ change, review, focusSummary }: ReviewProps & { change: SemanticChange }) {
  const { disabled, canEdit, navigate, revert } = review;
  return <tr>
    <th scope="row">{change.label}<small>{change.valueId}</small></th>
    <td>{reviewValue(change.originalValue, change.originalExists)}</td><td>{reviewValue(change.currentValue, change.currentExists)}</td>
    <td className="review-actions">
      <button type="button" className="btn btn--small" disabled={disabled || !change.currentExists || (!change.target && change.path.length === 0)}
        onClick={() => void navigate(change)} aria-label={`Jump to ${change.label}`}>Jump</button>
      <button type="button" className="btn btn--small" disabled={disabled || !canEdit || !change.canRevert}
        onClick={event => {
          // The refresh may remove this row. Move focus before disabling/removing its button.
          if (document.activeElement === event.currentTarget) focusSummary();
          void revert(change.valueId);
        }} aria-label={`Revert ${change.label}`}>Revert</button>
    </td>
  </tr>;
}
