import { runtime } from "../runtime";
// Not from "../runtime": component tests replace that module with a fetch-backed stand-in.
import { RuntimeRequestError } from "../runtime/client";
import { isOutOfMemory } from "../runtime/failures";

export class ApiError extends Error {
  constructor(message: string, readonly status: number, readonly body?: unknown) {
    super(message);
  }
}

const REQUEST_TIMEOUT_MS = 60_000;
// Imports/exports may process hundreds of MiB after transfer completes.
export const TRANSFER_TIMEOUT_MS = 5 * 60_000;
/**
 * Deadlines start when the worker begins a request; this only bounds the wait behind earlier requests
 * (several queued imports), so a runtime stuck on one request cannot leave the others pending forever.
 */
export const QUEUE_WAIT_LIMIT_MS = 30 * 60_000;

export const networkMessage = "The in-browser editor runtime did not respond. Reload the page and try again.";
export const runtimeFailedMessage = "The editor runtime could not be loaded in this browser. Reload the page to try again.";
export const runtimeCrashedMessage = "The in-browser editor runtime stopped unexpectedly, and the workspaces in this tab were lost with it. "
  + "Reload the page and import your saves again.";
export const runtimeCrashedOutOfMemoryMessage = "The in-browser editor ran out of memory and stopped, and the workspaces in this tab "
  + "were lost with it. Reload the page, then import fewer or smaller files at a time.";
export const runtimeStalledMessage = "The editor runtime stopped loading. Check your connection, then reload the page and try again.";
/** A boot that reports no progress for this long is treated as stalled; waiting requests fail instead of hanging. */
export const RUNTIME_STALL_LIMIT_MS = 90_000;
export const timeoutMessage = "The request timed out. Check the current state before retrying.";
export const cancelledMessage = "The request was cancelled. Check the current state before retrying.";
export const outOfMemoryMessage = "The in-browser editor ran out of memory. Import fewer or smaller files at a time and "
  + "close workspaces you no longer need. If the editor stops responding, reload the page.";
export const runtimeErrorMessage = (detail: string) =>
  `The in-browser editor runtime failed while handling the request${detail ? ` (${detail})` : ""}. `
  + "If it keeps happening, export your work and reload the page.";

/** Only use intentional API messages, never a proxy's HTML or a plain-text exception page. */
export function responseError(status: number, body: unknown): ApiError {
  let message = "";
  if (body && typeof body === "object") {
    const fields = body as Record<string, unknown>;
    const text = (key: string) => typeof fields[key] === "string" ? fields[key].trim() : "";
    const title = text("title");
    const detail = text("detail");
    message = text("error") || [title, detail === title ? "" : detail].filter(Boolean).join(": ");
  }
  return new ApiError(message || `Request failed (${status}). Please try again.`, status, body);
}

export async function readJson(response: Response): Promise<unknown> {
  if (!response.headers.get("content-type")?.includes("json")) return undefined;
  try {
    return await response.json();
  } catch (error) {
    // Abort/network errors during body transfer belong to the transport handler.
    if (!(error instanceof SyntaxError)) throw error;
    if (response.ok) throw new ApiError("The editor returned an unreadable response. Refresh the current view before retrying.", response.status);
    return undefined;
  }
}

/**
 * All `/api` traffic is answered by the in-browser runtime (see runtime/client.ts), never the network.
 * The deadline starts once the worker begins the request (time queued behind earlier requests does not
 * count, up to QUEUE_WAIT_LIMIT_MS) and includes consuming the body. Mutations are never replayed here.
 */
export async function fetchApi<T>(path: string, init: RequestInit | undefined,
  read: (response: Response) => Promise<T>, timeout = REQUEST_TIMEOUT_MS): Promise<T> {
  if (runtime.state().status !== "ready") await waitForRuntime(init?.signal);
  const controller = new AbortController();
  let timedOut = false;
  const cancel = () => controller.abort(init?.signal?.reason);
  if (init?.signal?.aborted) cancel();
  else init?.signal?.addEventListener("abort", cancel, { once: true });
  const expire = () => { timedOut = true; controller.abort(); };
  let timer = setTimeout(expire, QUEUE_WAIT_LIMIT_MS);
  const onStart = () => { clearTimeout(timer); timer = setTimeout(expire, timeout); };
  try {
    const response = await runtime.fetch(`/api${path}`, { ...init, signal: controller.signal }, { onStart });
    return await read(response);
  } catch (error) {
    if (error instanceof ApiError) throw error;
    if (timedOut) throw new ApiError(timeoutMessage, 0);
    if (controller.signal.aborted) throw new ApiError(cancelledMessage, 0);
    const failure = runtimeFailure(error);
    if (failure) throw new ApiError(failure, 0);
    if (error instanceof RuntimeRequestError) throw new ApiError(error.outOfMemory ? outOfMemoryMessage : runtimeErrorMessage(error.message), 0);
    throw new ApiError(networkMessage, 0);
  } finally {
    clearTimeout(timer);
    init?.signal?.removeEventListener("abort", cancel);
  }
}

/** The message for a runtime that is gone: it never booted, or it booted and then stopped (losing workspaces). */
function runtimeFailure(error?: unknown): string | undefined {
  const state = runtime.state();
  if (state.status !== "failed") return undefined;
  if (!state.crashed) return runtimeFailedMessage;
  // Requests sent after the crash learn the cause from the runtime state.
  return (error instanceof RuntimeRequestError && error.outOfMemory) || isOutOfMemory(state.message)
    ? runtimeCrashedOutOfMemoryMessage : runtimeCrashedMessage;
}

/** Waits for the boot; fails once it reports no progress for RUNTIME_STALL_LIMIT_MS instead of waiting forever. */
async function waitForRuntime(signal: AbortSignal | null | undefined) {
  const waiting = new AbortController();
  let stalled = false;
  const stall = () => { stalled = true; waiting.abort(); };
  let timer = setTimeout(stall, RUNTIME_STALL_LIMIT_MS);
  const unsubscribe = runtime.subscribe(() => { clearTimeout(timer); timer = setTimeout(stall, RUNTIME_STALL_LIMIT_MS); });
  const cancel = () => waiting.abort(signal?.reason);
  if (signal?.aborted) cancel();
  else signal?.addEventListener("abort", cancel, { once: true });
  try {
    await runtime.ready(waiting.signal);
  } catch {
    if (signal?.aborted) throw new ApiError(cancelledMessage, 0);
    throw new ApiError(stalled ? runtimeStalledMessage : runtimeFailure() ?? runtimeFailedMessage, 0);
  } finally {
    clearTimeout(timer);
    unsubscribe();
    signal?.removeEventListener("abort", cancel);
  }
}

/** Permanent failures and cancellations need an explicit retry, rather than repeated requests. */
export function retryRead(failureCount: number, error: Error): boolean {
  if (error instanceof ApiError && (error.status < 500)) return false;
  return failureCount < 2;
}
