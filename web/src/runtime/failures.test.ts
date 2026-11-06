import { expect, it } from "vitest";
import { describeError, isOutOfMemory, isRuntimeStopped } from "./failures";

it.each([
  new RangeError("Array buffer allocation failed"),
  new Error("System.OutOfMemoryException: Out of memory."),
  new Error("Aborted(OOM)"),
  "Cannot enlarge memory arrays to size 2147483648 bytes",
])("recognizes out-of-memory failures: %s", error => expect(isOutOfMemory(error)).toBe(true));

it.each([new TypeError("interop failure"), new RangeError("Invalid array length"), "Malformed response header."])(
  "leaves other failures alone: %s", error => expect(isOutOfMemory(error)).toBe(false));

it.each([
  new WebAssembly.RuntimeError("Aborted(OOM). Build with -sASSERTIONS for more info."),
  new WebAssembly.RuntimeError("memory access out of bounds"),
  new Error("Assert failed: .NET runtime already exited with 1 Aborted(OOM). You can use runtime.runMain() which doesn't exit the runtime."),
  // Deep managed recursion (plain, stackalloc or reflection, in a worker): .NET 10 WebAssembly exits
  // with this ExitStatus; requests made afterwards fail with the next entry.
  { name: "ExitStatus", message: "Program terminated with exit(1)", status: 1 },
  new Error("Assert failed: .NET runtime already exited with 1 [object Object]. You can use runtime.runMain() which doesn't exit the runtime."),
  "Aborted(native code called abort())",
  // A trap reported as text (a worker request error) or re-wrapped by another layer.
  "memory access out of bounds",
  "RuntimeError: memory access out of bounds",
  "unreachable",
  { name: "RuntimeError", message: "table index is out of bounds" },
  new Error("RuntimeError: unreachable executed"),
  // A trap re-wrapped by the .NET interop, with the managed stack after it.
  new Error("System.Runtime.InteropServices.JavaScript.JSException: unreachable\n   at Interop.Dispatch()"),
])("recognizes a runtime that stopped: %s", error => expect(isRuntimeStopped(error)).toBe(true));

// A JavaScript stack overflow (the worker's own code): managed recursion never surfaces as one.
it.each([new RangeError("Maximum call stack size exceeded"), new RangeError("Array buffer allocation failed"), new TypeError("interop failure"), new Error("System.OutOfMemoryException: Out of memory."), null,
  "System.DivideByZeroException: Attempted to divide by zero.", "Index was outside the bounds of the array.", "The save has unreachable records.",
  // .NET messages quoting save or preset content that happens to read like a trap or an abort.
  new Error("Preset schema 'RuntimeError: 3' is not supported."), "Invalid record 'memory access out of bounds'.",
  new Error("Unknown item 'Aborted(1)'."), "Preset schema 'unreachable' is not supported."])(
  "keeps the runtime for failures it survives: %s", error => expect(isRuntimeStopped(error)).toBe(false));

it.each([Object.create(null), new Proxy({}, { has: () => { throw new Error("trap"); }, getPrototypeOf: () => { throw new Error("trap"); } }),
  { name: "Error", get message(): string { throw new Error("getter"); } }, { toString: () => { throw new Error("toString"); } }])(
  "never throws, whatever was thrown: %#", error => {
    expect(isRuntimeStopped(error)).toBe(false);
    expect(isOutOfMemory(error)).toBe(false);
    expect(typeof describeError(error)).toBe("string");
  });
