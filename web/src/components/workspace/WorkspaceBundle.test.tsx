import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { Link, MemoryRouter, Route, Routes } from "react-router";
import { afterEach, expect, it, vi } from "vitest";
import { api } from "../../api/client";
import { WorkspacePage } from "./WorkspacePage";

const { editor } = vi.hoisted(() => ({ editor: { loaded: false } }));
vi.mock("../document/DocumentEditor", () => {
  editor.loaded = true;
  return { DocumentEditor: ({ documentId }: { documentId: string }) => <h2>Editor {documentId}</h2> };
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

it("defers editor code until a document is selected, including in a restored workspace", async () => {
  vi.spyOn(api, "workspace").mockResolvedValue({
    id: "w", name: "Bundle test", createdAt: "2026-10-29", titles: [],
    documents: [{ id: "d", fileId: "f", filePath: "save.json", title: "Save", pluginId: "test", isDirty: false }],
  });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } });
  render(<QueryClientProvider client={client}><MemoryRouter initialEntries={["/w/w"]}>
    <Link to="/w/w?doc=d">Select document</Link>
    <Routes><Route path="/w/:workspaceId" element={<WorkspacePage />} /></Routes>
  </MemoryRouter></QueryClientProvider>);
  try {
    await screen.findByRole("heading", { name: "Pick a save file" });
    expect(editor.loaded).toBe(false);
    fireEvent.click(screen.getByRole("link", { name: "Select document" }));
    await screen.findByRole("heading", { name: "Editor d" });
    expect(editor.loaded).toBe(true);
  } finally {
    client.clear();
  }
});
