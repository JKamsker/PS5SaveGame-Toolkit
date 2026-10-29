import { expect, it, vi } from "vitest";
import { installDialogStub } from "./dialog";

it("restores existing descriptors and leaves unrequested dialog methods alone", () => {
  const original = Object.getOwnPropertyDescriptor(HTMLDialogElement.prototype, "showModal");
  const close = Object.getOwnPropertyDescriptor(HTMLDialogElement.prototype, "close");
  const restore = installDialogStub({ methods: ["showModal"] });
  const dialog = document.createElement("dialog");
  dialog.showModal();
  expect(dialog.open).toBe(true);
  expect(Object.getOwnPropertyDescriptor(HTMLDialogElement.prototype, "close")).toEqual(close);
  restore();
  expect(Object.getOwnPropertyDescriptor(HTMLDialogElement.prototype, "showModal")).toEqual(original);
});

it.each([false, true])("makes close notifications explicit (dispatch=%s)", dispatchCloseEvent => {
  const restore = installDialogStub({ dispatchCloseEvent });
  try {
    const dialog = document.createElement("dialog");
    const closed = vi.fn();
    dialog.addEventListener("close", closed);
    dialog.showModal();
    dialog.close();
    expect(dialog.open).toBe(false);
    expect(closed).toHaveBeenCalledTimes(dispatchCloseEvent ? 1 : 0);
  } finally { restore(); }
});
