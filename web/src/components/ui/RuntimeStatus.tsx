import { useEffect, useSyncExternalStore } from "react";
import { runtime } from "../../runtime";

/**
 * Boot feedback for the in-browser .NET runtime (~20 MB on first visit, cached afterwards).
 * The page stays usable while it loads; requests simply wait for it.
 */
export function RuntimeStatus() {
  const state = useSyncExternalStore(runtime.subscribe, runtime.state);
  // Start downloading immediately rather than on the first request.
  useEffect(() => { runtime.ready().catch(() => undefined); }, []);

  if (state.status === "ready") return null;
  if (state.status === "failed") {
    return <div className="runtime-status runtime-status--failed" role="alert">
      <p>
        <strong>The editor runtime could not be loaded.</strong> This app runs entirely in your browser and needs
        WebAssembly, module workers and about 100 MB of free memory. Your saves never leave this device.
      </p>
      <p className="runtime-status__detail mono">{state.message}</p>
      <button type="button" className="btn btn--small" onClick={() => window.location.reload()}>Reload</button>
    </div>;
  }
  const fraction = state.starting ? 1 : state.total > 0 ? state.loaded / state.total : 0;
  const percent = Math.round(fraction * 100);
  const text = state.starting ? "Starting the editor and its game data…"
    : `Loading the editor runtime${state.total > 0 ? ` (${state.loaded} of ${state.total} files)` : ""}…`;
  return <div className="runtime-status" role="status" aria-live="polite">
    <span>{text}</span>
    <span className="runtime-status__bar" role="progressbar" aria-label="Editor runtime download" aria-valuemin={0} aria-valuemax={100} aria-valuenow={percent}>
      <span style={{ transform: `scaleX(${fraction})` }} />
    </span>
  </div>;
}
