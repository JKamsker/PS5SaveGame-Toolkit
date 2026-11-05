/// <reference types="vite/client" />
import { createRuntimeClient } from "./client";

export type { RuntimeState } from "./client";
export type { BootInfo } from "./protocol";

/** `_framework/` next to index.html, honouring a sub-path base (e.g. GitHub Pages). */
const frameworkUrl = new URL(`${import.meta.env.BASE_URL}_framework/`, document.baseURI).href;

/** The single in-browser runtime. The worker (and its ~20 MB download) starts on first use. */
export const runtime = createRuntimeClient(
  () => new Worker(new URL("./worker.ts", import.meta.url), { type: "module", name: "savetoolkit-runtime" }),
  frameworkUrl,
);
