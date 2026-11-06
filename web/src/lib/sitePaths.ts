/**
 * The app may be deployed below a sub-path of a larger site (GitHub Pages: documentation at `/<repo>/`,
 * the app at `/<repo>/app/`). Static hosts serve the app's 404.html for missing paths anywhere on that
 * site, so the page must tell its own routes from the rest of the site.
 */

/** True for an address outside the app's router `basename` (never for an app served from the root). */
export function isOutsideApp(pathname: string, basename: string): boolean {
  return basename !== "/" && !`${pathname}/`.startsWith(`${basename}/`);
}

/** The site's home above the app: `/<repo>/app/` → `/<repo>/`, `/app/` → `/`. */
export function siteHome(appBase: string): string {
  return appBase.replace(/[^/]+\/?$/, "") || "/";
}
