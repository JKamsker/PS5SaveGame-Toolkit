import { memo } from "react";
import { api } from "../../api/client";
import type { StatsNode, TextNode, ViewNode } from "../../api/protocol";
import { useDocument } from "../../state/documentStore";
import { FieldRow } from "../fields/FieldRow";
import { ActionButton } from "./ActionButton";
import { HexView } from "./HexView";
import { useFieldJump } from "./review/navigation";
import { LazyExpansionBoundary } from "./LazyExpansion";
import { SectionView } from "./SectionView";
import { TableView } from "./TableView";
import { ServerTableView } from "./table/ServerTableView";
import { SpatialView } from "./spatial/SpatialView";
import { TabsView } from "./TabsView";
import { viewKeys } from "./viewKeys";

export function ViewList({ nodes }: { nodes: ViewNode[] }) {
  const keys = viewKeys(nodes);
  return (
    <LazyExpansionBoundary><div className="view-stack">
      {nodes.map((node, i) => (
        <ViewRenderer key={keys[i]} node={node} />
      ))}
    </div></LazyExpansionBoundary>
  );
}

/** Maps abstract view nodes to components. Unknown node types render nothing rather than crash. */
// Snapshots are immutable; live fields and navigation retain their own subscriptions.
export const ViewRenderer = memo(function ViewRenderer({ node }: { node: ViewNode }) {
  return <LazyExpansionBoundary><ViewContent node={node} /></LazyExpansionBoundary>;
});

function ViewContent({ node }: { node: ViewNode }) {
  switch (node.type) {
    case "section":
      return <SectionView node={node} />;
    case "tabs":
      return <TabsView node={node} />;
    case "field":
      return <NavigableField node={node} />;
    case "table":
      return <TableView node={node} />;
    case "queryableTable":
      return <ServerTableView node={node} />;
    case "graph":
    case "map":
      return <SpatialView node={node} />;
    case "text":
      return <Text node={node} />;
    case "stats":
      return <Stats node={node} />;
    case "image":
      return <DocumentImage assetId={node.assetId} alt={node.alt} />;
    case "action":
      return <ActionButton node={node} />;
    case "hex":
      return <HexView node={node} />;
    default:
      return null;
  }
}

function NavigableField({ node }: { node: Extract<ViewNode, { type: "field" }> }) {
  const ref = useFieldJump(node.valueId);
  return <div ref={ref} tabIndex={-1} className="review-field"><FieldRow node={node} /></div>;
}

function Text({ node }: { node: TextNode }) {
  return <p className={`notice notice--${node.tone ?? "Normal"}${node.monospace ? " mono" : ""}`}>{node.text}</p>;
}

function Stats({ node }: { node: StatsNode }) {
  return (
    <dl className="stats">
      {node.items.map((s) => (
        <div key={s.label} className="stats__item" title={s.hint}>
          <dd className="stats__value" style={{ margin: 0 }}>
            {s.value}
          </dd>
          <dt className="stats__label">{s.label}</dt>
        </div>
      ))}
    </dl>
  );
}

function DocumentImage({ assetId, alt }: { assetId: string; alt: string }) {
  const { workspaceId, documentId } = useDocument();
  return <img className="image" src={api.assetUrl(workspaceId, documentId, assetId)} alt={alt} loading="lazy" />;
}
