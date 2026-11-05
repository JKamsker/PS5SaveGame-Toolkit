import { fetchBinary } from "../api/client";

/**
 * Shared object URLs for binary API resources (icons, assets). Without a server, an `<img src>`
 * cannot point at `/api/...`, so resources are fetched through the runtime and turned into blob URLs.
 * URLs are reference-counted and revoked shortly after the last user unmounts, so virtualized rows
 * that scroll back into view do not refetch.
 */
interface Entry { url: Promise<string>; refs: number; timer: ReturnType<typeof setTimeout> | undefined }

const RELEASE_DELAY_MS = 30_000;
const entries = new Map<string, Entry>();

export interface ObjectUrlLease { url: Promise<string>; release: () => void }

export function acquireObjectUrl(path: string): ObjectUrlLease {
  let entry = entries.get(path);
  if (!entry) {
    const created: Entry = { refs: 0, timer: undefined, url: fetchBinary(path).then(({ blob }) => URL.createObjectURL(blob)) };
    // A failed fetch is retried by the next user rather than cached.
    created.url.catch(() => { if (entries.get(path) === created) entries.delete(path); });
    entries.set(path, created);
    entry = created;
  }
  const held = entry;
  clearTimeout(held.timer);
  held.refs++;
  let released = false;
  return {
    url: held.url,
    release: () => {
      if (released) return;
      released = true;
      if (--held.refs > 0) return;
      held.timer = setTimeout(() => {
        if (held.refs > 0 || entries.get(path) !== held) return;
        entries.delete(path);
        held.url.then(url => URL.revokeObjectURL(url), () => undefined);
      }, RELEASE_DELAY_MS);
    },
  };
}
