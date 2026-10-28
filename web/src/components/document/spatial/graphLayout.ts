import type { GraphNode } from "../../../api/spatial";

export const GRAPH_CARD_WIDTH = 200;
export const GRAPH_CARD_HEIGHT = 120;
const COLUMN_STEP = 250;
const ROW_STEP = 150;
const PADDING = 24;

/** Deterministic layered DAG layout, computed before filtering so cards do not jump between filters. */
export function graphLayout(graph: GraphNode) {
  const items = new Map(graph.items.map(item => [item.id, item]));
  const incoming = new Map(graph.items.map(item => [item.id, 0]));
  const outgoing = new Map(graph.items.map(item => [item.id, [] as string[]]));
  for (const edge of graph.edges) {
    if (!items.has(edge.from) || !items.has(edge.to)) return null;
    incoming.set(edge.to, incoming.get(edge.to)! + 1);
    outgoing.get(edge.from)!.push(edge.to);
  }
  const queue = graph.items.filter(item => !incoming.get(item.id)).map(item => item.id);
  const layers = new Map(graph.items.map(item => [item.id, 0]));
  for (let index = 0; index < queue.length; index++) {
    const id = queue[index]!;
    for (const next of outgoing.get(id)!) {
      layers.set(next, Math.max(layers.get(next)!, layers.get(id)! + 1));
      incoming.set(next, incoming.get(next)! - 1);
      if (!incoming.get(next)) queue.push(next);
    }
  }
  if (queue.length !== graph.items.length || items.size !== graph.items.length) return null;
  const positions = new Map<string, { x: number; y: number }>();
  const rows = new Map<number, number>();
  const ordered = [...graph.items].sort((a, b) => {
    if (!a.branch) return b.branch ? 1 : 0;
    if (!b.branch) return -1;
    return a.branch.localeCompare(b.branch);
  });
  for (const item of ordered) {
    const layer = layers.get(item.id)!;
    const row = rows.get(layer) ?? 0;
    positions.set(item.id, { x: PADDING + layer * COLUMN_STEP, y: PADDING + row * ROW_STEP });
    rows.set(layer, row + 1);
  }
  return { positions, width: Math.max(600, ...[...positions.values()].map(p => p.x + GRAPH_CARD_WIDTH + PADDING)),
    height: Math.max(300, ...[...positions.values()].map(p => p.y + GRAPH_CARD_HEIGHT + PADDING)) };
}
