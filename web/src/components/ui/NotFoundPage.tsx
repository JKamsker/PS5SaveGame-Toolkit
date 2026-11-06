import type { ReactNode } from "react";

/**
 * Shown for an address the app does not know. Static hosts serve the app's 404.html (a copy of
 * index.html) for every missing path, so this also covers mistyped URLs elsewhere on the same site.
 */
export function NotFoundPage({ home }: { home: ReactNode }) {
  return (
    <main className="empty">
      <h1>Page not found</h1>
      <p>There is nothing at this address. Imported saves are kept only in the tab that imported them, so
        an old workspace link cannot be reopened.</p>
      <p>{home}</p>
    </main>
  );
}
