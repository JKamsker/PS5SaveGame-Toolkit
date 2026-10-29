import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import type { FieldNode } from "../../api/protocol";
import { DocumentContext, DocumentStore } from "../../state/documentStore";
import { ActionButton } from "../document/ActionButton";
import { FieldRow } from "./FieldRow";
import { TableCell } from "../document/table/TableCell";

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

describe("field supporting text", () => {
  const node: FieldNode = { type: "field", valueId: "count", label: "Item count", value: "2",
    spec: { kind: "integer", min: "0", max: "3" }, warning: "Unverified in-game", description: "Existing stack only" };

  it("describes a focused field with its warning, help and local validation error", () => {
    renderFields(<FieldRow node={node} />);
    const input = screen.getByRole("textbox", { name: "Item count", description: /Unverified in-game.*Existing stack only/ });
    fireEvent.change(input, { target: { value: "99" } });
    fireEvent.blur(input);
    const error = screen.getByRole("alert");
    expect(input.getAttribute("aria-describedby")?.split(" ")).toContain(error.id);
    expect(screen.getByRole("textbox", { description: new RegExp(error.textContent!) })).toBe(input);
  });

  it("makes range and formatted-duration hints available on focus", () => {
    renderFields(<><FieldRow node={{ ...node, warning: undefined, description: undefined }} />
      <FieldRow node={{ ...node, valueId: "clock", label: "Time", value: 61,
        spec: { kind: "float", format: "Duration", unitsPerSecond: 1 } }} /></>);
    expect(screen.getByRole("textbox", { name: "Item count", description: /Range/ })).toBeTruthy();
    expect(screen.getByRole("textbox", { name: "Time", description: /1 min 1 s/ })).toBeTruthy();
  });

  it("keeps warnings and errors associated with compact table editors", () => {
    renderFields(<TableCell node={node} rowLabel="Potion" onOpenDetails={() => {}} />);
    const input = screen.getByRole("textbox", { name: "Item count (Potion)", description: /Unverified in-game.*Existing stack only/ });
    fireEvent.change(input, { target: { value: "99" } });
    fireEvent.blur(input);
    expect(screen.getByRole("textbox", { description: /Unverified in-game.*Existing stack only.*3/ })).toBe(input);
  });

  it("passes supporting text to nested value editors", () => {
    renderFields(<FieldRow node={{ ...parameters[0]!, warning: "Use the saved name", description: "Keep the existing format" }} />);
    expect(screen.getByRole("textbox", { name: "Optional name", description: /Use the saved name.*Keep the existing format/ })).toBeTruthy();
  });

  it("describes switches, enum selectors and flags groups", () => {
    renderFields(<><FieldRow node={{ ...parameters[2]!, description: "Known area bits" }} />
      <FieldRow node={{ ...node, valueId: "enabled", label: "Enabled", value: true, spec: { kind: "bool" } }} />
      <FieldRow node={{ ...node, valueId: "mode", label: "Mode", value: "normal", spec: { kind: "enum",
        options: [{ value: "normal", label: "Normal" }] } }} /></>);
    expect(screen.getByRole("group", { name: "Unlocked areas", description: "Known area bits" })).toBeTruthy();
    expect(screen.getByRole("switch", { name: "Enabled", description: /Unverified in-game/ })).toBeTruthy();
    expect(screen.getByRole("combobox", { name: "Mode", description: /Existing stack only/ })).toBeTruthy();
  });
});
