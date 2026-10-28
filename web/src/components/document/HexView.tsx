import { useRef } from "react";
import { useQueries } from "@tanstack/react-query";
import { useVirtualizer } from "@tanstack/react-virtual";
import { api } from "../../api/client";
import type { HexNode } from "../../api/protocol";
import { formatBytes, parseInteger } from "../../lib/values";
import { useDocument, useDocumentState } from "../../state/documentStore";

const BYTES_PER_ROW = 16;
const PAGE = 16 * 1024;
const ROW_HEIGHT = 22;

const hex2 = (b: number) => b.toString(16).padStart(2, "0");
const printable = (b: number) => (b >= 0x20 && b < 0x7f ? String.fromCharCode(b) : "·");

/** Paged, virtualised hex dump. Pages are fetched on demand and cached per view. */
export function HexView({ node }: { node: HexNode }) {
  const { workspaceId, documentId, store, structureVersion } = useDocument();
  const { state, version } = useDocumentState(store);
  const scroller = useRef<HTMLDivElement>(null);
  const rowCount = Math.ceil(node.length / BYTES_PER_ROW);
  const virtualizer = useVirtualizer({ count: rowCount, getScrollElement: () => scroller.current, estimateSize: () => ROW_HEIGHT, overscan: 20 });
  const items = virtualizer.getVirtualItems();

  const firstPage = Math.floor(((items[0]?.index ?? 0) * BYTES_PER_ROW) / PAGE);
  const lastPage = Math.floor(((items.at(-1)?.index ?? 0) * BYTES_PER_ROW) / PAGE);
  const pageNumbers = node.length > 0 ? Array.from({ length: lastPage - firstPage + 1 }, (_, i) => firstPage + i) : [];
  const pages = useQueries({ queries: pageNumbers.map(page => ({
    queryKey: ["blob", workspaceId, documentId, node.blobId, state.revision, structureVersion, version, page],
    queryFn: () => api.blob(workspaceId, documentId, node.blobId, page * PAGE, PAGE),
    staleTime: Infinity,
    gcTime: 60_000,
    retry: false,
  })) });
  const byteAt = (offset: number) => pages[Math.floor(offset / PAGE) - firstPage]?.data?.[offset % PAGE];
  const failed = pages.filter(page => page.isError);

  return (
    <div className="hex">
      <div className="hex__head">
        <strong>{node.title ?? "Bytes"}</strong>
        <span className="file__size">{formatBytes(node.length)}</span>
        <input
          className="hex__jump mono"
          placeholder="Go to 0x…"
          aria-label="Go to offset"
          onKeyDown={(e) => {
            if (e.key !== "Enter") return;
            const target = parseInteger(e.currentTarget.value);
            if (target !== null) virtualizer.scrollToIndex(Math.floor(Number(target) / BYTES_PER_ROW), { align: "start" });
          }}
        />
      </div>
      {pages.some(page => page.isPending) && <p role="status">Loading bytes…</p>}
      {failed.length > 0 && <div>
        <p role="alert">Could not load bytes: {failed[0]!.error?.message}</p>
        <button type="button" className="btn btn--small" onClick={() => { for (const page of failed) void page.refetch(); }}>Retry bytes</button>
      </div>}
      <div className="hex__scroller" ref={scroller} tabIndex={0} role="region" aria-label={`${node.title ?? "Bytes"} hex dump`}>
        <div style={{ height: virtualizer.getTotalSize(), position: "relative" }}>
          {items.map((item) => {
            const start = item.index * BYTES_PER_ROW;
            const bytes = Array.from({ length: Math.min(BYTES_PER_ROW, node.length - start) }, (_, i) => byteAt(start + i));
            return (
              <div key={item.key} className="hex__row" style={{ position: "absolute", top: 0, transform: `translateY(${item.start}px)` }}>
                <span className="hex__offset">{start.toString(16).padStart(8, "0")}</span>
                <span>{bytes.map((b) => (b === undefined ? "  " : hex2(b))).join(" ")}</span>
                <span className="hex__ascii">{bytes.map((b) => (b === undefined ? " " : printable(b))).join("")}</span>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}
