import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { deferred } from "../../test/deferred";
import { FieldControl } from "./FieldControl";
import type { FieldSpec, FieldValue } from "../../api/protocol";

afterEach(cleanup);

it("submits an unacknowledged draft once across Enter and blur, allowing explicit retry", async () => {
  const pending = deferred<boolean>();
  const commit = vi.fn(() => pending.promise);
  render(<FieldControl id="f" spec={{ kind: "text" }} value="old" onCommit={commit} />);
  const input = screen.getByRole("textbox");
  fireEvent.change(input, { target: { value: "new" } });
  fireEvent.keyDown(input, { key: "Enter" });
  fireEvent.blur(input);
  expect(commit).toHaveBeenCalledExactlyOnceWith("new");
  await act(async () => { pending.resolve(false); await pending.promise; });
  fireEvent.keyDown(input, { key: "Enter" });
  expect(commit).toHaveBeenCalledTimes(2);
});

it("does not let an earlier rejection invalidate a newer acknowledged draft", async () => {
  const first = deferred<boolean>();
  const second = deferred<boolean>();
  const commit = vi.fn().mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise);
  render(<FieldControl id="f" spec={{ kind: "text" }} value="old" onCommit={commit} />);
  const input = screen.getByRole("textbox");
  fireEvent.change(input, { target: { value: "first" } });
  fireEvent.keyDown(input, { key: "Enter" });
  fireEvent.change(input, { target: { value: "second" } });
  fireEvent.keyDown(input, { key: "Enter" });
  await act(async () => { second.resolve(true); await second.promise; });
  await act(async () => { first.resolve(false); await first.promise; });
  expect(input.getAttribute("aria-invalid")).toBe("false");
});

it("does not revalidate text changed after submitting an earlier draft", async () => {
  const pending = deferred<boolean>();
  render(<FieldControl id="f" spec={{ kind: "text" }} value="old" onCommit={() => pending.promise} />);
  const input = screen.getByRole<HTMLInputElement>("textbox");
  fireEvent.change(input, { target: { value: "submitted" } });
  fireEvent.keyDown(input, { key: "Enter" });
  fireEvent.change(input, { target: { value: "still typing" } });
  await act(async () => { pending.resolve(false); await pending.promise; });
  expect(input.value).toBe("still typing");
  expect(input.getAttribute("aria-invalid")).toBe("false");
});

it("retains custom enum text typed while an earlier selection is acknowledged", async () => {
  const pending = deferred<boolean>();
  const commit = vi.fn(() => pending.promise);
  const spec: FieldSpec = { kind: "enum", allowCustom: true, options: [{ value: "base", label: "Base" }] };
  const { rerender } = render(<FieldControl id="f" spec={spec} value="base" onCommit={commit} />);
  const input = screen.getByRole<HTMLInputElement>("combobox");
  fireEvent.change(input, { target: { value: "first" } });
  fireEvent.keyDown(input, { key: "Enter" });
  fireEvent.change(input, { target: { value: "still typing" } });
  await act(async () => { pending.resolve(true); await pending.promise; });
  rerender(<FieldControl id="f" spec={spec} value="first" onCommit={commit} />);
  expect(input.value).toBe("still typing");
});

it.each<{ spec: FieldSpec; value: FieldValue; next: string }>([
  { spec: { kind: "nullable", inner: { kind: "text" } }, value: "text", next: "null" },
  { spec: { kind: "union", options: [{ id: "a", label: "A", spec: { kind: "text" } }, { id: "b", label: "B", spec: { kind: "text" } }] },
    value: { kind: "union", type: "a", value: "text" }, next: "b" },
])("keeps the $spec.kind controls inert while a type change is pending", async ({ spec, value, next }) => {
  const pending = deferred<boolean>(); const commit = vi.fn(() => pending.promise);
  render(<FieldControl id="f" spec={spec} value={value} onCommit={commit} />);
  const select = screen.getByRole<HTMLSelectElement>("combobox");
  act(() => { fireEvent.change(select, { target: { value: next } }); fireEvent.change(select, { target: { value: next } }); });
  expect(commit).toHaveBeenCalledOnce();
  expect(select.disabled).toBe(true);
  expect(select.closest("[inert]")).not.toBeNull();
  await act(async () => { pending.resolve(true); await pending.promise; });
  expect(select.disabled).toBe(false);
});
