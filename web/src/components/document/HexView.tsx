import { useEffect, useRef, useState } from "react";
import { useVirtualizer } from "@tanstack/react-virtual";
import { api } from "../../api/client";
import type { HexNode } from "../../api/protocol";
import { formatBytes, parseInteger } from "../../lib/values";
import { useDocument } from "../../state/documentStore";

const BYTES_PER_ROW = 16;
const PAGE = 16 * 1024;
const ROW_HEIGHT = 22;

const hex2 = (b: number) => b.toString(16).padStart(2, "0");
const printable = (b: number) => (b >= 0x20 && b < 0x7f ? String.fromCharCode(b) : "·");

/** Paged, virtualised hex dump. Pages are fetched on demand and cached per view. */
export function HexView({ node }: { node: HexNode }) {
  const { workspaceId, documentId } = useDocument();
  const [pages, setPages] = useState<Map<number, Uint8Array>>(new Map());
  const loading = useRef(new Set<number>());
  const scroller = useRef<HTMLDivElement>(null);
  const rowCount = Math.ceil(node.length / BYTES_PER_ROW);
  const virtualizer = useVirtualizer({ count: rowCount, getScrollElement: () => scroller.current, estimateSize: () => ROW_HEIGHT, overscan: 20 });
  const items = virtualizer.getVirtualItems();

  const firstPage = Math.floor(((items[0]?.index ?? 0) * BYTES_PER_ROW) / PAGE);
  const lastPage = Math.floor(((items.at(-1)?.index ?? 0) * BYTES_PER_ROW) / PAGE);
  useEffect(() => {
    for (let p = firstPage; p <= lastPage; p++) {
      if (pages.has(p) || loading.current.has(p)) continue;
      loading.current.add(p);
      void api.blob(workspaceId, documentId, node.blobId, p * PAGE, PAGE).then((bytes) => {
        loading.current.delete(p);
        setPages((prev) => new Map(prev).set(p, bytes));
      });
    }
  }, [firstPage, lastPage, pages, workspaceId, documentId, node.blobId]);

  const byteAt = (offset: number) => pages.get(Math.floor(offset / PAGE))?.[offset % PAGE];

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
      <div className="hex__scroller" ref={scroller}>
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
