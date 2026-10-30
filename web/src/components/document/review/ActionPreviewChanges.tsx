import { reviewEffectExactValue, reviewEffectValue, reviewValue } from "../../../api/actionReview";
import type { usePlannedAction } from "./usePlannedAction";
import { ReviewPager } from "./paging";

export function ActionPreviewChanges({ action }: { action: ReturnType<typeof usePlannedAction> }) {
  const { preview, changesPage, effectsPage, skippedPage } = action;
  if (!preview) return null;
  return <>
    {preview.plan.errors.map((error, index) => <p role="alert" tabIndex={-1} key={index}>{error.valueId}: {error.message}</p>)}
    {preview.plan.warnings.map((warning, index) => <p className="notice notice--Warning" key={index}>{warning}</p>)}
    {preview.plan.changes.some(change => change.effect) && <>
      <p className="notice notice--Info">{preview.plan.changes.filter(change => change.effect).length} action effects · {preview.plan.changes.length} total ordered rows · one whole-action undo</p>
      {/* eslint-disable-next-line jsx-a11y/no-noninteractive-tabindex -- Keyboard users must be able to inspect all effect rows. */}
      <div className="review-scroll" role="region" aria-label="Action effects" tabIndex={0}><table className="review-table" aria-label="Action effects">
        <thead><tr><th>Effect</th><th>Before</th><th>After</th><th>Evidence</th></tr></thead>
        <tbody>{effectsPage.items.map((change, index) => <EffectRow key={`${change.valueId}-${index}`} change={change} />)}</tbody>
      </table></div>
      <ReviewPager {...effectsPage} label="Action effects pages" />
    </>}
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

function EffectRow({ change }: { change: ReturnType<typeof usePlannedAction>["effectsPage"]["items"][number] }) {
  const effect = change.effect!;
  const evidence = [effect.kind, effect.presentation?.confidence ?? "Unspecified", effect.source,
    effect.path?.join(" / ")].filter(Boolean).join(" · ");
  return <tr>
    <th scope="row">{change.label}<small>{effect.kind} · {change.valueId}</small>
      {effect.presentation?.warning && <small className="notice--Warning">{effect.presentation.warning}</small>}
      {effect.presentation?.description && <small>{effect.presentation.description}</small>}
    </th>
    <td><Payload value={change.oldValue} exists={change.oldExists} spec={effect.spec} /></td>
    <td><Payload value={change.newValue} exists={change.newExists} spec={effect.spec} /></td>
    <td>{evidence || "Unspecified"}</td>
  </tr>;
}

function Payload({ value, exists, spec }: { value: Parameters<typeof reviewValue>[0]; exists: boolean; spec: Parameters<typeof reviewEffectValue>[2] }) {
  const text = reviewEffectValue(value, exists, spec);
  const exact = reviewEffectExactValue(value, exists, spec);
  return <details><summary>{text.length > 120 ? `${text.slice(0, 117)}…` : text}</summary>
    <pre>{exact}</pre><button type="button" className="btn btn--small" onClick={() => void navigator.clipboard?.writeText(exact)}>Copy exact</button>
  </details>;
}
