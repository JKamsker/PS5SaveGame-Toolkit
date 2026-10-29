import { useMemo, useState } from "react";
import type { GraphItem, GraphNode } from "../../../api/spatial";
import { useSpatialJump } from "../review/navigation";
import { graphLayout } from "./graphLayout";
import { GraphDiagram } from "./GraphDiagram";
import { JumpButton, MAX_GRAPH_EDGES, MAX_SPATIAL_ITEMS, SpatialFilters, TextAlternative } from "./SpatialShared";

const STATES = ["Unlocked", "Locked", "Available", "Unknown"] as const;

export function GraphView({ node }: { node: GraphNode }) {
  // Check counts before constructing a layout, filtering or rendering any items.
  if (node.items.length > MAX_SPATIAL_ITEMS || node.edges.length > MAX_GRAPH_EDGES)
    return <p role="alert">{node.title}: graph exceeds the display limits. {node.textAlternative}</p>;
  return <BoundedGraph key={node.title} node={node} />;
}

function BoundedGraph({ node }: { node: GraphNode }) {
  const [query, setQuery] = useState("");
  const [status, setStatus] = useState("");
  const [branch, setBranch] = useState("");
  const jump = useSpatialJump();
  const layout = useMemo(() => graphLayout(node), [node]);
  const labels = new Map(node.items.map(item => [item.id, item.label]));
  const groups = [...new Set(node.items.flatMap(item => item.branch ? [item.branch] : []))].sort();
  const needle = query.trim().toLowerCase();
  const items = node.items.filter(item => (!status || item.state === status) && (!branch || item.branch === branch)
    && `${item.label} ${item.description ?? ""} ${item.cost ?? ""}`.toLowerCase().includes(needle));
  const prerequisites = (item: GraphItem) => node.edges.filter(edge => edge.to === item.id).map(edge => labels.get(edge.from) ?? edge.from);
  const activate = (item: GraphItem) => { if (item.target) jump(item.target); };

  return <section className="spatial" aria-label={node.title}>
    <h3>{node.title}</h3><p>{node.textAlternative}</p>
    <SpatialFilters query={query} onQuery={setQuery} status={status} onStatus={setStatus} statuses={STATES}
      group={branch} onGroup={setBranch} groups={groups} groupLabel="Branch" />
    <p className="spatial-legend">{STATES.map(state => <span key={state} className={`spatial-state spatial-state--${state}`}>{state}</span>)}</p>
    <p role="status">{items.length} of {node.items.length} skills shown. Arrows point from prerequisite to dependent skill.</p>
    {layout ? <GraphDiagram node={node} items={items} layout={layout} prerequisites={prerequisites} onActivate={activate} /> : <p role="alert">The graph is malformed. Use the text alternative.</p>}
    <TextAlternative summary={node.textAlternative} items={items} renderItem={item => <>
      <strong>{item.label}</strong> — {item.state}{item.branch && ` · Branch: ${item.branch}`}{item.cost && ` · Cost: ${item.cost}`}
      <span> · Prerequisites: {prerequisites(item).join(", ") || "None"}</span>
      {item.description && <p>{item.description}</p>} <JumpButton label={item.label} target={item.target} />
    </>} />
  </section>;
}
