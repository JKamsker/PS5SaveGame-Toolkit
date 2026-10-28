import type { ReferenceCandidate, ReferenceDatasetInfo } from "../../../api/protocol";
import { api } from "../../../api/client";

export function ReferenceIcon({ candidate, workspaceId, documentId }: { candidate: ReferenceCandidate; workspaceId: string; documentId: string }) {
  return candidate.assetId ? (
    <img className="reference-icon" src={api.referenceAssetUrl(workspaceId, documentId, candidate.assetId)} alt="" width={32} height={32}
      onError={(event) => { event.currentTarget.hidden = true; }} />
  ) : <span className="reference-icon reference-icon--empty" aria-hidden="true">◇</span>;
}

export function ReferencePreview({ candidate, workspaceId, documentId }: { candidate: ReferenceCandidate; workspaceId: string; documentId: string }) {
  return (
    <section className="reference-preview" aria-label="Item details">
      <ReferenceIcon candidate={candidate} workspaceId={workspaceId} documentId={documentId} />
      <h3>{candidate.label ?? candidate.rawId}</h3>
      <code>{candidate.rawId}</code>
      {!candidate.label && <p>Unknown ID. The current raw value is preserved until you choose a replacement.</p>}
      {candidate.category && <p>Category: {candidate.category}</p>}
      {candidate.description && <p>{candidate.description}</p>}
      {candidate.eligibility.reasons.map((reason, index) => <p key={index} className="field__warning">{reason}</p>)}
      {candidate.source && <p>Record source: <SourceLink href={candidate.source} /></p>}
      {candidate.dataset && <Attribution title="Catalog attribution" dataset={candidate.dataset} />}
      {candidate.assetDataset && <Attribution title="Icon attribution" dataset={candidate.assetDataset} />}
    </section>
  );
}

function Attribution({ title, dataset }: { title: string; dataset: ReferenceDatasetInfo }) {
  const a = dataset.attribution;
  return <div className="reference-attribution">
    <strong>{title}</strong>
    <p>{a.author} · Licence: <SourceLink href={a.licenseUrl} label={a.license} /></p>
    <p>Source: <SourceLink href={a.sourceUrl} /></p>
    <p>Retrieved <time dateTime={a.retrieved}>{a.retrieved}</time> · Changes: {a.modifications}</p>
  </div>;
}

function SourceLink({ href, label = href }: { href: string; label?: string }) {
  return /^https?:\/\//i.test(href) ? <a href={href} target="_blank" rel="noopener noreferrer">{label}</a> : <span>{label}</span>;
}
