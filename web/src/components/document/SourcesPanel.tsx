import { useEffect, useId, useState, type ChangeEvent, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, ApiError } from "../../api/client";
import type { DocumentSource } from "../../api/protocol";
import "../../styles/sources.css";

interface SourcesPanelProps {
  workspaceId: string;
  documentId: string;
  /** Upstream URLs from the plugin's `Info.References`. */
  pluginReferences: string[];
  /** Bumped when the view is rebuilt; plugins may resolve more datasets then. */
  refreshKey: number;
  onToast: (message: string, isError?: boolean) => void;
}

/**
 * Per-document "Sources" disclosure: every reference dataset the plugin used, with author, licence,
 * source link, retrieval date and modifications (rule 9 of the plugin guide), plus the plugin's references.
 */
export function SourcesPanel({ workspaceId, documentId, pluginReferences, refreshKey, onToast }: SourcesPanelProps) {
  const [open, setOpen] = useState(false);
  const panelId = useId();
  const query = useQuery({
    queryKey: ["sources", workspaceId, documentId, refreshKey],
    queryFn: () => api.sources(workspaceId, documentId),
  });
  const { refetch } = query;

  useEffect(() => {
    if (open) void refetch();
  }, [open, refetch]);

  const used = query.data?.datasets.filter((d) => d.used) ?? [];
  const importPack = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) return;
    try {
      const pack = await api.importReferencePack(file);
      onToast(`Imported “${pack.title}”. Re-open the document to use it.`);
    } catch (error) {
      onToast(error instanceof ApiError ? error.message : "The pack could not be imported.", true);
    }
  };

  return (
    <section className="sources">
      <button type="button" className="sources__toggle" aria-expanded={open} aria-controls={panelId} onClick={() => setOpen((o) => !o)}>
        Sources and attribution
        <span className="sources__count" aria-label={`${used.length} datasets in use`}>
          {used.length}
        </span>
      </button>
      {open && (
        <div id={panelId} className="sources__panel">
          {query.isPending && <p className="sources__empty">Loading…</p>}
          {query.isError && <p className="notice notice--Danger">{query.error.message}</p>}
          {query.data && <SourcesList datasets={query.data.datasets} pluginReferences={pluginReferences} />}
          <label className="btn btn--small btn--quiet sources__import">
            Import reference pack…
            <input type="file" accept=".zip,application/zip" className="visually-hidden" onChange={importPack} />
          </label>
        </div>
      )}
    </section>
  );
}

interface SourcesListProps {
  datasets: DocumentSource[];
  pluginReferences: string[];
}

/** Presentational part of the panel; also used by tests. */
export function SourcesList({ datasets, pluginReferences }: SourcesListProps) {
  const used = datasets.filter((d) => d.used);
  const unused = datasets.filter((d) => !d.used);
  return (
    <div className="sources__body">
      <h2 className="sources__heading">Reference data in this document</h2>
      {used.length === 0 ? (
        <p className="sources__empty">No reference pack is used here. Names and values come from the save and the plugin itself.</p>
      ) : (
        <ul className="sources__list">
          {used.map((d) => (
            <SourceItem key={`${d.packId}/${d.datasetId}`} source={d} />
          ))}
        </ul>
      )}
      {unused.length > 0 && (
        <details className="sources__more">
          <summary>Also loaded for this game ({unused.length})</summary>
          <ul className="sources__list">
            {unused.map((d) => (
              <SourceItem key={`${d.packId}/${d.datasetId}`} source={d} />
            ))}
          </ul>
        </details>
      )}
      {pluginReferences.length > 0 && (
        <>
          <h2 className="sources__heading">Plugin references</h2>
          <ul className="sources__refs">
            {pluginReferences.map((url) => (
              <li key={url}>
                <ExternalLink href={url}>{url}</ExternalLink>
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  );
}

function SourceItem({ source: d }: { source: DocumentSource }) {
  const a = d.attribution;
  const count = `${d.itemCount} ${d.kind === "Assets" ? "images" : "records"}`;
  return (
    <li className="sources__item">
      <div className="sources__title">
        <strong>{d.title ?? d.datasetId}</strong>
        <span className="sources__meta">
          {d.packTitle} {d.packVersion} · {d.origin === "Embedded" ? "built-in" : "imported"}
          {d.locale ? ` · ${d.locale}` : ""} · {count}
        </span>
      </div>
      <p className="sources__attribution">
        {a.author} · Licence: <ExternalLink href={a.licenseUrl}>{a.license}</ExternalLink> · Source:{" "}
        <ExternalLink href={a.sourceUrl}>{hostOf(a.sourceUrl)}</ExternalLink> · Retrieved <time dateTime={a.retrieved}>{a.retrieved}</time>
      </p>
      <p className="sources__changes">Changes: {a.modifications}</p>
    </li>
  );
}

/** Only http(s) links become anchors; the server validates too, this is defence in depth. */
function ExternalLink({ href, children }: { href: string; children: ReactNode }) {
  if (!/^https?:\/\//i.test(href)) return <span>{children}</span>;
  return (
    <a href={href} target="_blank" rel="noopener noreferrer">
      {children}
    </a>
  );
}

function hostOf(url: string): string {
  try {
    return new URL(url).host || url;
  } catch {
    return url;
  }
}
