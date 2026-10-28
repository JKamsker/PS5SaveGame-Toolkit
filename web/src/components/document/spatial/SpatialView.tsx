import { Component, lazy, Suspense, type ReactNode } from "react";
import type { GraphNode, MapNode } from "../../../api/spatial";

const GraphView = lazy(() => import("./GraphView").then(module => ({ default: module.GraphView })));
const MapView = lazy(() => import("./MapView").then(module => ({ default: module.MapView })));

/** Spatial layouts and styles are only downloaded when the corresponding view is opened. */
export function SpatialView({ node }: { node: GraphNode | MapNode }) {
  return <SpatialBoundary key={`${node.type}:${node.title}`} title={node.title} alternative={node.textAlternative}>
    <Suspense fallback={<p role="status">Loading {node.title}…</p>}>
      {node.type === "graph" ? <GraphView node={node} /> : <MapView node={node} />}
    </Suspense>
  </SpatialBoundary>;
}

class SpatialBoundary extends Component<{ title: string; alternative: string; children: ReactNode }, { failed: boolean }> {
  state = { failed: false };

  static getDerivedStateFromError() { return { failed: true }; }

  render() {
    if (!this.state.failed) return this.props.children;
    return <section aria-label={this.props.title}>
      <h3>{this.props.title}</h3>
      <p role="alert">Could not load this view. Reload the page to try again.</p>
      <p>{this.props.alternative}</p>
    </section>;
  }
}
