import { useState } from "react";
import type { MapMarker, MapNode } from "../../../api/spatial";
import { useDocument } from "../../../state/documentStore";
import { MapDiagram } from "./MapDiagram";
import { ExternalLink, JumpButton, MAX_SPATIAL_ITEMS, SpatialFilters, TextAlternative } from "./SpatialShared";

const STATUSES = ["Found", "Missing", "Unknown"] as const;
const PACK_ASSET = /^[a-z0-9][a-z0-9._-]{0,63}:[A-Za-z0-9_][A-Za-z0-9._-]*(\/[A-Za-z0-9_][A-Za-z0-9._-]*)*$/;

export function MapView({ node }: { node: MapNode }) {
  if (node.markers.length > MAX_SPATIAL_ITEMS)
    return <p role="alert">{node.title}: map exceeds the display limits. {node.textAlternative}</p>;
  return <BoundedMap key={node.title} node={node} />;
}

function BoundedMap({ node }: { node: MapNode }) {
  const { workspaceId, documentId } = useDocument();
  const [query, setQuery] = useState("");
  const [status, setStatus] = useState("");
  const [category, setCategory] = useState("");
  const [selected, setSelected] = useState<string | null>(null);
  const [failedAsset, setFailedAsset] = useState<string | null>(null);
  const groups = [...new Set(node.markers.map(marker => marker.category))].sort();
  const needle = query.trim().toLowerCase();
  const markers = node.markers.filter(marker => (!status || marker.status === status) && (!category || marker.category === category)
    && `${marker.label} ${marker.description ?? ""} ${marker.category}`.toLowerCase().includes(needle));
  const selectedMarker = markers.find(marker => marker.id === selected);
  const bounds = node.bounds;
  const transform = node.transform;
  const validBounds = [bounds.minX, bounds.minY, bounds.maxX, bounds.maxY, transform.scaleX, transform.scaleY, transform.offsetX, transform.offsetY].every(Number.isFinite)
    && bounds.minX < bounds.maxX && bounds.minY < bounds.maxY;
  const canPosition = validBounds && node.coordinateEmbeddingPermitted && node.coordinateAttribution;
  const position = (marker: MapMarker) => {
    if (!canPosition || marker.x == null || marker.y == null) return null;
    const x = (marker.x * transform.scaleX + transform.offsetX - bounds.minX) / (bounds.maxX - bounds.minX);
    const y = (marker.y * transform.scaleY + transform.offsetY - bounds.minY) / (bounds.maxY - bounds.minY);
    return Number.isFinite(x) && Number.isFinite(y) && x >= 0 && x <= 1 && y >= 0 && y <= 1 ? { x, y } : null;
  };
  const background = node.background;
  const canEmbed = background?.embeddingPermitted && PACK_ASSET.test(background.assetId) && background.attribution;
  const hasDiagram = validBounds && (canEmbed || node.markers.some(marker => position(marker)));

  return <section className="spatial" aria-label={node.title}>
    <h3>{node.title}</h3><p>{node.textAlternative}</p>
    {node.externalSource && <ExternalLink link={node.externalSource} prominent />}
    <SpatialFilters query={query} onQuery={setQuery} status={status} onStatus={setStatus} statuses={STATUSES}
      group={category} onGroup={setCategory} groups={groups} groupLabel="Category" />
    <p className="spatial-legend">{STATUSES.map(state => <span key={state} className={`spatial-state spatial-state--${state}`}>{state}</span>)}
      <span>Categories: {groups.join(", ") || "None"}</span></p>
    <p role="status">{markers.length} of {node.markers.length} locations shown.</p>
    {hasDiagram ? <MapDiagram node={node} workspaceId={workspaceId} documentId={documentId} markers={markers}
      selected={selected} onSelect={setSelected} position={position} canEmbed={Boolean(canEmbed)}
      failedAsset={failedAsset} onAssetFailure={setFailedAsset} /> : <p>No embedded map data. Use the source link and location list.</p>}
    {background && !canEmbed && <p role="alert">Background unavailable: an embedding permission and a local pack asset are required.</p>}
    {selectedMarker && <aside className="spatial-detail" aria-label={`Selected location: ${selectedMarker.label}`}>
      <h4>{selectedMarker.label}</h4><MarkerText marker={selectedMarker} showPosition={Boolean(canPosition)} coordinateSpace={node.coordinateSpace} />
      <button type="button" className="btn btn--small" onClick={() => setSelected(null)}>Clear selection</button>
    </aside>}
    <TextAlternative summary={node.textAlternative} items={markers} renderItem={marker => <>
      <button type="button" className="btn btn--quiet" aria-pressed={selected === marker.id} onClick={() => setSelected(marker.id)}>{marker.label}</button>
      <MarkerText marker={marker} showPosition={Boolean(canPosition)} coordinateSpace={node.coordinateSpace} />
    </>} />
  </section>;
}

function MarkerText({ marker, showPosition, coordinateSpace }: { marker: MapMarker; showPosition: boolean; coordinateSpace: string }) {
  return <div className="spatial-marker-text">
    <span>{marker.status} · {marker.category}{marker.confidence && ` · ${marker.confidence}`}</span>
    {showPosition && marker.x != null && marker.y != null && <span>Position: {marker.x}, {marker.y} ({coordinateSpace})</span>}
    {marker.description && <p>{marker.description}</p>}
    <JumpButton target={marker.target} label={marker.label} />
    {marker.externalLink && <ExternalLink link={marker.externalLink} prominent />}
  </div>;
}
