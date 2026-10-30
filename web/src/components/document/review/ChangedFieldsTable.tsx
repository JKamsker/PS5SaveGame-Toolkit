import { reviewValue, type SemanticChange } from "../../../api/actionReview";
import { useState } from "react";
import { ReviewPager } from "./paging";
import type { useChangesPanel } from "./useChangesPanel";

type ChangeReview = ReturnType<typeof useChangesPanel>;
interface ReviewProps { review: ChangeReview; focusSummary: () => void }

export function ChangedFieldsTable({ review, focusSummary }: ReviewProps) {
  // The named overflow region is intentionally keyboard-focusable for horizontal scrolling.
  // eslint-disable-next-line jsx-a11y/no-noninteractive-tabindex
  return <div className="review-scroll" role="region" aria-label="Changed fields table" tabIndex={0}
    aria-busy={review.busy || review.query.isFetching}><table className="review-table">
    <thead><tr><th>Field</th><th>Original</th><th>Current</th><th>Review</th></tr></thead>
    <tbody>{review.page.items.map(change => <ChangedFieldRow key={change.valueId} change={change} review={review} focusSummary={focusSummary} />)}</tbody>
  </table><ReviewPager {...review.page} label="Changed fields pages" /></div>;
}

function ChangedFieldRow({ change, review, focusSummary }: ReviewProps & { change: SemanticChange }) {
  const { disabled, canEdit, navigate, revert } = review;
  const canNavigate = change.canNavigate ?? Boolean(change.target || change.path.length > 0);
  const reasonId = `change-reason-${change.valueId.replace(/[^a-zA-Z0-9_-]/g, "-")}`;
  const navigationReasonId = `change-navigation-${change.valueId.replace(/[^a-zA-Z0-9_-]/g, "-")}`;
  const readOnlyReason = change.revertReason ?? (change.bindingKind === "Derived"
    ? "Derived fields update when the canonical primary changes."
    : "This field cannot be reverted safely.");
  const technicalId = `change-technical-${change.valueId.replace(/[^a-zA-Z0-9_-]/g, "-")}`;
  return <tr>
    <th scope="row">
      <span>{change.label}</span>
      {change.bindingKind === "Derived" && <span className="review-badge">Derived · read only</span>}
      {change.presentation?.confidence && <span className="review-confidence">Confidence: {change.presentation.confidence}</span>}
      {change.presentation?.warning && <span className="review-reason">{change.presentation.warning}</span>}
      {change.member && <span className="review-context">{change.member.role} · {change.member.relativePath}
        {change.member.groupTitle && <> · {change.member.groupTitle}</>}
        {change.member.provenance && <> · {change.member.provenance}</>}
      </span>}
      <TechnicalDetails id={technicalId} valueId={change.valueId} canonicalValueId={change.canonicalValueId} />
      {change.aliases && <details className="review-aliases">
        <summary>Also shown as {change.aliases.length} projection{change.aliases.length === 1 ? "" : "s"}</summary>
        <ul>{change.aliases.map(alias => <li key={alias.valueId}>
          <span>{alias.label}</span>
          {alias.changed && <span className="review-badge">Changed projection</span>}
          {alias.bindingKind === "Derived" && <span className="review-badge">Derived</span>}
          {alias.presentation?.confidence && <span className="review-confidence">Confidence: {alias.presentation.confidence}</span>}
          {alias.presentation?.warning && <span className="review-reason">{alias.presentation.warning}</span>}
          {alias.presentation?.description && <span className="review-context">{alias.presentation.description}</span>}
          {alias.member && <span className="review-context">{alias.member.role} · {alias.member.relativePath}
            {alias.member.groupTitle && <> · {alias.member.groupTitle}</>}
            {alias.member.provenance && <> · {alias.member.provenance}</>}
          </span>}
          <TechnicalDetails id={`${technicalId}-${alias.valueId.replace(/[^a-zA-Z0-9_-]/g, "-")}`}
            valueId={alias.valueId} path={alias.path} />
        </li>)}</ul>
      </details>}
      {!change.canRevert && <span id={reasonId} className="review-reason">{readOnlyReason}</span>}
      {!canNavigate && <span id={navigationReasonId} className="review-reason">
        {change.navigationTarget?.unavailableReason ?? "This field is unavailable in the current reviewed view."}
      </span>}
    </th>
    <td>{reviewValue(change.originalValue, change.originalExists)}</td><td>{reviewValue(change.currentValue, change.currentExists)}</td>
    <td className="review-actions">
      <button type="button" className="btn btn--small" disabled={disabled || !change.currentExists || !canNavigate}
        aria-describedby={!canNavigate ? navigationReasonId : undefined}
        onClick={() => void navigate(change)} aria-label={`Jump to ${change.label}`}>Jump</button>
      <button type="button" className="btn btn--small" disabled={disabled || !canEdit || !change.canRevert}
        aria-describedby={!change.canRevert ? reasonId : undefined}
        onClick={event => {
          // The refresh may remove this row. Move focus before disabling/removing its button.
          if (document.activeElement === event.currentTarget) focusSummary();
          void revert(change.valueId);
        }} aria-label={`Revert ${change.label}`}>Revert</button>
    </td>
  </tr>;
}

function TechnicalDetails({ id, valueId, canonicalValueId, path }: {
  id: string; valueId: string; canonicalValueId?: string; path?: string[]
}) {
  const [open, setOpen] = useState(false);
  const contentId = `${id}-content`;
  return <div className="review-technical">
    <button type="button" className="review-technical__toggle" aria-expanded={open}
      aria-controls={contentId} onClick={() => setOpen(value => !value)}>Technical details</button>
    {open && <small id={contentId}>Technical ID: {valueId}
      {canonicalValueId && <> · canonical {canonicalValueId}</>}
      {path && <> · path {path.join(" › ") || "unavailable"}</>}
    </small>}
  </div>;
}
