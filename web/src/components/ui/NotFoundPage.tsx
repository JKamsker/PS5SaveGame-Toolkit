import type { ReactNode } from "react";

/** Shown for an address inside the app that it does not know (a mistyped or outdated editor link). */
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

/**
 * Shown for a missing page elsewhere on the site the app is deployed in (GitHub Pages serves the app's
 * 404.html for every missing path, documentation included). It does not start the editor runtime.
 */
export function SiteNotFoundPage({ documentation, editor }: { documentation: string; editor: string }) {
  return (
    <main className="empty">
      <h1>Page not found</h1>
      <p>There is no page at this address. It may have been moved or renamed; start from the documentation
        home or open the editor.</p>
      <p><a href={documentation}>Documentation home</a> · <a href={editor}>Open the editor</a></p>
    </main>
  );
}
