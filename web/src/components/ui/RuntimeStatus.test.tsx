import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { runtime, type BootInfo } from "../../runtime";
import { RuntimeStatus, runtimeNotices } from "./RuntimeStatus";

afterEach(() => vi.restoreAllMocks());

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
