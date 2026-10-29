import { render } from "@testing-library/react";
import { QueryClientProvider } from "@tanstack/react-query";
import { vi } from "vitest";
import type { ReactNode } from "react";
import type { EditResult } from "../../../api/protocol";
import { DocumentContext } from "../../../state/documentStore";
import { createDocumentFixture, createQueryClientScope } from "../../../test/documentFixture";
import { ReviewNavigationProvider } from "./navigation";

const clients = createQueryClientScope();
export const clearReviewClients = () => clients.clear();
const result: EditResult = { ok: true, errors: [], changes: [], warnings: [], structureChanged: false,
  state: { canUndo: true, canRedo: false, isDirty: true, revision: 2 } };

export function renderDocument(children: ReactNode, canEdit = true) {
  const { context, store } = createDocumentFixture({
    state: { canUndo: true, canRedo: false, isDirty: true, revision: 1 },
    applyResults: true, acceptMutations: true,
    context: { canEdit, runAction: vi.fn(async () => result) },
  });
  const client = clients.create({ defaultOptions: { queries: { retry: false } } });
  const rendered = render(<QueryClientProvider client={client}><DocumentContext.Provider value={context}>
    <ReviewNavigationProvider>{children}</ReviewNavigationProvider>
  </DocumentContext.Provider></QueryClientProvider>);
  return { ...rendered, context, store };
}

