import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { FieldControl } from "./FieldControl";
import type { FieldSpec, FieldValue } from "../../api/protocol";

afterEach(cleanup);

it("keeps explicit-null capacity editable without a counter, including readonly inspection", async () => {
  const commit = vi.fn(() => true);
  const spec: FieldSpec = { kind: "text", maxUtf8Bytes: null };
  render(<>
    <FieldControl id="legacy-null" spec={spec} value="old" onCommit={commit} />
    <FieldControl id="readonly-null" spec={{ ...spec, readOnly: true }} value="🎮" onCommit={commit} />
  </>);
  const input = screen.getByRole("textbox");
  fireEvent.change(input, { target: { value: "🎮🎮" } });
  await act(async () => { fireEvent.blur(input); });
  expect(commit).toHaveBeenCalledExactlyOnceWith("🎮🎮");
  expect(input.getAttribute("aria-invalid")).toBe("false");
  expect(document.querySelector(".text-capacity")).toBeNull();
});

const nestedCases: { name: string; spec: FieldSpec; value: FieldValue }[] = [
  { name: "nullable", spec: { kind: "nullable", inner: { kind: "text", maxUtf8Bytes: 4 } }, value: "🎮" },
  { name: "union", spec: { kind: "union", options: [{ id: "caption", label: "Caption", spec: { kind: "text", maxUtf8Bytes: 4 } }] },
    value: { kind: "union", type: "caption", value: "🎮" } },
];

it.each(nestedCases)("keeps $name text hints when the document is disabled without exposing an editor", ({ spec, value }) => {
  const commit = vi.fn();
  render(<FieldControl id="inspect" spec={spec} value={value} disabled onCommit={commit} />);
  expect(document.getElementById("inspect-capacity")?.textContent).toBe("4 / 4 UTF-8 bytes used · 0 bytes remaining");
  expect(screen.queryByRole("textbox")).toBeNull();
  expect(screen.queryByRole("combobox")).toBeNull();
  expect(commit).not.toHaveBeenCalled();
});

it.each(nestedCases)("keeps $name hints when only the inner text is readonly", ({ spec, value }) => {
  if (spec.kind === "nullable") spec = { ...spec, inner: { ...spec.inner, readOnly: true } };
  if (spec.kind === "union") spec = { ...spec, options: spec.options.map(option => ({ ...option, spec: { ...option.spec, readOnly: true } })) };
  render(<FieldControl id="inner" spec={spec} value={value} onCommit={() => true} />);
  expect(document.getElementById("inner-value-capacity")?.textContent).toBe("4 / 4 UTF-8 bytes used · 0 bytes remaining");
  expect(screen.queryByRole("textbox")).toBeNull();
});

it.each(nestedCases)("keeps wrapper-readonly $name hints", ({ spec, value }) => {
  render(<FieldControl id="wrapper" spec={{ ...spec, readOnly: true }} value={value} onCommit={() => true} />);
  expect(document.getElementById("wrapper-capacity")?.textContent).toContain("4 / 4");
  expect(screen.queryByRole("combobox")).toBeNull();
});

it("does not invent a text projection for null values or unknown union variants", () => {
  render(<>
    <FieldControl id="absent" spec={nestedCases[0]!.spec} value={null} disabled onCommit={() => true} />
    <FieldControl id="unknown" spec={nestedCases[1]!.spec} value={{ kind: "union", type: "unknown", value: "🎮" }} disabled onCommit={() => true} />
  </>);
  expect(document.querySelector(".text-capacity")).toBeNull();
});

it.each([false, true])("updates accessible counters without truncating drafts (multiline %s)", async multiline => {
  const commit = vi.fn(() => true);
  render(<FieldControl id="caption" spec={{ kind: "text", maxUtf8Bytes: 4, multiline }} value="" describedBy="help" onCommit={commit} />);
  const input = screen.getByRole<HTMLInputElement>("textbox");
  expect(screen.getByText("0 / 4 UTF-8 bytes used · 4 bytes remaining").id).toBe("caption-capacity");
  expect(input.getAttribute("aria-describedby")).toBe("help caption-capacity");
  fireEvent.change(input, { target: { value: "🎮a" } });
  expect(input.value).toBe("🎮a");
  expect(screen.getByText("5 / 4 UTF-8 bytes used · 1 byte over capacity")).toBeTruthy();
  expect(input.getAttribute("aria-errormessage")).toBe("caption-capacity");
  await act(async () => { fireEvent.blur(input); });
  expect(commit).not.toHaveBeenCalled();
  fireEvent.change(input, { target: { value: "🎮" } });
  await act(async () => { fireEvent.blur(input); });
  expect(commit).toHaveBeenCalledExactlyOnceWith("🎮");
  fireEvent.keyDown(input, { key: "Escape" });
  expect(input.value).toBe("");
});

it("uses unique supplied field IDs, shows readonly capacities, and leaves legacy controls quiet", () => {
  render(<>
    <FieldControl id="one" spec={{ kind: "text", maxUtf8Bytes: 0 }} value="" onCommit={() => true} />
    <FieldControl id="two" spec={{ kind: "text", maxUtf8Bytes: 4, readOnly: true }} value="🎮" onCommit={() => true} />
    <FieldControl id="legacy" spec={{ kind: "text" }} value="plain" onCommit={() => true} />
  </>);
  expect(document.getElementById("one-capacity")?.textContent).toContain("0 / 0");
  expect(document.getElementById("two-capacity")?.textContent).toContain("4 / 4");
  expect(document.getElementById("legacy-capacity")).toBeNull();
  expect(screen.getAllByRole("textbox")).toHaveLength(2);
});

it("keeps compact table hints in one row with a full screen-reader description", () => {
  render(<FieldControl id="cell" spec={{ kind: "text", maxUtf8Bytes: 4 }} value="old" compact onCommit={() => true} />);
  expect(screen.getByText("3/4 B · 1 left").getAttribute("aria-hidden")).toBe("true");
  const counter = document.getElementById("cell-capacity")!;
  expect(counter.querySelector(".visually-hidden")?.textContent).toBe("3 / 4 UTF-8 bytes used · 1 byte remaining");
  expect(screen.getByRole("textbox").getAttribute("aria-describedby")).toBe(counter.id);
});
