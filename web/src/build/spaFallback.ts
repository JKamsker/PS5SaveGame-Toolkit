import type { Plugin } from "vite";

/**
 * Static hosts (GitHub Pages, Netlify, Cloudflare Pages, ...) answer an unknown path with `404.html`.
 * Emitting a copy of `index.html` under that name makes a reloaded deep link such as `/w/<id>` load
 * the app, which then routes it, instead of the host's own 404 page.
 *
 * The copy only works from nested paths when asset URLs are absolute, i.e. the base starts with "/"
 * (`/` or `/<repo>/app/`). With a relative base ("./") `assets/...` would resolve below the deep link,
 * so no fallback is emitted and the build says why.
 */
export function spaFallback(base: string): Plugin {
  return {
    name: "savetoolkit-spa-fallback",
    apply: "build",
    enforce: "post",
    generateBundle(_options, bundle) {
      if (!base.startsWith("/")) {
        this.warn(`No 404.html fallback: base "${base}" is relative, so its asset URLs would break below a deep link.`);
        return;
      }
      const index = bundle["index.html"];
      if (index?.type !== "asset") this.error("index.html is missing from the bundle; cannot emit the 404.html fallback.");
      this.emitFile({ type: "asset", fileName: "404.html", source: index.source });
    },
  };
}
