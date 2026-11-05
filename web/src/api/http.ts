import { runtime } from "../runtime";

export class ApiError extends Error {
  constructor(message: string, readonly status: number, readonly body?: unknown) {
    super(message);
  }
}

const REQUEST_TIMEOUT_MS = 60_000;
// Imports/exports may process hundreds of MiB after transfer completes.
export const TRANSFER_TIMEOUT_MS = 5 * 60_000;

export const networkMessage = "The in-browser editor runtime did not respond. Reload the page and try again.";
export const runtimeFailedMessage = "The editor runtime could not be loaded in this browser. Reload the page to try again.";
export const timeoutMessage = "The request timed out. Check the current state before retrying.";
export const cancelledMessage = "The request was cancelled. Check the current state before retrying.";

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
 * The deadline starts once the runtime has booted and includes consuming the body. Mutations are never replayed here.
 */
export async function fetchApi<T>(path: string, init: RequestInit | undefined,
  read: (response: Response) => Promise<T>, timeout = REQUEST_TIMEOUT_MS): Promise<T> {
  if (runtime.state().status !== "ready") await waitForRuntime(init?.signal);
  const controller = new AbortController();
  let timedOut = false;
  const cancel = () => controller.abort(init?.signal?.reason);
  if (init?.signal?.aborted) cancel();
  else init?.signal?.addEventListener("abort", cancel, { once: true });
  const timer = setTimeout(() => { timedOut = true; controller.abort(); }, timeout);
  try {
    const response = await runtime.fetch(`/api${path}`, { ...init, signal: controller.signal });
    return await read(response);
  } catch (error) {
    if (error instanceof ApiError) throw error;
    throw new ApiError(timedOut ? timeoutMessage : controller.signal.aborted ? cancelledMessage : networkMessage, 0);
  } finally {
    clearTimeout(timer);
    init?.signal?.removeEventListener("abort", cancel);
  }
}

async function waitForRuntime(signal: AbortSignal | null | undefined) {
  try {
    await runtime.ready(signal);
  } catch {
    throw new ApiError(signal?.aborted ? cancelledMessage : runtimeFailedMessage, 0);
  }
}

/** Permanent failures and cancellations need an explicit retry, rather than repeated requests. */
export function retryRead(failureCount: number, error: Error): boolean {
  if (error instanceof ApiError && (error.status < 500)) return false;
  return failureCount < 2;
}
