// @vitest-environment node
import { expect, it, vi } from "vitest";
import { runSelectedSuites } from "./e2e-suite-runner.mjs";

const suites = [["canonical", ["canonical-id"]], ["unreal", ["unreal-id"]], ["ff7", ["ff7-id"]]];

it.each(["returned", "thrown"])("still launches real FF7 after a %s canonical launch error and Unreal failure", failure => {
  const attempted = [];
  const report = vi.fn();
  const code = runSelectedSuites(suites, suite => {
    attempted.push(suite);
    if (suite === "canonical") {
      const error = new Error("Authored process launch failure");
      if (failure === "thrown") throw error;
      return { error, status: null };
    }
    return { status: suite === "unreal" ? 2 : 0 };
  }, report);
  expect(attempted).toEqual(["canonical", "unreal", "ff7"]);
  expect(code).not.toBe(0);
  expect(report).toHaveBeenCalledTimes(1);
});

it("keeps a focused FF7 selection without launching empty complementary partitions", () => {
  const launch = vi.fn().mockReturnValue({ status: 0 });
  expect(runSelectedSuites([["canonical", []], ["unreal", []], suites[2]], launch, vi.fn())).toBe(0);
  expect(launch.mock.calls).toEqual([["ff7"]]);
});
