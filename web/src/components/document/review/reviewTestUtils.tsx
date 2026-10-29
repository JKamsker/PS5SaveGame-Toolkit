import { render } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { vi } from "vitest";
import type { ReactNode } from "react";
import type { EditResult } from "../../../api/protocol";
import { DocumentContext, DocumentStore, type DocumentContextValue } from "../../../state/documentStore";
import { ReviewNavigationProvider } from "./navigation";

const clients: QueryClient[] = [];
export const clearReviewClients = () => clients.splice(0).forEach(client => client.clear());
const result: EditResult = { ok: true, errors: [], changes: [], warnings: [], structureChanged: false,
  state: { canUndo: true, canRedo: false, isDirty: true, revision: 2 } };

export function renderDocument(children: ReactNode, canEdit = true) {
  const store = new DocumentStore({ canUndo: true, canRedo: false, isDirty: true, revision: 1 });
  const context: DocumentContextValue = { workspaceId: "w1", documentId: "d1", canEdit, store, structureVersion: 0,
    refresh: vi.fn(async () => {}), commit: vi.fn(async () => true), runAction: vi.fn(async () => result),
    mutate: async call => { const outcome = await call(); await context.acceptResult(outcome); return outcome; }, acceptResult: vi.fn(async outcome => { store.apply(outcome); }),
  };
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  clients.push(client);
  const rendered = render(<QueryClientProvider client={client}><DocumentContext.Provider value={context}>
    <ReviewNavigationProvider>{children}</ReviewNavigationProvider>
  </DocumentContext.Provider></QueryClientProvider>);
  return { ...rendered, context, store };
}

