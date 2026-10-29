import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import type { FieldNode } from "../../api/protocol";
import { DocumentContext, DocumentStore } from "../../state/documentStore";
import { ActionButton } from "../document/ActionButton";
import { FieldRow } from "./FieldRow";

beforeAll(() => {
  HTMLDialogElement.prototype.showModal = function () { this.open = true; };
  HTMLDialogElement.prototype.close = function () { this.open = false; };
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

const parameters: FieldNode[] = [
  { type: "field", valueId: "optional", label: "Optional name", spec: { kind: "nullable", inner: { kind: "text" } }, value: "Alice" },
  { type: "field", valueId: "variant", label: "Saved setting", spec: { kind: "union", options: [
    { id: "boolean", label: "Boolean", spec: { kind: "bool" } },
  ] }, value: { kind: "union", type: "boolean", value: true } },
  { type: "field", valueId: "flags", label: "Unlocked areas", spec: { kind: "flags", bitWidth: 8, bits: [
    { bit: 0, label: "Village" },
  ] }, value: "1" },
];

function renderFields(content: React.ReactNode) {
  const store = new DocumentStore({ canUndo: false, canRedo: false, isDirty: false, revision: 1 });
  const commit = vi.fn(async () => true);
  const context = { workspaceId: "w", documentId: "d", canEdit: true, store, structureVersion: 0,
    commit, acceptResult: async () => {}, runAction: vi.fn(), refresh: async () => {} };
  return { ...render(<DocumentContext.Provider value={context}>{content}</DocumentContext.Provider>), store, commit };
}

describe.each([false, true])("action parameter names (planned: %s)", supportsPlanning => {
  it("names composite inner controls and type selectors after their visible fields", () => {
    renderFields(<ActionButton node={{ type: "action", actionId: "run", label: "Run", parameters, supportsPlanning }} />);
    fireEvent.click(screen.getByRole("button", { name: "Run" }));
    const dialog = within(screen.getByRole("dialog"));
    expect(dialog.getByRole("textbox", { name: "Optional name" })).toBeTruthy();
    expect(dialog.getByRole("combobox", { name: "Optional name Null or value" })).toBeTruthy();
    expect(dialog.getByRole("switch", { name: "Saved setting" })).toBeTruthy();
    expect(dialog.getByRole("combobox", { name: "Saved setting Value type" })).toBeTruthy();
    expect(dialog.getByRole("group", { name: "Unlocked areas" })).toBeTruthy();
    expect(dialog.getByRole("checkbox", { name: "Village" })).toBeTruthy();
  });
});

it("names flags groups in ordinary field rows", () => {
  renderFields(<FieldRow node={parameters[2]!} />);
  expect(screen.getByRole("group", { name: "Unlocked areas" })).toBeTruthy();
});
