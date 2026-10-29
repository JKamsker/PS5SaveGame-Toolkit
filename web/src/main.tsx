import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { createBrowserRouter, RouterProvider } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { HomePage } from "./components/home/HomePage";
import "./styles/tokens.css";
import "./styles/ui.css";
import "./styles/home.css";
import "./styles/workspace.css";
import "./styles/view.css";
import "./styles/fields.css";

const queryClient = new QueryClient({
  defaultOptions: { queries: { refetchOnWindowFocus: false, staleTime: 30_000 } },
});

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
]);

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </StrictMode>,
);
