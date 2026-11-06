import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { createBrowserRouter, Link, RouterProvider } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { retryRead } from "./api/http";
import { HomePage } from "./components/home/HomePage";
import { NotFoundPage } from "./components/ui/NotFoundPage";
import { RuntimeStatus } from "./components/ui/RuntimeStatus";
import "./styles/tokens.css";
import "./styles/ui.css";
import "./styles/home.css";
import "./styles/workspace.css";
import "./styles/view.css";
import "./styles/fields.css";

const queryClient = new QueryClient({
  defaultOptions: { queries: { refetchOnWindowFocus: false, staleTime: 30_000, retry: retryRead } },
});

/** A sub-path deployment (e.g. GitHub Pages, `SAVETOOLKIT_WEB_BASE=/repo/`) routes below that path. */
const basename = import.meta.env.BASE_URL.startsWith("/") ? import.meta.env.BASE_URL.replace(/\/+$/, "") || "/" : "/";

const router = createBrowserRouter([
  { path: "/", element: <HomePage /> },
  {
    path: "/w/:workspaceId",
    lazy: async () => {
      const { WorkspacePage } = await import("./components/workspace/WorkspacePage");
      return { Component: WorkspacePage };
    },
    hydrateFallbackElement: <div className="empty" role="status">Loading workspace…</div>,
  },
  { path: "*", element: <NotFoundPage home={<Link to="/">Open the editor</Link>} /> },
], { basename });

/**
 * A static host may serve the app's 404.html for any missing path on the site (GitHub Pages only uses
 * the site root's), including paths outside the app's base. Those get a plain not-found page and do not
 * download the editor runtime.
 */
const outsideApp = basename !== "/" && !`${window.location.pathname}/`.startsWith(`${basename}/`);

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    {outsideApp ? <NotFoundPage home={<a href={import.meta.env.BASE_URL}>Open the editor</a>} /> : (
      <QueryClientProvider client={queryClient}>
        <RuntimeStatus />
        <RouterProvider router={router} />
      </QueryClientProvider>
    )}
  </StrictMode>,
);
