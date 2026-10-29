import { useDeferredValue, useEffect, useMemo, useState } from "react";
import type { CollectionQuery, QueryableTableNode } from "../../../api/protocol";
import { useNextReviewPath, useReviewNavigation } from "../review/navigation";
import { DEFAULT_COLLECTION_PAGE_SIZE } from "./collectionQuery";

/** Keep a review's exact page request until the user changes the query or the document changes. */
export function useCollectionQuery(node: QueryableTableNode) {
  const { target, clear } = useReviewNavigation();
  const next = useNextReviewPath();
  const destination = target?.collection?.collectionId === node.collectionId && next === `row:${target.collection.rowKey}`
    ? target.collection : undefined;
  const [requested, setRequested] = useState<CollectionQuery>({ filters: {}, descending: false, offset: 0, limit: DEFAULT_COLLECTION_PAGE_SIZE,
    includeEmpty: node.emptyRows ? !(node.emptyRows.hideByDefault ?? true) : undefined });
  const deferredText = useDeferredValue(requested.q);
  const query = useMemo(() => destination?.query ?? { ...requested, q: deferredText }, [destination, requested, deferredText]);
  // Keep this page visible and refetch it at the next revision, while retiring its focus intent.
  useEffect(() => { if (destination) setRequested(destination.query); }, [destination]);
  const update = (patch: Partial<CollectionQuery>, resetPage = true) => {
    // Stop an old jump from reapplying its query or stealing focus after user navigation.
    clear();
    setRequested(previous => ({ ...(destination?.query ?? previous), ...patch,
      ...(resetPage ? { offset: 0, cursor: undefined } : {}) }));
  };
  return { query, update, text: destination ? query.q ?? "" : requested.q ?? "",
    pendingSearch: !destination && requested.q !== deferredText, reviewing: Boolean(destination) };
}
