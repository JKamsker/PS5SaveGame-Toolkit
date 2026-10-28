import type { Confidence, ReferenceAttribution } from "./protocol";

export interface ViewTarget { kind: "Field" | "Action" | "Row"; id: string; path?: string[] }
export interface ExternalViewLink { url: string; label: string; attribution: string }
export type GraphState = "Unlocked" | "Locked" | "Available" | "Unknown";
export interface GraphItem {
  id: string; label: string; state: GraphState; cost?: string; branch?: string;
  description?: string; target?: ViewTarget;
}
export interface GraphEdge { from: string; to: string }
export interface GraphNode { type: "graph"; title: string; items: GraphItem[]; edges: GraphEdge[]; textAlternative: string }
export interface MapBounds { minX: number; minY: number; maxX: number; maxY: number }
export interface MapTransform { scaleX: number; scaleY: number; offsetX: number; offsetY: number }
export interface MapBackground { assetId: string; alt: string; attribution: ReferenceAttribution; embeddingPermitted?: boolean }
export type MapMarkerStatus = "Found" | "Missing" | "Unknown";
export interface MapMarker {
  id: string; label: string; status: MapMarkerStatus; category: string;
  x?: number; y?: number; description?: string; target?: ViewTarget;
  externalLink?: ExternalViewLink; confidence?: Confidence;
}
export interface MapNode {
  type: "map"; title: string; bounds: MapBounds; markers: MapMarker[]; textAlternative: string;
  transform: MapTransform; coordinateSpace: string;
  coordinateAttribution?: ReferenceAttribution; coordinateEmbeddingPermitted?: boolean;
  background?: MapBackground; externalSource?: ExternalViewLink;
}
