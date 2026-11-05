import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { ToastStack } from "./Toast";

afterEach(() => { cleanup(); vi.useRealTimers(); });

it("primes role-free announcement channels before inserting feedback and never announces removals", async () => {
  vi.useFakeTimers();
  const onDone = vi.fn();
  const view = render(<ToastStack messages={[]} onDone={onDone} />);
  const polite = view.container.querySelector('[aria-live="polite"]');
  const urgent = view.container.querySelector('[aria-live="assertive"]');
  expect(polite).not.toBeNull();
  expect(urgent).not.toBeNull();
  expect(polite?.textContent).toBe("");
  const messages = [{ id: 1, text: "Saved", isError: false }, { id: 2, text: "Failed", isError: true }];
  view.rerender(<ToastStack messages={messages} onDone={onDone} />);
  expect(polite?.textContent).toBe("");
  await act(async () => { await vi.advanceTimersByTimeAsync(100); });
  expect(polite?.textContent).toBe("Saved");
  expect(urgent?.textContent).toBe("Failed");
  expect(screen.getByRole("status").getAttribute("aria-live")).toBe("off");
  expect(screen.getByRole("alert").getAttribute("aria-live")).toBe("off");
  view.rerender(<ToastStack messages={[]} onDone={onDone} />);
  await act(async () => { await vi.advanceTimersByTimeAsync(100); });
  expect(view.container.querySelector('[aria-live="polite"]')).toBe(polite);
  expect(polite?.getAttribute("aria-relevant")).toBe("additions text");
  expect(screen.queryByRole("status")).toBeNull();
  expect(screen.queryByRole("alert")).toBeNull();
});

it("adds no live role to the page without feedback and gives each message its own role", () => {
  const onDone = vi.fn();
  const view = render(<><button>Editor</button><ToastStack messages={[]} onDone={onDone} /></>);
  // Page-level "is an error shown?" checks rely on alert/status meaning visible feedback.
  expect(screen.queryAllByRole("status")).toEqual([]);
  expect(screen.queryAllByRole("alert")).toEqual([]);
  expect(screen.queryByRole("region", { name: "Notifications" })).toBeNull();
  screen.getByRole("button").focus();
  view.rerender(<><button>Editor</button><ToastStack messages={[
    { id: 1, text: "Saved", isError: false }, { id: 2, text: "Failed", isError: true },
  ]} onDone={onDone} /></>);
  expect(screen.getAllByRole("status").map(element => element.textContent)).toEqual(["Saved"]);
  expect(screen.getAllByRole("alert").map(element => element.textContent)).toEqual(["Failed"]);
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

it("keeps a flooded stack readable while focused and resumes expiry on leaving", async () => {
  vi.useFakeTimers();
  const onDone = vi.fn();
  render(<ToastStack messages={[{ id: 1, text: "Warning", isError: false }]} onDone={onDone} />);
  const region = screen.getByRole("region", { name: "Notifications" });
  expect(region.tabIndex).toBe(0);
  fireEvent.focus(region);
  await act(async () => { await vi.advanceTimersByTimeAsync(10000); });
  expect(onDone).not.toHaveBeenCalled();
  fireEvent.blur(region);
  await act(async () => { await vi.advanceTimersByTimeAsync(4500); });
  expect(onDone).toHaveBeenCalledExactlyOnceWith(1);
});

it("still expires while the pointer rests on the stack, so it never permanently covers controls beneath", async () => {
  vi.useFakeTimers();
  const onDone = vi.fn();
  render(<ToastStack messages={[{ id: 1, text: "Warning", isError: false }]} onDone={onDone} />);
  fireEvent.mouseEnter(screen.getByRole("region", { name: "Notifications" }));
  await act(async () => { await vi.advanceTimersByTimeAsync(4500); });
  expect(onDone).toHaveBeenCalledExactlyOnceWith(1);
});
