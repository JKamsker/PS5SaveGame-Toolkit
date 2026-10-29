import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { useState } from "react";
import type { FieldSpec, FieldValue } from "../../api/protocol";
import { DocumentContext, DocumentStore } from "../../state/documentStore";
import { displayValue, parseDraft, sameValue, toDraft } from "../../lib/values";
import { FieldControl } from "./FieldControl";
import { ActionButton } from "../document/ActionButton";

afterEach(cleanup);

describe("failed edit drafts", () => {
  it("keeps a rejected draft and its error when a refreshed snapshot replaces an equivalent spec", async () => {
    const commit = vi.fn(async () => false);
    const spec = { kind: "integer" as const, min: "0", max: "99" };
    const { rerender } = render(<FieldControl id="field" spec={spec} value="2" onCommit={commit} />);
    const input = screen.getByRole<HTMLInputElement>("textbox");
    fireEvent.change(input, { target: { value: "9" } });
    fireEvent.keyDown(input, { key: "Enter" });
    await waitFor(() => expect(input.getAttribute("aria-invalid")).toBe("true"));
    rerender(<FieldControl id="field" spec={{ ...spec }} value="3" onCommit={commit} />);
    expect(input.value).toBe("9");
    expect(input.getAttribute("aria-invalid")).toBe("true");
    fireEvent.keyDown(input, { key: "Escape" });
    expect(input.value).toBe("3");
  });

  it("keeps text entered while an earlier commit is in flight", async () => {
    let finish!: (accepted: boolean) => void;
    const spec = { kind: "text" as const };
    const commit = vi.fn(() => new Promise<boolean>(resolve => { finish = resolve; }));
    const { rerender } = render(<FieldControl id="field" spec={spec} value="old" onCommit={commit} />);
    const input = screen.getByRole<HTMLInputElement>("textbox");
    fireEvent.change(input, { target: { value: "first" } });
    fireEvent.keyDown(input, { key: "Enter" });
    fireEvent.change(input, { target: { value: "newer draft" } });
    rerender(<FieldControl id="field" spec={spec} value="first" onCommit={commit} />);
    await act(async () => finish(true));
    expect(input.value).toBe("newer draft");
    expect(commit).toHaveBeenCalledExactlyOnceWith("first");
  });
});

function LiveControl({ spec, initial, commit = () => true }: { spec: FieldSpec; initial: FieldValue; commit?: (v: FieldValue) => boolean }) {
  const [value, setValue] = useState(initial);
  const [error, setError] = useState<string>();
  return <><FieldControl id="field" spec={spec} value={value} onLocalError={setError}
    onCommit={(v) => { const accepted = commit(v); if (accepted) setValue(v); return accepted; }} />
    {error && <span role="alert">{error}</span>}</>;
}

describe("exact field protocol", () => {
  it.each(["12345678901234567890", "1.2345678901234567", "-0.000E+002", "1e1000000", "1e-1000000"])("keeps %s exact through editing and JSON", (text) => {
    const spec = { kind: "exactNumber" as const };
    const onCommit = vi.fn(() => true);
    render(<FieldControl id="number" spec={spec} value={{ kind: "exactNumber", text: "0" }} onCommit={onCommit} />);
    fireEvent.change(screen.getByRole("textbox"), { target: { value: text } });
    fireEvent.keyDown(screen.getByRole("textbox"), { key: "Enter" });
    const value: FieldValue = { kind: "exactNumber", text };
    expect(onCommit).toHaveBeenCalledWith(value);
    const reopened = JSON.parse(JSON.stringify(value));
    expect(toDraft(spec, reopened)).toBe(text);
    expect(displayValue(spec, reopened)).toBe(text);
    expect(sameValue(value, reopened)).toBe(true);
    expect(sameValue(value, text)).toBe(false);
  });

  it.each(["", "01", "+1", ".1", "1.", "1e", "NaN", "Infinity", " 1", "1\n", "١", "1e1000001", "1e-1000001"])("rejects invalid number %j with a field error", (text) => {
    const commit = vi.fn(() => true);
    render(<LiveControl spec={{ kind: "exactNumber" }} initial={{ kind: "exactNumber", text: "0" }} commit={commit} />);
    fireEvent.change(screen.getByRole("textbox"), { target: { value: text } });
    // HTML single-line inputs strip newlines; test that lexeme directly as well.
    if (text.includes("\n")) { expect(parseDraft({ kind: "exactNumber" }, text).ok).toBe(false); return; }
    fireEvent.blur(screen.getByRole("textbox"));
    expect(screen.getByRole("alert").textContent).toBeTruthy();
    expect(screen.getByRole("textbox").getAttribute("aria-invalid")).toBe("true");
    expect(commit).not.toHaveBeenCalled();
  });

  it("enforces exact bounds and size without exponent expansion", () => {
    const spec = { kind: "exactNumber" as const, min: "1.20000000000000001", max: "1.20000000000000002" };
    expect(parseDraft(spec, "1.200000000000000015").ok).toBe(true);
    expect(parseDraft(spec, "1.20000000000000003").ok).toBe(false);
    expect(parseDraft({ kind: "exactNumber", min: "-2e1000000", max: "-1e999999" }, "-1e1000000").ok).toBe(true);
    expect(parseDraft({ kind: "exactNumber", min: "0", max: "2e-1000000" }, "1e-1000000").ok).toBe(true);
    expect(parseDraft({ kind: "exactNumber" }, "1".repeat(4097)).ok).toBe(false);
    expect(parseDraft({ kind: "exactNumber" }, "1e" + "9".repeat(1000)).ok).toBe(false);
    expect(parseDraft({ kind: "exactNumber", min: "NaN" }, "1").ok).toBe(false);
  });

  it("recognises undo to an equal tagged value as unmodified", () => {
    const store = new DocumentStore({ canUndo: false, canRedo: false, isDirty: false });
    const value: FieldValue = { kind: "union", type: "number", value: { kind: "exactNumber", text: "1.00" } };
    store.seed("number", value);
    store.apply({ ok: true, changes: [{ valueId: "number", oldValue: value, newValue: JSON.parse(JSON.stringify(value)) }],
      errors: [], warnings: [], structureChanged: false, state: store.state });
    expect(store.isModified("number")).toBe(false);
  });
});

describe("nullable and union controls", () => {
  it("keeps explicit null in action parameter forms with a non-null default", () => {
    const store = new DocumentStore({ canUndo: false, canRedo: false, isDirty: false });
    const context = { workspaceId: "test", documentId: "test", canEdit: true, store, structureVersion: 0,
      commit: async () => true, acceptResult: async () => {}, runAction: vi.fn(), refresh: async () => {} };
    const { container } = render(<DocumentContext.Provider value={context}>
      <ActionButton node={{ type: "action", actionId: "run", label: "Run", parameters: [
        { type: "field", valueId: "optional", label: "Optional", spec: { kind: "nullable", inner: { kind: "text" } }, value: "default" },
      ] }} />
    </DocumentContext.Provider>);
    const selector = container.querySelector("select")!;
    fireEvent.change(selector, { target: { value: "null" } });
    expect(selector.value).toBe("null");
    expect(container.querySelector("input")).toBeNull();
  });

  it("initialises nullable bounded floats inside their range", () => {
    const commit = vi.fn(() => true);
    render(<LiveControl spec={{ kind: "nullable", inner: { kind: "float", min: 10 } }} initial={null} commit={commit} />);
    fireEvent.change(screen.getByRole("combobox"), { target: { value: "value" } });
    expect(commit).toHaveBeenLastCalledWith(10);
  });

  it("explicitly sets null, restores empty text and retains the previous text", async () => {
    const commit = vi.fn(() => true);
    render(<LiveControl spec={{ kind: "nullable", inner: { kind: "text" } }} initial={null} commit={commit} />);
    expect(screen.queryByRole("textbox")).toBeNull();
    fireEvent.change(screen.getByRole("combobox"), { target: { value: "value" } });
    expect(commit).toHaveBeenLastCalledWith("");
    fireEvent.change(screen.getByRole("textbox"), { target: { value: "hello" } });
    fireEvent.blur(screen.getByRole("textbox"));
    await waitFor(() => expect(commit).toHaveBeenLastCalledWith("hello"));
    fireEvent.change(screen.getByRole("combobox"), { target: { value: "null" } });
    expect(commit).toHaveBeenLastCalledWith(null);
    fireEvent.change(screen.getByRole("combobox"), { target: { value: "value" } });
    expect(commit).toHaveBeenLastCalledWith("hello");
  });

  it("retains explicit type choices without converting string to number", () => {
    const commit = vi.fn(() => true);
    const spec: FieldSpec = { kind: "union", options: [
      { id: "text", label: "String", spec: { kind: "text" } },
      { id: "number", label: "Number", spec: { kind: "exactNumber" } },
    ] };
    render(<LiveControl spec={spec} initial={{ kind: "union", type: "text", value: "12345678901234567890" }} commit={commit} />);
    fireEvent.change(screen.getByRole("combobox"), { target: { value: "number" } });
    expect(commit).toHaveBeenLastCalledWith({ kind: "union", type: "number", value: { kind: "exactNumber", text: "0" } });
    fireEvent.change(screen.getByRole("textbox"), { target: { value: "1.2345678901234567" } });
    fireEvent.blur(screen.getByRole("textbox"));
    expect(commit).toHaveBeenLastCalledWith({ kind: "union", type: "number", value: { kind: "exactNumber", text: "1.2345678901234567" } });
    fireEvent.change(screen.getByRole("combobox"), { target: { value: "text" } });
    expect(commit).toHaveBeenLastCalledWith({ kind: "union", type: "text", value: "12345678901234567890" });
  });

  it("keeps the chosen type when the server rejects a change", () => {
    render(<LiveControl spec={{ kind: "nullable", inner: { kind: "text" } }} initial="original" commit={() => false} />);
    fireEvent.change(screen.getByRole("combobox"), { target: { value: "null" } });
    expect((screen.getByRole("combobox") as HTMLSelectElement).value).toBe("value");
    expect((screen.getByRole("textbox") as HTMLInputElement).value).toBe("original");
  });
});

describe("enum controls", () => {
  it("allows custom IDs and displays option descriptions", () => {
    const commit = vi.fn(() => true);
    render(<LiveControl spec={{ kind: "enum", allowCustom: true, options: [{ value: "1", label: "Easy", description: "A known ID" }] }} initial="1" commit={commit} />);
    expect(screen.getByText("A known ID").textContent).toBeTruthy();
    fireEvent.change(screen.getByRole("combobox"), { target: { value: "unmapped-id" } });
    fireEvent.blur(screen.getByRole("combobox"));
    expect(commit).toHaveBeenCalledWith("unmapped-id");
  });

  it("searches long option lists and retains the unknown current value", () => {
    const options = Array.from({ length: 30 }, (_, i) => ({ value: String(i), label: `Item ${i}`, description: `Description ${i}` }));
    render(<LiveControl spec={{ kind: "enum", options }} initial="unknown" />);
    fireEvent.change(screen.getByRole("searchbox"), { target: { value: "Description 29" } });
    expect(screen.getAllByRole("option").map((o) => o.textContent)).toEqual(["unknown (unlisted)", "Item 29"]);
  });
});
