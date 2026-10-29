import { QueryClient, type QueryClientConfig } from "@tanstack/react-query";
import { vi } from "vitest";
import type { DocumentState, EditResult } from "../api/protocol";
import { DocumentStore, type DocumentContextValue } from "../state/documentStore";

interface DocumentFixtureOptions {
  state?: DocumentState;
  /** Some suites exercise result acceptance; others only inspect callbacks. */
  applyResults?: boolean;
  acceptMutations?: boolean;
  context?: Partial<DocumentContextValue>;
}

export function createDocumentFixture({ state = { canUndo: false, canRedo: false, isDirty: false, revision: 0 },
  applyResults = false, acceptMutations = false, context: overrides = {} }: DocumentFixtureOptions = {}) {
  const store = overrides.store ?? new DocumentStore(state);
  const context: DocumentContextValue = {
    workspaceId: "w1", documentId: "d1", canEdit: true, store, structureVersion: 0,
    refresh: vi.fn(async () => {}), commit: vi.fn(async () => true),
    runAction: vi.fn(async (): Promise<EditResult> => ({ ok: true, errors: [], changes: [], warnings: [], structureChanged: false, state: store.state })),
    acceptResult: vi.fn(async result => { if (applyResults) store.apply(result); }),
    mutate: async call => {
      const result = await call();
      if (acceptMutations) await context.acceptResult(result);
      return result;
    },
    ...overrides,
  };
  return { context, store };
}

/** Cache/retry/scheduler policy stays with the caller; this helper only tracks cleanup. */
export function createQueryClientScope() {
  const clients: QueryClient[] = [];
  return {
    create: (config: QueryClientConfig) => {
      const client = new QueryClient(config);
      clients.push(client);
      return client;
    },
    clear: () => clients.splice(0).forEach(client => client.clear()),
  };
}
