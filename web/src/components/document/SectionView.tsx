import { useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, isStaleError } from "../../api/client";
import { retryRead } from "../../api/http";
import type { SectionNode, ViewNode } from "../../api/protocol";
import { RequestQueue } from "../../lib/RequestQueue";
import { useDocument } from "../../state/documentStore";
import { LazyDescendants, MAX_CONCURRENT_LAZY_REQUESTS, useAutomaticLazyExpansion } from "./LazyExpansion";
import { ReviewPath, useNextReviewPath } from "./review/navigation";
import { ViewList } from "./ViewRenderer";

// Shared across views and document generations, so retiring a view cannot multiply concurrency.
const requests = new RequestQueue(MAX_CONCURRENT_LAZY_REQUESTS);

export function SectionView({ node }: { node: SectionNode }) {
  const automatic = useAutomaticLazyExpansion(node.collapsed ? undefined : node.lazyId);
  const [requestedOpen, setRequestedOpen] = useState<boolean>();
  const open = requestedOpen ?? (node.lazyId ? automatic : !node.collapsed);
  const next = useNextReviewPath();
  const part = `section:${node.lazyId ?? node.title}`;
  useEffect(() => {
    if (next === part) setRequestedOpen(true);
  }, [next, part]);
  return (
    <details className="section" open={open} onToggle={event => {
      // Native toggle also fires for controlled changes. Only record actual user overrides.
      if (event.currentTarget.open !== open) setRequestedOpen(event.currentTarget.open);
    }}>
      <summary className="section__summary">
        <span className="section__title">{node.title}</span>
        {node.badge && <span className="section__badge">{node.badge}</span>}
        {node.lazyId && !open && <button type="button" className="btn btn--small btn--quiet"
          aria-label={`Load ${node.title}`} onClick={event => {
            event.preventDefault();
            event.currentTarget.closest("summary")?.focus();
            setRequestedOpen(true);
          }}>Load</button>}
      </summary>
      {open && (
        <div className="section__body">
          {node.description && <p className="section__description">{node.description}</p>}
          <ReviewPath part={part}>{node.lazyId ? <LazyChildren lazyId={node.lazyId} /> : <ViewList nodes={node.children} />}</ReviewPath>
        </div>
      )}
    </details>
  );
}

function LazyChildren({ lazyId }: { lazyId: string }) {
  const { workspaceId, documentId, structureVersion, refresh } = useDocument();
  const query = useQuery({
    queryKey: ["nodes", workspaceId, documentId, lazyId, structureVersion],
    queryFn: ({ signal }) => requests.run(() => api.expand(workspaceId, documentId, lazyId, signal), signal),
    staleTime: Infinity,
    retry: retryRead,
  });
  const loaded = useRef<{ nodes: ViewNode[]; generation: number } | undefined>(undefined);
  if (query.data) loaded.current = { nodes: query.data, generation: structureVersion };
  const children = query.data ?? loaded.current?.nodes;
  const stale = query.isError && isStaleError(query.error);
  const recoveryAttempted = useRef(false);
  useEffect(() => {
    if (query.isSuccess) recoveryAttempted.current = false;
    // Retry automatically once until a node request succeeds, including across document refreshes.
    if (!stale || recoveryAttempted.current) return;
    recoveryAttempted.current = true;
    void refresh().catch(() => {});
  }, [stale, query.isSuccess, refresh]);
  return <>
    {query.isFetching && <p role="status" className="section__loading">Loading…</p>}
    {stale ? <div>
    <p role="alert" className="notice notice--Danger">This section is out of date. Refresh the document to load its current contents.</p>
    <button type="button" className="btn btn--small" onClick={() => void refresh().catch(() => {})}>Refresh document</button>
    </div> : query.isError && <div>
      <p role="alert" className="notice notice--Danger">{query.error.message}</p>
      <button type="button" className="btn btn--small" disabled={query.isFetching} onClick={() => void query.refetch()}>Retry section</button>
    </div>}
    {children && <div inert={stale || loaded.current?.generation !== structureVersion}>
      <LazyDescendants><ViewList nodes={children} /></LazyDescendants>
    </div>}
  </>;
}
