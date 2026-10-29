import { useQueryClient } from "@tanstack/react-query";
import { api, ApiError } from "../../../api/client";
import { collectionReviewQuery, type SemanticChange } from "../../../api/actionReview";
import type { ViewNode } from "../../../api/protocol";
import { useDocument } from "../../../state/documentStore";
import { collectionQueryKey } from "../table/collectionQuery";
import { useReviewNavigation } from "./navigation";

/** Resolve a bounded F8 page before publishing a focus target. This path performs reads only. */
export function useSemanticJump() {
  const doc = useDocument();
  const client = useQueryClient();
  const { jump } = useReviewNavigation();
  return async (change: SemanticChange, revision: number, isCurrent: () => boolean) => {
    const version = doc.store.valueVersion;
    if (doc.store.state.revision !== revision) throw new ApiError("This review is out of date. Refresh changes before jumping.", 409);
    if (!change.target) { jump(change); return; }
    const { collectionId, rowKey } = change.target;
    const query = collectionReviewQuery(change.target.query);
    const page = await client.fetchQuery({
      queryKey: collectionQueryKey(doc.workspaceId, doc.documentId, collectionId, query, revision, version, doc.structureVersion),
      queryFn: () => api.queryCollection(doc.workspaceId, doc.documentId, collectionId, query, revision),
      retry: false,
    });
    if (!isCurrent()) return;
    if (page.revision !== revision || doc.store.state.revision !== revision || doc.store.valueVersion !== version) {
      throw new ApiError("This review page is out of date. Refresh changes before jumping.", 409);
    }
    const row = page.rows.find(row => row.key === rowKey);
    if (page.collectionId !== collectionId || !row) {
      throw new Error("The changed row was not found on its review page. Refresh changes before trying again.");
    }
    if (!Object.values(row.cells).some(cell => canReachField(cell, change.valueId))) {
      throw new Error("The changed field was not found on its review page. Refresh changes before trying again.");
    }
    jump({ ...change, collection: { collectionId, rowKey, query } });
  };
}

/** Lazy descendants are resolved by the section renderer after opening the review path. */
function canReachField(node: ViewNode, valueId: string): boolean {
  switch (node.type) {
    case "field": return node.valueId === valueId;
    case "section": return Boolean(node.lazyId) || node.children.some(child => canReachField(child, valueId));
    case "tabs": return node.tabs.some(tab => tab.children.some(child => canReachField(child, valueId)));
    case "table": return node.rows.some(row => Object.values(row.cells).some(cell => canReachField(cell, valueId)));
    default: return false;
  }
}
