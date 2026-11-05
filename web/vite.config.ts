import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

// tsconfig limits ambient types to vite/client (src must not see Node globals).
declare const process: { env: Record<string, string | undefined> };

/**
 * The app is a static site: the .NET runtime (public/_framework, from `npm run build:wasm`) runs in a
 * worker and answers /api requests in the browser. Set SAVETOOLKIT_WEB_BASE (e.g. "/PS5SaveGame-Toolkit/")
 * to deploy below a sub-path such as GitHub Pages.
 */
export default defineConfig({
  base: process.env.SAVETOOLKIT_WEB_BASE ?? "/",
  plugins: [react()],
  server: {
    port: 5173,
  },
  worker: {
    format: "es",
  },
  build: {
    outDir: "dist",
    emptyOutDir: true,
    manifest: true,
  },
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test/setupRuntime.ts"],
    coverage: {
      provider: "v8",
      include: ["src/**/*.{ts,tsx}"],
      exclude: ["**/*.{test,spec}.{ts,tsx}", "**/*.d.ts"],
      reporter: [
        "text-summary",
        ["text-summary", { file: "summary.txt" }],
        "json-summary",
        "cobertura",
        "html",
      ],
    },
  },
});
