import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { ToastStack } from "./Toast";

afterEach(() => { cleanup(); vi.useRealTimers(); });

it("primes non-atomic live regions before adding feedback without moving focus", () => {
  const onDone = vi.fn();
  const view = render(<><button>Editor</button><ToastStack messages={[]} onDone={onDone} /></>);
  const status = screen.getByRole("status");
  const alert = screen.getByRole("alert");
  expect(status.getAttribute("aria-atomic")).toBe("false");
  expect(alert.getAttribute("aria-atomic")).toBe("false");
  expect(status.getAttribute("aria-relevant")).toBe("additions");
  screen.getByRole("button").focus();
  view.rerender(<><button>Editor</button><ToastStack messages={[
    { id: 1, text: "Saved", isError: false }, { id: 2, text: "Failed", isError: true },
  ]} onDone={onDone} /></>);
  expect(screen.getByRole("status")).toBe(status);
  expect(screen.getByRole("alert")).toBe(alert);
  expect(status.textContent).toBe("Saved");
  expect(alert.textContent).toBe("Failed");
  expect(document.activeElement).toBe(screen.getByRole("button"));
});

it("cleans every timer when the stack unmounts", async () => {
  vi.useFakeTimers();
  const onDone = vi.fn();
  const view = render(<ToastStack messages={Array.from({ length: 30 }, (_, id) => ({ id, text: `Warning ${id}`, isError: false }))} onDone={onDone} />);
  view.unmount();
  await act(async () => { await vi.advanceTimersByTimeAsync(10000); });
  expect(onDone).not.toHaveBeenCalled();
  expect(vi.getTimerCount()).toBe(0);
});

it.each(["hover", "focus"])("keeps a flooded stack readable during %s and resumes expiry on leaving", async mode => {
  vi.useFakeTimers();
  const onDone = vi.fn();
  render(<ToastStack messages={[{ id: 1, text: "Warning", isError: false }]} onDone={onDone} />);
  const region = screen.getByRole("region", { name: "Notifications" });
  expect(region.tabIndex).toBe(0);
  if (mode === "hover") fireEvent.mouseEnter(region);
  else fireEvent.focus(region);
  await act(async () => { await vi.advanceTimersByTimeAsync(10000); });
  expect(onDone).not.toHaveBeenCalled();
  if (mode === "hover") fireEvent.mouseLeave(region);
  else fireEvent.blur(region);
  await act(async () => { await vi.advanceTimersByTimeAsync(4500); });
  expect(onDone).toHaveBeenCalledExactlyOnceWith(1);
});
