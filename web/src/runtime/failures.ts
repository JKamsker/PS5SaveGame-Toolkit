/**
 * Recognizes a worker-side failure caused by exhausted memory: the 32-bit WebAssembly heap (.NET
 * `OutOfMemoryException`, an Emscripten OOM abort) or a JavaScript buffer allocation for a large upload.
 */
const outOfMemory = /out ?of ?memory|OutOfMemoryException|Aborted\(OOM\)|Cannot enlarge memory|allocation failed|could not allocate memory|Array buffer allocation/i;

export function isOutOfMemory(error: unknown): boolean {
  return outOfMemory.test(errorText(error));
}

/**
 * Emscripten's abort (`Aborted(...)`), a WebAssembly trap or a .NET runtime that already exited: the
 * runtime cannot answer another request, and the workspaces in its memory are gone. Anything else (a
 * JavaScript error around a request, a failed buffer allocation for an upload) leaves it usable.
 * Deep managed recursion is such an exit: Mono detects the stack overflow and calls `exit(1)` (an
 * `ExitStatus`), so a `RangeError: Maximum call stack size exceeded` is a JavaScript-side overflow that
 * unwound only JavaScript frames, and does not stop the runtime.
 *
 * The text must start with the runtime's wording, after nothing but the prefixes the runtime and the
 * browser put before it: "Uncaught " (Chrome's `ErrorEvent.message`; Firefox: "uncaught exception: "),
 * the names of a trap, an abort, an exit or a re-wrapped JavaScript error ("Error: ", "RuntimeError: ",
 * "ExitStatus: ", the .NET interop's "JSException: ", an empty name) and the assertion prefixes of
 * dotnet.js ("Assert failed: ") and Emscripten ("Assertion failed: "). A .NET message may quote save or
 * preset content ("Preset schema 'RuntimeError: 3' is not supported.", "Path: Aborted(1) is not a valid
 * preset name."), and such a failure must not end the runtime.
 */
const errorName = String.raw`(?:Error|RuntimeError|ExitStatus|(?:System\.Runtime\.InteropServices\.JavaScript\.)?JSException)`;
const prefix = String.raw`^(?:Uncaught |uncaught exception: )?(?:${errorName}?: |Assert(?:ion)? failed: )*`;
const stopped = new RegExp(prefix + String.raw`(?:Aborted\(|program has already aborted|\.NET runtime already exited|ExitStatus\b|Program terminated with exit\()`);
/**
 * A WebAssembly trap that reached us as text or under another realm's RuntimeError (for example a request
 * error reported by the worker, or a trap re-wrapped by the .NET interop). The trap leaves the runtime's
 * memory and managed locks in an unknown state, so it is fatal even though the runtime may still answer.
 * Only V8's and SpiderMonkey's trap wording counts: a .NET DivideByZeroException must not end the runtime.
 */
const trapped = new RegExp(prefix + String.raw`(?:RuntimeError\b|memory access out of bounds|table index is out of bounds|unreachable executed|unreachable(?:$|\r?\n))`);

/** Never throws, whatever was thrown (a null-prototype object, a throwing getter or `instanceof`). */
export function isRuntimeStopped(error: unknown): boolean {
  if (isWebAssemblyTrap(error)) return true;
  const text = errorText(error);
  return stopped.test(text) || trapped.test(text);
}

function isWebAssemblyTrap(error: unknown): boolean {
  try {
    return typeof WebAssembly === "object" && typeof WebAssembly.RuntimeError === "function" && error instanceof WebAssembly.RuntimeError;
  } catch {
    return false; // a proxy whose prototype trap throws: judged by its text
  }
}

/**
 * `name: message` of an error-like value (Emscripten's ExitStatus is a plain object), its message alone
 * when it has no name (like an empty name), else its text; never throws.
 */
function errorText(error: unknown): string {
  try {
    if (typeof error !== "object" || error === null || !("message" in error)) return String(error);
    const { name, message } = error as { name?: unknown; message: unknown };
    return name === undefined ? String(message) : `${String(name)}: ${String(message)}`;
  } catch {
    return "";
  }
}

/**
 * The message to report for any thrown value; never throws. An error-like plain object (Emscripten's
 * `ExitStatus` has no `toString`, so String() would give "[object Object]") reports its message.
 */
export function describeError(error: unknown): string {
  try {
    if (typeof error === "object" && error !== null && "message" in error) {
      const { message } = error as { message: unknown };
      if (typeof message === "string") return message;
    }
    return String(error);
  } catch {
    return "The editor runtime failed with an error that cannot be described.";
  }
}
