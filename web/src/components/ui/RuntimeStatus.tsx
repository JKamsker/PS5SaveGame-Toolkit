import { useEffect, useState, useSyncExternalStore } from "react";
import { runtime, type BootInfo } from "../../runtime";

/**
 * Boot feedback for the in-browser .NET runtime (~20 MB on first visit, cached afterwards).
 * The page stays usable while it loads; requests simply wait for it.
 */
export function RuntimeStatus() {
  const state = useSyncExternalStore(runtime.subscribe, runtime.state);
  // Start downloading immediately rather than on the first request.
  useEffect(() => { runtime.ready().catch(() => undefined); }, []);

  if (state.status === "ready") return <RuntimeNotices boot={state.boot} />;
  if (state.status === "failed") return <RuntimeFailure message={state.message} crashed={state.crashed === true} />;
  return <RuntimeLoading loaded={state.loaded} total={state.total} starting={state.starting === true} />;
}

function RuntimeFailure({ message, crashed }: { message: string; crashed: boolean }) {
  return <div className="runtime-status runtime-status--failed" role="alert">
    {crashed ? <p>
      <strong>The editor runtime stopped unexpectedly.</strong> Imported saves and unsaved changes in this tab were
      lost with it. Reload the page and import your saves again.
    </p> : <p>
      <strong>The editor runtime could not be loaded.</strong> This app runs entirely in your browser and needs
      WebAssembly, module workers and about 100 MB of free memory. Your saves never leave this device.
    </p>}
    <p className="runtime-status__detail mono">{message}</p>
    <button type="button" className="btn btn--small" onClick={() => window.location.reload()}>Reload</button>
  </div>;
}

/**
 * The runtime only learns how many files it needs while it downloads them, so the file total grows and
 * `loaded / total` can fall. The bar never moves backwards, the progressbar stays indeterminate for
 * assistive technology until the download is complete (no value it could later contradict), and the
 * polite live region only changes between its two phases rather than on every file.
 */
function RuntimeLoading({ loaded, total, starting }: { loaded: number; total: number; starting: boolean }) {
  const fraction = starting ? 1 : total > 0 ? Math.min(1, loaded / total) : 0;
  const [peak, setPeak] = useState(fraction);
  if (fraction > peak) setPeak(fraction);
  const shown = Math.max(peak, fraction);
  return <div className="runtime-status">
    <span role="status" aria-live="polite">{starting ? "Starting the editor and its game data…" : "Loading the editor runtime…"}</span>
    {!starting && total > 0 && <span className="runtime-status__files">{loaded} of {total} files</span>}
    <span className="runtime-status__bar" role="progressbar" aria-label="Editor runtime download"
      {...(starting ? { "aria-valuemin": 0, "aria-valuemax": 100, "aria-valuenow": 100 } : {})}>
      <span style={{ transform: `scaleX(${shown})` }} />
    </span>
  </div>;
}

/** Non-blocking warnings about the booted runtime: storage that will not last, plugins that are missing. */
export function runtimeNotices(boot: BootInfo): string[] {
  const notices: string[] = [];
  if (!boot.persistent) {
    notices.push("Browser storage is unavailable (for example in a private window), so presets and reference packs "
      + "you add will not survive a reload. Export presets you want to keep.");
  } else if (!boot.tabCoordination) {
    notices.push("This browser cannot coordinate tabs here, so change presets and reference packs in one tab at a "
      + "time; saving in two tabs can overwrite the other tab's changes.");
  }
  if (boot.failedAssemblies.length > 0) {
    notices.push(`Some game plugins could not be loaded and are unavailable until a reload: `
      + `${boot.failedAssemblies.join(", ")}. Reload the page to try again.`);
  }
  return notices;
}

function RuntimeNotices({ boot }: { boot: BootInfo }) {
  const [dismissed, setDismissed] = useState(false);
  const notices = runtimeNotices(boot);
  if (dismissed || notices.length === 0) return null;
  return <div className="runtime-status runtime-status--warning" role="status">
    {notices.map(notice => <p key={notice}>{notice}</p>)}
    <button type="button" className="btn btn--small" onClick={() => setDismissed(true)}>Dismiss</button>
  </div>;
}
