import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { createBrowserRouter, RouterProvider } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { HomePage } from "./components/home/HomePage";
import { WorkspacePage } from "./components/workspace/WorkspacePage";
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
  { path: "/w/:workspaceId", element: <WorkspacePage /> },
]);

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </StrictMode>,
);
