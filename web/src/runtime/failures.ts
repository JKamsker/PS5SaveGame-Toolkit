/**
 * Recognizes a worker-side failure caused by exhausted memory: the 32-bit WebAssembly heap (.NET
 * `OutOfMemoryException`, an Emscripten OOM abort) or a JavaScript buffer allocation for a large upload.
 */
const outOfMemory = /out ?of ?memory|OutOfMemoryException|Aborted\(OOM\)|Cannot enlarge memory|allocation failed|could not allocate memory|Array buffer allocation/i;

export function isOutOfMemory(error: unknown): boolean {
  const text = error instanceof Error ? `${error.name}: ${error.message}` : String(error);
  return outOfMemory.test(text);
}

/**
 * Emscripten's abort (`Aborted(...)`), a WebAssembly trap or a .NET runtime that already exited: the
 * runtime cannot answer another request, and the workspaces in its memory are gone. Anything else (a
 * JavaScript error around a request, a failed buffer allocation for an upload) leaves it usable.
 */
const stopped = /\bAborted\(|program has already aborted|runtime already exited|^ExitStatus\b|Program terminated with exit\(/;

export function isRuntimeStopped(error: unknown): boolean {
  if (typeof WebAssembly === "object" && typeof WebAssembly.RuntimeError === "function" && error instanceof WebAssembly.RuntimeError) return true;
  // Emscripten's ExitStatus is a plain object with a name and a message, not an Error.
  const described = typeof error === "object" && error !== null && "message" in error;
  const text = described ? `${String((error as { name?: unknown }).name)}: ${String((error as { message: unknown }).message)}` : String(error);
  return stopped.test(text);
}
