// @vitest-environment node
import { expect, it, vi } from "vitest";
import { runSelectedSuites } from "./e2e-suite-runner.mjs";

const suites = [["canonical", ["canonical-test"]], ["unreal", ["real-plugin-test"]]];

it("attempts the second mandatory suite after an injected first process-launch error and stays failed", () => {
  const launch = vi.fn().mockReturnValueOnce({ error: new Error("spawn EAGAIN"), status: null })
    .mockReturnValueOnce({ status: 0 });
  const report = vi.fn();
  expect(runSelectedSuites(suites, launch, report)).toBe(1);
  expect(launch.mock.calls).toEqual([["canonical"], ["unreal"]]);
  expect(report).toHaveBeenCalledExactlyOnceWith("E2E suite 'canonical' could not launch: spawn EAGAIN");
});

it.each([1, 2, null])("attempts the next suite after process status %s without losing aggregate failure", status => {
  const launch = vi.fn().mockReturnValueOnce({ status }).mockReturnValueOnce({ status: 0 });
  expect(runSelectedSuites(suites, launch, vi.fn())).toBe(status || 1);
  expect(launch.mock.calls).toEqual([["canonical"], ["unreal"]]);
});

it("also contains a thrown launch exception to its suite", () => {
  const launch = vi.fn().mockImplementationOnce(() => { throw new Error("launch failed"); })
    .mockReturnValueOnce({ status: 0 });
  expect(runSelectedSuites(suites, launch, vi.fn())).toBe(1);
  expect(launch.mock.calls).toEqual([["canonical"], ["unreal"]]);
});

it("attempts later suites after a signaled process and a teardown failure", () => {
  const attempted = [];
  const launch = vi.fn().mockImplementation(suite => {
    attempted.push(suite);
    if (suite === "unreal") return { status: null, signal: "SIGTERM" };
    if (suite === "text-capacity") return { status: 1, signal: null };
    return { status: 0 };
  });
  expect(runSelectedSuites([
    ["canonical", ["canonical-id"]],
    ["unreal", ["unreal-id"]],
    ["text-capacity", ["capacity-id"]],
    ["ff7", ["ff7-id"]],
    ["transfer", ["transfer-id"]],
    ["caption", ["caption-id"]],
    ["action-effects", ["action-effects-id"]],
  ], launch, vi.fn())).toBe(1);
  expect(attempted).toEqual(["canonical", "unreal", "text-capacity", "ff7", "transfer", "caption", "action-effects"]);
});

it("keeps filtered empty suites unlaunched and succeeds for the selected suite", () => {
  const launch = vi.fn().mockReturnValue({ status: 0 });
  expect(runSelectedSuites([["canonical", []], suites[1]], launch, vi.fn())).toBe(0);
  expect(launch.mock.calls).toEqual([["unreal"]]);
});
