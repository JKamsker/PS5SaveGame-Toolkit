import { expect, it, vi } from "vitest";
import type { RouteObject } from "react-router";

const { createBrowserRouter, loadedWorkspace } = vi.hoisted(() => ({
  createBrowserRouter: vi.fn((routes: RouteObject[]) => routes), loadedWorkspace: vi.fn(),
}));
vi.mock("react-dom/client", () => ({ createRoot: () => ({ render: vi.fn() }) }));
vi.mock("react-router", () => ({ createBrowserRouter, RouterProvider: () => null }));
vi.mock("./components/home/HomePage", () => ({ HomePage: () => null }));
vi.mock("./components/workspace/WorkspacePage", () => {
  loadedWorkspace();
  return { WorkspacePage: () => null };
});

it("loads the editor code only when the workspace route is requested", async () => {
  await import("./main");
  expect(loadedWorkspace).not.toHaveBeenCalled();
  const routes = createBrowserRouter.mock.calls[0]![0];
  const workspace = routes.find(route => route.path === "/w/:workspaceId")!;
  if (typeof workspace.lazy !== "function") throw new Error("Workspace route must load lazily");
  expect((await workspace.lazy()).Component).toBeTypeOf("function");
  expect(loadedWorkspace).toHaveBeenCalledTimes(1);
});
