import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import type { ActionNode } from "../../api/protocol";
import { DocumentContext, DocumentStore } from "../../state/documentStore";
import { ActionButton } from "./ActionButton";
import { installDialogStub } from "../../test/dialog";

let restoreDialog: () => void;
beforeEach(() => { restoreDialog = installDialogStub(); });
afterEach(() => { cleanup(); vi.restoreAllMocks(); restoreDialog(); });

function setup(planned = false) {
  const store = new DocumentStore({ canUndo: false, canRedo: false, isDirty: false, revision: 1 });
  const context = { workspaceId: "w", documentId: "d", canEdit: true, store, structureVersion: 0,
    commit: vi.fn(async () => true), mutate: vi.fn(), acceptResult: vi.fn(), runAction: vi.fn(), refresh: vi.fn() };
  const node: ActionNode = { type: "action", actionId: "run", label: "Run", supportsPlanning: planned,
    parameters: [{ type: "field", valueId: "index", label: "Entry index", description: "Choose an entry.",
      spec: { kind: "integer", min: "0", max: "3", readOnly: true }, value: "0" }] };
  const view = (action: ActionNode) => <DocumentContext.Provider value={context}><ActionButton node={action} /></DocumentContext.Provider>;
  const mounted = render(view(node));
  fireEvent.click(screen.getByRole("button", { name: "Run" }));
  return { node, rerender: (action: ActionNode) => mounted.rerender(view(action)) };
}

it.each([false, true])("renders writable action parameters with descriptions (planned: %s)", planned => {
  setup(planned);
  const input = screen.getByRole("textbox", { name: "Entry index" });
  expect(input).toHaveProperty("disabled", false);
  const description = screen.getByText("Choose an entry.");
  expect(input.getAttribute("aria-describedby")?.split(" ")).toContain(description.id);
});

it.each([false, true])("preserves the direct draft and resets the planned draft on reopen (planned: %s)", planned => {
  setup(planned);
  const input = screen.getByRole("textbox", { name: "Entry index" });
  fireEvent.change(input, { target: { value: "2" } });
  fireEvent.blur(input);
  fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
  fireEvent.click(screen.getByRole("button", { name: "Run" }));
  expect(screen.getByRole("textbox", { name: "Entry index" })).toHaveProperty("value", planned ? "0" : "2");
});

it("replaces a committed direct parameter with rebuilt action defaults", () => {
  const { node, rerender } = setup();
  const input = screen.getByRole("textbox", { name: "Entry index" });
  fireEvent.change(input, { target: { value: "2" } });
  fireEvent.blur(input);
  expect(input).toHaveProperty("value", "2");
  rerender({ ...node, parameters: [{ ...node.parameters![0]!, value: "1" }] });
  expect(screen.getByRole("textbox", { name: "Entry index" })).toHaveProperty("value", "1");
  expect(screen.queryByRole("alert")).toBeNull();
  expect(within(screen.getByRole("dialog")).getByRole("button", { name: "Run" })).toHaveProperty("disabled", false);
});
