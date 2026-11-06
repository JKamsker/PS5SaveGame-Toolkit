import { useEffect, useRef, useState, useSyncExternalStore, type ImgHTMLAttributes, type RefObject } from "react";
import { acquireObjectUrl, objectUrlStamp, subscribeObjectUrls } from "../../lib/apiObjectUrls";

type ApiImageProps = Omit<ImgHTMLAttributes<HTMLImageElement>, "src" | "onError"> & {
  /** API-relative path, e.g. from `api.assetPath(...)`. */
  path: string;
  /** Called when the resource cannot be fetched or decoded. */
  onFailure?: () => void;
};

/**
 * An image served by the in-browser runtime. `loading="lazy"` defers the fetch until it is near the
 * viewport. When the cached bytes go stale (a re-imported reference pack, a structural edit), it fetches them again
 * and shows the previous picture until the new one has arrived.
 */
export function ApiImage({ path, onFailure, loading, alt, ...props }: ApiImageProps) {
  const image = useRef<HTMLImageElement>(null);
  const visible = useNearViewport(image, loading === "lazy");
  const stamp = useSyncExternalStore(subscribeObjectUrls, () => objectUrlStamp(path));
  const [loaded, setLoaded] = useState<{ path: string; url: string } | null>(null);
  const failure = useRef(onFailure);
  useEffect(() => { failure.current = onFailure; });

  useEffect(() => {
    if (!visible) return;
    let active = true;
    const lease = acquireObjectUrl(path);
    lease.url.then(url => { if (active) setLoaded({ path, url }); }, () => { if (active) failure.current?.(); });
    return () => { active = false; lease.release(); };
  }, [path, visible, stamp]);

  const src = loaded?.path === path ? loaded.url : undefined;
  return <img ref={image} {...props} alt={alt} loading={loading} src={src} onError={() => failure.current?.()} />;
}

function useNearViewport(target: RefObject<HTMLElement | null>, lazy: boolean): boolean {
  const [visible, setVisible] = useState(!lazy || typeof IntersectionObserver === "undefined");
  useEffect(() => {
    const element = target.current;
    if (visible || !element) return;
    const observer = new IntersectionObserver(entries => {
      if (entries.some(entry => entry.isIntersecting)) { setVisible(true); observer.disconnect(); }
    }, { rootMargin: "200px" });
    observer.observe(element);
    return () => observer.disconnect();
  }, [target, visible]);
  return visible;
}
