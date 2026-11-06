/**
 * Recognizes a worker-side failure caused by exhausted memory: the 32-bit WebAssembly heap (.NET
 * `OutOfMemoryException`, an Emscripten OOM abort) or a JavaScript buffer allocation for a large upload.
 */
const outOfMemory = /out ?of ?memory|OutOfMemoryException|Aborted\(OOM\)|Cannot enlarge memory|allocation failed|could not allocate memory|Array buffer allocation/i;

export function isOutOfMemory(error: unknown): boolean {
  const text = error instanceof Error ? `${error.name}: ${error.message}` : String(error);
  return outOfMemory.test(text);
}
