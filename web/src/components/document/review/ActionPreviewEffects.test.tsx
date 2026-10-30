import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { ActionPreviewChanges } from "./ActionPreviewChanges";
import type { ActionPreview, PlannedChange } from "../../../api/actionReview";
import type { FieldError } from "../../../api/protocol";
import type { usePlannedAction } from "./usePlannedAction";

const page = <T,>(items: T[]) => ({ items, page: 0, pages: 1, setPage: vi.fn() });
const action = (changes: PlannedChange[], errors: FieldError[] = []): ReturnType<typeof usePlannedAction> => ({
  preview: { revision: 1, planId: "p", scope: {}, plan: { changes, skippedRows: [], warnings: [], errors } } as ActionPreview,
  changesPage: page(changes), effectsPage: page(changes.filter(change => change.effect)), skippedPage: page([]),
} as unknown as ReturnType<typeof usePlannedAction>);

afterEach(() => { cleanup(); vi.restoreAllMocks(); });

it("renders exact absent/null effect evidence and copies the complete long payload", async () => {
  const long = "serial-" + "x".repeat(180);
  const writeText = vi.fn().mockResolvedValue(undefined);
  Object.assign(navigator, { clipboard: { writeText } });
  const change: PlannedChange = {
    valueId: "inventory/serial", label: "Serial", oldValue: null, newValue: long,
    derived: false, oldExists: false, newExists: true,
    effect: { kind: "StructuralMemento", spec: { kind: "text", maxLength: 512 },
      presentation: { confidence: "Inferred", warning: "Action-owned", description: "Raw serial evidence" },
      path: ["inventory", "item:0"], source: "fixture action" },
  };
  render(<ActionPreviewChanges action={action([change], [{ valueId: "", message: "one warning" }])} />);
  expect(screen.getByText("1 action effects · 1 total ordered rows · one whole-action undo")).toBeTruthy();
  expect(screen.getByRole("region", { name: "Action effects" }).tabIndex).toBe(0);
  expect(screen.getAllByText("(absent", { exact: false }).length).toBeGreaterThan(0);
  expect(screen.getByText(/StructuralMemento · inventory\/serial/)).toBeTruthy();
  expect(screen.getByText(/Inferred/)).toBeTruthy();
  expect(screen.getByText(/fixture action/)).toBeTruthy();
  expect(screen.getAllByText(`text: ${long}`).length).toBeGreaterThan(0);
  fireEvent.click(screen.getAllByRole("button", { name: "Copy exact" })[1]!);
  await waitFor(() => expect(writeText).toHaveBeenCalledWith(`text: ${long}`));
  expect(screen.getAllByRole("alert")).toHaveLength(1);
});

it("pages every ordered effect row without changing the ordinary change list", () => {
  const changes = Array.from({ length: 101 }, (_, index): PlannedChange => ({
    valueId: `effect-${index}`, label: `Effect ${index}`, oldValue: 0, newValue: 1,
    derived: false, oldExists: true, newExists: true,
    effect: { kind: "Observation", spec: { kind: "integer", min: "0", max: "9" } },
  }));
  const state = action(changes);
  state.effectsPage = { items: changes.slice(0, 100), page: 0, pages: 2, setPage: vi.fn() };
  render(<ActionPreviewChanges action={state} />);
  expect(screen.getByText("101 action effects · 101 total ordered rows · one whole-action undo")).toBeTruthy();
  expect(screen.getByRole("row", { name: /effect-0/ })).toBeTruthy();
  fireEvent.click(screen.getByRole("button", { name: "Next" }));
  expect(state.effectsPage.setPage).toHaveBeenCalledWith(1);
  expect(within(screen.getByRole("region", { name: "Preview changes" })).getAllByRole("row")).toHaveLength(102);
});

it("preserves nullable null, text, integer and union distinctions", () => {
  const changes: PlannedChange[] = [
    { valueId: "nullable", label: "Nullable", oldValue: null, newValue: "null", derived: false, oldExists: true, newExists: true,
      effect: { kind: "Observation", spec: { kind: "nullable", inner: { kind: "text" } } } },
    { valueId: "union", label: "Union", oldValue: { kind: "union", type: "count", value: "9007199254740993" }, newValue: { kind: "union", type: "count", value: "9007199254740994" }, derived: false, oldExists: true, newExists: true,
      effect: { kind: "Observation", spec: { kind: "union", options: [{ id: "count", label: "Count", spec: { kind: "integer", min: "0", max: "99999999999999999999" } }] } } },
  ];
  render(<ActionPreviewChanges action={action(changes)} />);
  expect(screen.getAllByText("null (explicit)").length).toBeGreaterThan(0);
  expect(screen.getAllByText('text: null').length).toBeGreaterThan(0);
  expect(screen.getAllByText("count: integer: 9007199254740993").length).toBeGreaterThan(0);
});

it("copies distinct structured union tags through the rendered exact-copy controls", async () => {
  const writeText = vi.fn().mockResolvedValue(undefined);
  Object.assign(navigator, { clipboard: { writeText } });
  const change: PlannedChange = {
    valueId: "opaque", label: "Opaque tags", derived: false, oldExists: true, newExists: true,
    oldValue: { kind: "union", type: "a", value: "text: x" },
    newValue: { kind: "union", type: "a: text", value: "x" },
    effect: { kind: "Observation", spec: { kind: "union", options: [
      { id: "a", label: "A", spec: { kind: "text" } },
      { id: "a: text", label: "Opaque", spec: { kind: "text" } },
    ] } },
  };
  render(<ActionPreviewChanges action={action([change])} />);
  expect(screen.getByText('a: text: text: x')).toBeTruthy();
  expect(screen.getByText('"a: text": text: x')).toBeTruthy();
  const buttons = screen.getAllByRole("button", { name: "Copy exact" });
  fireEvent.click(buttons[0]!);
  fireEvent.click(buttons[1]!);
  await waitFor(() => expect(writeText).toHaveBeenCalledTimes(2));
  expect(JSON.parse(writeText.mock.calls[0]![0] as string)).toEqual({ kind: "union", type: "a", value: { kind: "text", value: "text: x" } });
  expect(JSON.parse(writeText.mock.calls[1]![0] as string)).toEqual({ kind: "union", type: "a: text", value: { kind: "text", value: "x" } });
});
