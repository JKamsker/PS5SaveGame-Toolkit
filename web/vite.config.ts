import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: { "/api": "http://127.0.0.1:5180" },
  },
  build: {
    outDir: "../src/SaveToolkit.Api/wwwroot",
    emptyOutDir: true,
    manifest: true,
  },
  test: {
    environment: "jsdom",
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
