import { render } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { vi } from "vitest";
import type { ReactNode } from "react";
import type { ReferenceAttribution } from "../../../api/protocol";
import type { GraphNode, MapNode } from "../../../api/spatial";
import { DocumentContext, DocumentStore, type DocumentContextValue } from "../../../state/documentStore";
import { ReviewNavigationProvider } from "../review/navigation";

const attribution: ReferenceAttribution = {
  sourceUrl: "https://example.org/original", author: "Original schematic authors", license: "CC0-1.0",
  licenseUrl: "https://creativecommons.org/publicdomain/zero/1.0/", retrieved: "2026-10-28", modifications: "Original test data.",
};
export const graph: GraphNode = {
  type: "graph", title: "Skills", textAlternative: "Dash requires Walk. Glide requires Dash.",
  items: [
    { id: "walk", label: "Walk", state: "Unlocked", branch: "Movement" },
    { id: "dash", label: "Dash", state: "Available", cost: "2 points", branch: "Movement", target: { kind: "Field", id: "skill/dash", path: ["section:Controls", "tab:Skills"] } },
    { id: "glide", label: "Glide", state: "Locked", branch: "Flight", cost: "4 points" },
    { id: "respec", label: "Respec", state: "Unknown", target: { kind: "Action", id: "reset", path: ["section:Controls", "tab:Skills"] } },
  ], edges: [{ from: "walk", to: "dash" }, { from: "dash", to: "glide" }],
};
export const map: MapNode = {
  type: "map", title: "Courtyard", textAlternative: "The cache lies west of the east gate.",
  bounds: { minX: 0, minY: 0, maxX: 100, maxY: 70 },
  transform: { scaleX: 1, scaleY: -1, offsetX: 0, offsetY: 70 }, coordinateSpace: "Original schematic",
  coordinateAttribution: attribution, coordinateEmbeddingPermitted: true,
  background: { assetId: "test.spatial:schematic.png", alt: "Original courtyard", attribution, embeddingPermitted: true },
  markers: [
    { id: "cache", label: "Courtyard cache", status: "Missing", category: "Loot", x: 20, y: 40, confidence: "Verified",
      target: { kind: "Field", id: "world/cache" }, externalLink: { url: "https://example.org/map#cache", label: "Open cache guide", attribution: "Example community, link only." } },
    { id: "gate", label: "East gate", status: "Found", category: "Travel", x: 80, y: 40 },
    { id: "unknown", label: "Unknown region", status: "Unknown", category: "Region" },
  ],
};

export function renderSpatial(children: ReactNode) {
  const store = new DocumentStore({ canUndo: false, canRedo: false, isDirty: false, revision: 0 });
  const context: DocumentContextValue = { workspaceId: "w1", documentId: "d1", canEdit: true, store,
    structureVersion: 0, refresh: vi.fn(async () => {}), commit: vi.fn(async () => true),
    runAction: vi.fn(async () => ({ ok: true, errors: [], changes: [], warnings: [], structureChanged: false, state: store.state })), mutate: async call => { const result = await call(); await context.acceptResult(result); return result; },
    acceptResult: vi.fn(async () => {}),
  };
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return { context, ...render(<QueryClientProvider client={client}><DocumentContext.Provider value={context}>
    <ReviewNavigationProvider>{children}</ReviewNavigationProvider>
  </DocumentContext.Provider></QueryClientProvider>) };
}
