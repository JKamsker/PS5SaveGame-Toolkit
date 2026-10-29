import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { deferred } from "../../test/deferred";
import { FieldControl } from "./FieldControl";

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
