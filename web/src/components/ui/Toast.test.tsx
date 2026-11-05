import { act, cleanup, render, screen } from "@testing-library/react";
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

it("cleans every timer when the stack unmounts", () => {
  vi.useFakeTimers();
  const onDone = vi.fn();
  const view = render(<ToastStack messages={Array.from({ length: 30 }, (_, id) => ({ id, text: `Warning ${id}`, isError: false }))} onDone={onDone} />);
  view.unmount();
  act(() => vi.advanceTimersByTime(10000));
  expect(onDone).not.toHaveBeenCalled();
  expect(vi.getTimerCount()).toBe(0);
});
