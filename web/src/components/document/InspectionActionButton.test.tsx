import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { DocumentContext, DocumentStore } from "../../state/documentStore";
import { ActionButton } from "./ActionButton";

afterEach(cleanup);

it("enables only explicitly non-mutating actions on a read-only document", async () => {
  const store = new DocumentStore({ canUndo: false, canRedo: false, isDirty: false, revision: 7 });
  const runAction = vi.fn(async () => ({ ok: true, errors: [], changes: [], warnings: [], structureChanged: false, state: store.state }));
  const context = { workspaceId: "w", documentId: "d", canEdit: false, store, structureVersion: 0,
    commit: vi.fn(async () => true), acceptResult: async () => {}, runAction, refresh: async () => {} };
  render(<DocumentContext.Provider value={context}>
    <ActionButton node={{ type: "action", actionId: "inspect", label: "Inspect", mutates: false }} />
    <ActionButton node={{ type: "action", actionId: "legacy", label: "Legacy" }} />
    <ActionButton node={{ type: "action", actionId: "mutating", label: "Mutating", mutates: true }} />
  </DocumentContext.Provider>);
  expect((screen.getByRole("button", { name: "Inspect" }) as HTMLButtonElement).disabled).toBe(false);
  expect((screen.getByRole("button", { name: "Legacy" }) as HTMLButtonElement).disabled).toBe(true);
  expect((screen.getByRole("button", { name: "Mutating" }) as HTMLButtonElement).disabled).toBe(true);
  fireEvent.click(screen.getByRole("button", { name: "Inspect" }));
  await waitFor(() => expect(runAction).toHaveBeenCalledWith("inspect", {}));
  await waitFor(() => expect((screen.getByRole("button", { name: "Inspect" }) as HTMLButtonElement).disabled).toBe(false));
  expect(context.commit).not.toHaveBeenCalled();
  expect(store.state.revision).toBe(7);
});
