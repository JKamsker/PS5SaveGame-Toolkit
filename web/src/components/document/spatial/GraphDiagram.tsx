import type { GraphItem, GraphNode } from "../../../api/spatial";
import { GRAPH_CARD_HEIGHT, GRAPH_CARD_WIDTH, type graphLayout } from "./graphLayout";
import { SpatialCanvas } from "./SpatialCanvas";

interface GraphDiagramProps {
  node: GraphNode;
  items: GraphItem[];
  layout: NonNullable<ReturnType<typeof graphLayout>>;
  prerequisites: (item: GraphItem) => string[];
  onActivate: (item: GraphItem) => void;
}

/** Keep the full-graph layout while filters change which cards and connecting edges are visible. */
export function GraphDiagram({ node, items, layout, prerequisites, onActivate }: GraphDiagramProps) {
  const shown = new Set(items.map(item => item.id));
  return <SpatialCanvas label={node.title} width={layout.width} height={layout.height}>
    <svg className="spatial-edges" width={layout.width} height={layout.height} aria-hidden="true">
      {node.edges.filter(edge => shown.has(edge.from) && shown.has(edge.to)).map(edge => {
        const from = layout.positions.get(edge.from)!;
        const to = layout.positions.get(edge.to)!;
        const x = to.x;
        const y = to.y + GRAPH_CARD_HEIGHT / 2;
        return <g key={`${edge.from.length}:${edge.from}${edge.to}`}>
          <line x1={from.x + GRAPH_CARD_WIDTH} y1={from.y + GRAPH_CARD_HEIGHT / 2} x2={x} y2={y} />
          <path d={`M ${x - 8} ${y - 5} L ${x} ${y} L ${x - 8} ${y + 5}`} />
        </g>;
      })}
    </svg>
    {items.map(item => {
      const position = layout.positions.get(item.id)!;
      return <button key={item.id} type="button" className={`spatial-card spatial-state--${item.state}`}
        style={{ left: position.x, top: position.y, width: GRAPH_CARD_WIDTH, height: GRAPH_CARD_HEIGHT }}
        aria-label={`${item.label}: ${item.state}${item.cost ? `, cost ${item.cost}` : ""}${item.target ? ", jump to control" : ""}`}
        onClick={() => onActivate(item)} title={[item.description, `Prerequisites: ${prerequisites(item).join(", ") || "None"}`].filter(Boolean).join(". ")}>
        <strong>{item.label}</strong><span>{item.state}{item.cost ? ` · Cost: ${item.cost}` : ""}</span>
        {item.branch && <span>{item.branch}</span>}
      </button>;
    })}
  </SpatialCanvas>;
}
