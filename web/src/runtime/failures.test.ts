import { expect, it } from "vitest";
import { isOutOfMemory, isRuntimeStopped } from "./failures";

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
  { name: "ExitStatus", message: "Program terminated with exit(1)", status: 1 },
  "Aborted(native code called abort())",
  // A trap reported as text (a worker request error) or re-wrapped by another layer.
  "memory access out of bounds",
  "RuntimeError: memory access out of bounds",
  "unreachable",
  { name: "RuntimeError", message: "table index is out of bounds" },
  new Error("RuntimeError: unreachable executed"),
])("recognizes a runtime that stopped: %s", error => expect(isRuntimeStopped(error)).toBe(true));

it.each([new RangeError("Array buffer allocation failed"), new TypeError("interop failure"), new Error("System.OutOfMemoryException: Out of memory."), null,
  "System.DivideByZeroException: Attempted to divide by zero.", "Index was outside the bounds of the array.", "The save has unreachable records."])(
  "keeps the runtime for failures it survives: %s", error => expect(isRuntimeStopped(error)).toBe(false));
