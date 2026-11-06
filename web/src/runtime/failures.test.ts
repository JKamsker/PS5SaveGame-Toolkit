import { expect, it } from "vitest";
import { isOutOfMemory } from "./failures";

it.each([
  new RangeError("Array buffer allocation failed"),
  new Error("System.OutOfMemoryException: Out of memory."),
  new Error("Aborted(OOM)"),
  "Cannot enlarge memory arrays to size 2147483648 bytes",
])("recognizes out-of-memory failures: %s", error => expect(isOutOfMemory(error)).toBe(true));

it.each([new TypeError("interop failure"), new RangeError("Invalid array length"), "Malformed response header."])(
  "leaves other failures alone: %s", error => expect(isOutOfMemory(error)).toBe(false));
