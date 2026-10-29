import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { FieldControl } from "./FieldControl";

afterEach(cleanup);

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
