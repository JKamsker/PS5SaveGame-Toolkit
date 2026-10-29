import { api } from "../../../api/client";
import type { MapMarker, MapNode } from "../../../api/spatial";
import { SpatialCanvas } from "./SpatialCanvas";
import { Attribution } from "./SpatialShared";

const MAP_WIDTH = 1000;
const MAP_HEIGHT = 700;

interface MapDiagramProps {
  node: MapNode;
  workspaceId: string;
  documentId: string;
  markers: MapMarker[];
  selected: string | null;
  onSelect: (id: string) => void;
  position: (marker: MapMarker) => { x: number; y: number } | null;
  canEmbed: boolean;
  failedAsset: string | null;
  onAssetFailure: (assetId: string) => void;
}

export function MapDiagram({ node, workspaceId, documentId, markers, selected, onSelect, position, canEmbed, failedAsset, onAssetFailure }: MapDiagramProps) {
  const background = node.background;
  return <>
    <p>Coordinate space: {node.coordinateSpace}. Unpositioned locations remain in the text list.</p>
    <SpatialCanvas label={node.title} width={MAP_WIDTH} height={MAP_HEIGHT}>
      {canEmbed && background && failedAsset !== background.assetId && <img className="spatial-background" alt={background.alt}
        src={api.referenceAssetUrl(workspaceId, documentId, background.assetId)} draggable={false}
        onError={() => onAssetFailure(background.assetId)} />}
      {markers.map(marker => {
        const point = position(marker);
        if (!point) return null;
        return <button key={marker.id} type="button" className={`spatial-marker spatial-state--${marker.status}`}
          style={{ left: `${point.x * 100}%`, top: `${point.y * 100}%` }} aria-pressed={selected === marker.id}
          aria-label={`${marker.label}: ${marker.status}, ${marker.category}`} title={marker.label} onClick={() => onSelect(marker.id)}>
          {marker.status === "Found" ? "✓" : marker.status === "Missing" ? "!" : "?"}
        </button>;
      })}
    </SpatialCanvas>
    {canEmbed && background && <Attribution source={background.attribution} />}
    {failedAsset === background?.assetId && <p role="status">Background unavailable. The schematic and text list remain usable.</p>}
    {node.coordinateAttribution && <Attribution source={node.coordinateAttribution} />}
  </>;
}
