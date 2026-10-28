import { useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, isStaleError } from "../../api/client";
import type { SectionNode, StatsNode, TabsNode, TextNode, ViewNode } from "../../api/protocol";
import { useDocument } from "../../state/documentStore";
import { FieldRow } from "../fields/FieldRow";
import { ActionButton } from "./ActionButton";
import { HexView } from "./HexView";
import { TableView } from "./TableView";
import { ServerTableView } from "./table/ServerTableView";

export function ViewList({ nodes }: { nodes: ViewNode[] }) {
  return (
    <div className="view-stack">
      {nodes.map((node, i) => (
        <ViewRenderer key={i} node={node} />
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
      return <Tabs node={node} />;
    case "field":
      return <FieldRow node={node} />;
    case "table":
      return <TableView node={node} />;
    case "queryableTable":
      return <ServerTableView node={node} />;
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

function Section({ node }: { node: SectionNode }) {
  const [open, setOpen] = useState(!node.collapsed);
  return (
    <details className="section" open={open} onToggle={(e) => setOpen(e.currentTarget.open)}>
      <summary className="section__summary">
        <span className="section__title">{node.title}</span>
        {node.badge && <span className="section__badge">{node.badge}</span>}
      </summary>
      {open && (
        <div className="section__body">
          {node.description && <p className="section__description">{node.description}</p>}
          {node.lazyId ? <LazyChildren lazyId={node.lazyId} /> : <ViewList nodes={node.children} />}
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
  const refreshedFor = useRef<number | null>(null);
  useEffect(() => {
    // The section belongs to a replaced view: reload the document (once) instead of showing an error.
    if (!stale || refreshedFor.current === structureVersion) return;
    refreshedFor.current = structureVersion;
    void refresh();
  }, [stale, structureVersion, refresh]);
  if (query.isPending || stale) return <p className="section__loading">Loading…</p>;
  if (query.isError) return <p className="notice notice--Danger">{query.error.message}</p>;
  return <ViewList nodes={query.data} />;
}

function Tabs({ node }: { node: TabsNode }) {
  const [active, setActive] = useState(0);
  const tab = node.tabs[active] ?? node.tabs[0];
  return (
    <div className="tabs">
      <div className="tabs__list" role="tablist">
        {node.tabs.map((t, i) => (
          <button key={t.title} type="button" role="tab" aria-selected={i === active} className="tabs__tab" onClick={() => setActive(i)}>
            {t.title}
          </button>
        ))}
      </div>
      {tab && (
        <div role="tabpanel">
          <ViewList nodes={tab.children} />
        </div>
      )}
    </div>
  );
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
