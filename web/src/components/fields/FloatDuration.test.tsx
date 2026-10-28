import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { DocumentContext, DocumentStore } from "../../state/documentStore";
import { describeDuration, displayValue } from "../../lib/values";
import type { FloatSpec } from "../../api/protocol";
import { FieldRow } from "./FieldRow";

afterEach(cleanup);
const spec: FloatSpec = { kind: "float", format: "Duration", unitsPerSecond: 1000 };

it("formats fractional and negative durations, handles rollover, and falls back for invalid metadata", () => {
  expect(displayValue(spec, 3661250)).toBe("1 h 1 min 1.25 s");
  expect(displayValue({ kind: "float", format: "Duration" }, 61.125)).toBe("1 min 1.125 s");
  expect(displayValue(spec, -61250)).toBe("−1 min 1.25 s");
  expect(displayValue(spec, 59999.6)).toBe("1 min 0 s");
  expect(displayValue(spec, 0)).toBe("0 s");
  expect(displayValue({ ...spec, unitsPerSecond: 0 }, 42.5)).toBe("42.5");
  expect(describeDuration({ ...spec, unitsPerSecond: Infinity }, 42.5)).toBeNull();
  expect(describeDuration(spec, Infinity)).toBeNull();
  expect(describeDuration(spec, null)).toBeNull();
});

it.each([true, false])("renders duration hints and preserves raw numbers when editing (canEdit=%s)", async (canEdit) => {
  const store = new DocumentStore({ canUndo: false, canRedo: false, isDirty: false });
  const commit = vi.fn(async () => true);
  const context = { workspaceId: "w", documentId: "d", canEdit, store, structureVersion: 0,
    commit, acceptResult: async () => {}, runAction: vi.fn(), refresh: async () => {} };
  render(<DocumentContext.Provider value={context}><FieldRow node={{ type: "field", valueId: "timer", label: "Timer", spec, value: 61250 }} /></DocumentContext.Provider>);
  expect(screen.getAllByText("1 min 1.25 s").length).toBeGreaterThan(0);
  if (canEdit) {
    expect((screen.getByRole("textbox") as HTMLInputElement).value).toBe("61250");
    fireEvent.change(screen.getByRole("textbox"), { target: { value: "61250.125" } });
    fireEvent.keyDown(screen.getByRole("textbox"), { key: "Enter" });
    await waitFor(() => expect(commit).toHaveBeenCalledWith("timer", 61250.125));
  } else {
    expect(screen.queryByRole("textbox")).toBeNull();
    expect(commit).not.toHaveBeenCalled();
  }
});
