import { useState, type ReactNode } from "react";
import type { ExternalViewLink, ViewTarget } from "../../../api/spatial";
import type { ReferenceAttribution } from "../../../api/protocol";
import { useSpatialJump } from "../review/navigation";
import "./spatial.css";

// Keep aligned with SpatialViewLimits in the SDK. These also bound malformed client payloads.
export const MAX_SPATIAL_ITEMS = 512;
export const MAX_GRAPH_EDGES = 2048;
const TEXT_PAGE_SIZE = 50;

export function httpsUrl(value: string): URL | null {
  // eslint-disable-next-line no-control-regex -- Reject literal control characters before URL parsing can normalize them.
  if (!/^https:\/\//i.test(value) || /[\s\\\x00-\x1f\x7f]/.test(value)) return null;
  try {
    const url = new URL(value);
    return url.protocol === "https:" && url.hostname && !url.username && !url.password ? url : null;
  } catch { return null; }
}

/** No prefetching, remote images, embeds or network calls: this is only a user-activated anchor. */
export function ExternalLink({ link, prominent = false }: { link: ExternalViewLink; prominent?: boolean }) {
  const url = httpsUrl(link.url);
  return <span className="spatial-source">
    {url ? <a className={prominent ? "btn btn--small" : undefined} href={url.href} target="_blank" rel="noopener noreferrer">{link.label} ({url.host})</a> : <span>{link.label} — link unavailable (HTTPS required)</span>}
    <span>{link.attribution}</span>
  </span>;
}

export function Attribution({ source }: { source: ReferenceAttribution }) {
  return <div className="spatial-attribution">
    <ExternalLink link={{ url: source.sourceUrl, label: "Source", attribution: source.author }} />
    <ExternalLink link={{ url: source.licenseUrl, label: source.license, attribution: `Retrieved ${source.retrieved}. ${source.modifications}` }} />
  </div>;
}

export function JumpButton({ target, label }: { target?: ViewTarget; label: string }) {
  const jump = useSpatialJump();
  return target ? <button className="btn btn--small" type="button" onClick={() => jump(target)}>Jump to {label}</button> : null;
}

export function SpatialFilters({ query, onQuery, status, onStatus, statuses, group, onGroup, groups, groupLabel }: {
  query: string; onQuery: (value: string) => void; status: string; onStatus: (value: string) => void;
  statuses: readonly string[]; group: string; onGroup: (value: string) => void; groups: string[]; groupLabel: string;
}) {
  return <div className="spatial-toolbar">
    <label>Find locations or skills <input className="input" type="search" value={query} onChange={e => onQuery(e.target.value)} /></label>
    <label>Status <select className="input" value={status} onChange={e => onStatus(e.target.value)}>
      <option value="">All statuses</option>{statuses.map(s => <option key={s}>{s}</option>)}
    </select></label>
    <label>{groupLabel} <select className="input" value={group} onChange={e => onGroup(e.target.value)}>
      <option value="">All</option>{groups.map(g => <option key={g}>{g}</option>)}
    </select></label>
  </div>;
}

/** A real text list remains available without SVG/artwork; paging keeps the keyboard tab order short. */
export function TextAlternative<T extends { id: string }>({ summary, items, renderItem }: {
  summary: string; items: T[]; renderItem: (item: T) => ReactNode;
}) {
  const [page, setPage] = useState(0);
  const lastPage = Math.max(0, Math.ceil(items.length / TEXT_PAGE_SIZE) - 1);
  const current = Math.min(page, lastPage);
  return <details className="spatial-text" open>
    <summary>Text alternative ({items.length} {items.length === 1 ? "item" : "items"})</summary><p>{summary}</p>
    {items.length === 0 ? <p>No items match the filters.</p> : <ul>{items.slice(current * TEXT_PAGE_SIZE, (current + 1) * TEXT_PAGE_SIZE).map(item => <li key={item.id}>{renderItem(item)}</li>)}</ul>}
    {lastPage > 0 && <div className="spatial-toolbar">
      <button type="button" className="btn" disabled={current === 0} onClick={() => setPage(current - 1)}>Previous items</button>
      <span>Page {current + 1} of {lastPage + 1}</span>
      <button type="button" className="btn" disabled={current === lastPage} onClick={() => setPage(current + 1)}>Next items</button>
    </div>}
  </details>;
}
