import type { ViewNode } from "../../api/protocol";

/** Prefer published identities so inserting/reordering siblings never moves another node's UI state. */
function identity(node: ViewNode): string {
  switch (node.type) {
    case "field": return JSON.stringify([node.type, node.valueId]);
    case "fieldReference": return JSON.stringify([node.type, node.targetValueId, node.label]);
    case "action": return JSON.stringify([node.type, node.actionId]);
    case "queryableTable": return JSON.stringify([node.type, node.collectionId]);
    case "table": return JSON.stringify([node.type, node.selectionId ?? node.title]);
    case "section": return JSON.stringify([node.type, node.lazyId ?? node.title]);
    case "hex": return JSON.stringify([node.type, node.blobId]);
    case "image": return JSON.stringify([node.type, node.assetId]);
    case "tabs": return JSON.stringify([node.type, node.tabs.map(tab => tab.title).sort()]);
    case "text": return JSON.stringify([node.type, node.text]);
    case "stats": return JSON.stringify([node.type, node.items.map(item => item.label).sort()]);
    default: return JSON.stringify([node.type, node.title]);
  }
}

export function viewKeys(nodes: readonly ViewNode[]): string[] {
  const occurrences = new Map<string, number>();
  return nodes.map(node => {
    const key = identity(node);
    const occurrence = occurrences.get(key) ?? 0;
    occurrences.set(key, occurrence + 1);
    // The protocol permits repeated labels and has no universal node ID. Disambiguate these
    // siblings without penalising the stable identities available on fields, actions and lazy nodes.
    return `${key}:${occurrence}`;
  });
}
