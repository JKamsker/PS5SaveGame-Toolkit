import { useRef, useState, type ReactNode } from "react";

const MIN_ZOOM = 0.5;
const MAX_ZOOM = 3;
const ZOOM_STEP = 0.25;
const PAN_STEP = 80;

/** Native scrolling supports touch/trackpads; drag empty space or use keyboard arrows to pan. */
export function SpatialCanvas({ label, width, height, children }: { label: string; width: number; height: number; children: ReactNode }) {
  const [zoom, setZoom] = useState(1);
  const viewport = useRef<HTMLDivElement>(null);
  const drag = useRef<{ x: number; y: number; left: number; top: number } | null>(null);
  return <>
    <div className="spatial-toolbar">
      <button type="button" className="btn btn--small" aria-label={`Zoom out ${label}`} disabled={zoom <= MIN_ZOOM} onClick={() => setZoom(Math.max(MIN_ZOOM, zoom - ZOOM_STEP))}>−</button>
      <output aria-label="Zoom">{Math.round(zoom * 100)}%</output>
      <button type="button" className="btn btn--small" aria-label={`Zoom in ${label}`} disabled={zoom >= MAX_ZOOM} onClick={() => setZoom(Math.min(MAX_ZOOM, zoom + ZOOM_STEP))}>+</button>
      <button type="button" className="btn btn--small" onClick={() => { setZoom(1); viewport.current?.scrollTo?.(0, 0); }}>Reset view</button>
      <span>Drag empty space, scroll, or focus the diagram and use arrow keys to pan.</span>
    </div>
    {/* eslint-disable-next-line jsx-a11y/no-noninteractive-element-interactions, jsx-a11y/no-noninteractive-tabindex -- The named diagram region supports both arrow-key panning and pointer dragging. */}
    <div className="spatial-viewport" ref={viewport} tabIndex={0} role="region" aria-label={`${label} diagram`}
      onKeyDown={e => {
        if (e.target !== e.currentTarget) return;
        const directions: Record<string, [number, number]> = { ArrowLeft: [-PAN_STEP, 0], ArrowRight: [PAN_STEP, 0], ArrowUp: [0, -PAN_STEP], ArrowDown: [0, PAN_STEP] };
        const delta = directions[e.key];
        if (delta) { e.preventDefault(); e.currentTarget.scrollLeft += delta[0]; e.currentTarget.scrollTop += delta[1]; }
      }}
      onPointerDown={e => {
        if (e.button !== 0 || (e.target as Element).closest("button, a")) return;
        drag.current = { x: e.clientX, y: e.clientY, left: e.currentTarget.scrollLeft, top: e.currentTarget.scrollTop };
        e.currentTarget.setPointerCapture?.(e.pointerId);
      }}
      onPointerMove={e => {
        if (!drag.current) return;
        e.currentTarget.scrollLeft = drag.current.left + drag.current.x - e.clientX;
        e.currentTarget.scrollTop = drag.current.top + drag.current.y - e.clientY;
      }}
      onPointerUp={() => { drag.current = null; }} onPointerCancel={() => { drag.current = null; }} onLostPointerCapture={() => { drag.current = null; }}>
      <div style={{ width: width * zoom, height: height * zoom }}>
        <div className="spatial-canvas" style={{ width, height, transform: `scale(${zoom})` }}>{children}</div>
      </div>
    </div>
  </>;
}
