import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { deferred } from "../../test/deferred";
import { FieldControl } from "./FieldControl";

afterEach(cleanup);

it("Escape restores the latest server value and retires a pending rejection", async () => {
  const pending = deferred<boolean>();
  const commit = vi.fn(() => pending.promise);
  const spec = { kind: "text" as const };
  const { rerender } = render(<FieldControl id="f" spec={spec} value="old" onCommit={commit} />);
  const input = screen.getByRole<HTMLInputElement>("textbox");
  fireEvent.change(input, { target: { value: "draft" } });
  fireEvent.keyDown(input, { key: "Enter" });
  rerender(<FieldControl id="f" spec={spec} value="latest" onCommit={commit} />);
  fireEvent.keyDown(input, { key: "Escape" });
  await act(async () => { pending.resolve(false); await pending.promise; });
  expect(input.value).toBe("latest");
  expect(input.getAttribute("aria-invalid")).toBe("false");
});

it("steps integer drafts within exact bounds and ignores steps beyond either limit", async () => {
  const commit = vi.fn(() => true);
  render(<FieldControl id="f" spec={{ kind: "integer", min: "9007199254740992", max: "9007199254740993" }} value="9007199254740992" onCommit={commit} />);
  const input = screen.getByRole<HTMLInputElement>("textbox");
  await act(async () => { fireEvent.keyDown(input, { key: "ArrowDown" }); });
  expect(commit).not.toHaveBeenCalled();
  await act(async () => { fireEvent.click(screen.getByRole("button", { name: "Increase" })); });
  expect(input.value).toBe("9007199254740993");
  expect(commit).toHaveBeenCalledExactlyOnceWith("9007199254740993");
  await act(async () => { fireEvent.keyDown(input, { key: "ArrowUp" }); });
  expect(commit).toHaveBeenCalledOnce();
});

it("multiline Enter keeps editing while blur commits and Escape restores the server text", async () => {
  const commit = vi.fn(() => true);
  render(<FieldControl id="f" spec={{ kind: "text", multiline: true }} value="original" onCommit={commit} />);
  const input = screen.getByRole<HTMLTextAreaElement>("textbox");
  fireEvent.change(input, { target: { value: "first\nsecond" } });
  fireEvent.keyDown(input, { key: "Enter" });
  expect(commit).not.toHaveBeenCalled();
  await act(async () => { fireEvent.blur(input); });
  expect(commit).toHaveBeenCalledExactlyOnceWith("first\nsecond");
  fireEvent.keyDown(input, { key: "Escape" });
  expect(input.value).toBe("original");
});
