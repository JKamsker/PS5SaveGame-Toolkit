import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { runtime, type BootInfo, type RuntimeState } from "../../runtime";
import { RuntimeStatus, runtimeNotices } from "./RuntimeStatus";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });

const boot = (overrides: Partial<BootInfo>): BootInfo => ({
  version: "1", plugins: [], failedAssemblies: [], persistent: true, tabCoordination: true, elapsedMs: 0, initializeMs: 0, ...overrides,
});

it("warns when presets and packs will not persist, tabs cannot coordinate, or plugins failed to load", () => {
  expect(runtimeNotices(boot({}))).toEqual([]);
  expect(runtimeNotices(boot({ persistent: false, tabCoordination: false }))).toEqual([expect.stringMatching(/will not survive a reload/)]);
  expect(runtimeNotices(boot({ tabCoordination: false }))).toEqual([expect.stringMatching(/one tab at a time/)]);
  expect(runtimeNotices(boot({ failedAssemblies: ["SaveToolkit.Plugins.A", "SaveToolkit.Plugins.B"] })))
    .toEqual([expect.stringMatching(/could not be loaded.*SaveToolkit\.Plugins\.A, SaveToolkit\.Plugins\.B/)]);
});

it("shows the notices as a dismissible, non-blocking banner once the runtime is ready", () => {
  vi.spyOn(runtime, "state").mockReturnValue({ status: "ready", boot: boot({ persistent: false }) });
  render(<RuntimeStatus />);
  expect(screen.getByRole("status").textContent).toMatch(/will not survive a reload/);
  fireEvent.click(screen.getByRole("button", { name: "Dismiss" }));
  expect(screen.queryByRole("status")).toBeNull();
});

/** Drives RuntimeStatus through runtime states, as the worker's progress messages would. */
function controlledRuntime(initial: RuntimeState) {
  let current = initial;
  const listeners = new Set<() => void>();
  vi.spyOn(runtime, "state").mockImplementation(() => current);
  vi.spyOn(runtime, "subscribe").mockImplementation(listener => { listeners.add(listener); return () => listeners.delete(listener); });
  vi.spyOn(runtime, "ready").mockReturnValue(new Promise(() => undefined));
  return (next: RuntimeState) => act(() => { current = next; listeners.forEach(listener => listener()); });
}

const barScale = () => Number(/scaleX\(([\d.]+)\)/.exec(screen.getByRole("progressbar").querySelector("span")!.style.transform)![1]);

it("announces boot phases once while file counts and a never-receding bar update silently", () => {
  const emit = controlledRuntime({ status: "loading", loaded: 0, total: 0 });
  render(<RuntimeStatus />);
  const live = screen.getByRole("status");
  expect(live.getAttribute("aria-live")).toBe("polite");
  expect(live.textContent).toBe("Loading the editor runtime…");
  const scales: number[] = [];
  // The runtime discovers files while downloading: the total grows and loaded/total falls (11/18 -> 12/27).
  for (const [loaded, total] of [[1, 16], [11, 18], [12, 27], [30, 40], [78, 78]] as const) {
    emit({ status: "loading", loaded, total });
    expect(screen.getByRole("status")).toBe(live);
    expect(live.textContent).toBe("Loading the editor runtime…");
    expect(screen.getByText(`${loaded} of ${total} files`)).toBeTruthy();
    expect(screen.getByRole("progressbar").hasAttribute("aria-valuenow")).toBe(false);
    scales.push(barScale());
  }
  expect(scales).toEqual([...scales].sort((a, b) => a - b));
  expect(scales.at(-1)).toBe(1);
  emit({ status: "loading", loaded: 78, total: 78, starting: true });
  expect(live.textContent).toBe("Starting the editor and its game data…");
  expect(screen.getByRole("progressbar").getAttribute("aria-valuenow")).toBe("100");

  // The banner goes away, but the same live region says once that the editor is ready.
  emit({ status: "ready", boot: boot({}) });
  expect(screen.getByRole("status")).toBe(live);
  expect(live.textContent).toBe("The editor is ready.");
  expect(live.parentElement!.className).toBe("visually-hidden");
  expect(screen.queryByRole("progressbar")).toBeNull();
  expect(document.querySelector(".runtime-status")).toBeNull();
});

it("says nothing about readiness when the runtime was already ready", () => {
  controlledRuntime({ status: "ready", boot: boot({}) });
  render(<RuntimeStatus />);
  expect(screen.queryByRole("status")).toBeNull();
});

it.each([
  [false, /could not be loaded/],
  [true, /stopped unexpectedly.*were\s+lost/s],
])("tells a boot failure from a runtime that stopped after booting (crashed: %s)", (crashed, text) => {
  vi.spyOn(runtime, "state").mockReturnValue(crashed ? { status: "failed", message: "Aborted(OOM)", crashed } : { status: "failed", message: "Aborted(OOM)" });
  vi.spyOn(runtime, "ready").mockRejectedValue(new TypeError("Aborted(OOM)"));
  render(<RuntimeStatus />);
  expect(screen.getByRole("alert").textContent).toMatch(text);
  expect(screen.getByRole("button", { name: "Reload" })).toBeTruthy();
});
