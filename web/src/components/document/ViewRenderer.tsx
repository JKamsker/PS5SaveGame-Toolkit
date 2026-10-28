import { useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, isStaleError } from "../../api/client";
import type { SectionNode, StatsNode, TextNode, ViewNode } from "../../api/protocol";
import { useDocument } from "../../state/documentStore";
import { FieldRow } from "../fields/FieldRow";
import { ActionButton } from "./ActionButton";
import { HexView } from "./HexView";
import { ReviewPath, useFieldJump, useNextReviewPath } from "./review/navigation";
import { TableView } from "./TableView";
import { ServerTableView } from "./table/ServerTableView";
import { SpatialView } from "./spatial/SpatialView";
import { TabsView } from "./TabsView";
import { viewKeys } from "./viewKeys";

export function ViewList({ nodes }: { nodes: ViewNode[] }) {
  const keys = viewKeys(nodes);
  return (
    <div className="view-stack">
      {nodes.map((node, i) => (
        <ViewRenderer key={keys[i]} node={node} />
      ))}
    </div>
  );
}

/** Maps abstract view nodes to components. Unknown node types render nothing rather than crash. */
export function ViewRenderer({ node }: { node: ViewNode }) {
  switch (node.type) {
    case "section":
      return <Section node={node} />;
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

function Section({ node }: { node: SectionNode }) {
  const [open, setOpen] = useState(!node.collapsed);
  const next = useNextReviewPath();
  const part = `section:${node.lazyId ?? node.title}`;
  useEffect(() => {
    if (next === part) setOpen(true);
  }, [next, part]);
  return (
    <details className="section" open={open} onToggle={(e) => setOpen(e.currentTarget.open)}>
      <summary className="section__summary">
        <span className="section__title">{node.title}</span>
        {node.badge && <span className="section__badge">{node.badge}</span>}
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
    queryFn: () => api.expand(workspaceId, documentId, lazyId),
    staleTime: Infinity,
    retry: (count, error) => !isStaleError(error) && count < 3,
  });
  const stale = query.isError && isStaleError(query.error);
  const recoveryAttempted = useRef(false);
  useEffect(() => {
    if (query.isSuccess) recoveryAttempted.current = false;
    // Retry automatically once until a node request succeeds, including across document refreshes.
    if (!stale || recoveryAttempted.current) return;
    recoveryAttempted.current = true;
    void refresh().catch(() => {});
  }, [stale, query.isSuccess, refresh]);
  if (stale) return <div>
    <p role="alert" className="notice notice--Danger">This section is out of date. Refresh the document to load its current contents.</p>
    <button type="button" className="btn btn--small" onClick={() => void refresh().catch(() => {})}>Refresh document</button>
  </div>;
  if (query.isPending) return <p className="section__loading">Loading…</p>;
  if (query.isError) return <p className="notice notice--Danger">{query.error.message}</p>;
  return <ViewList nodes={query.data} />;
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
