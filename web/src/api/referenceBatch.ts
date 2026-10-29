import { api, ApiError } from "./client";
import type { ReferenceCandidate, ReferenceRecordPair } from "./protocol";

// Match the documented API limit. No result cache: eligibility can change with
// the document revision, field binding, action owner or pinned catalog.
const MAX_PAIRS = 256;
const COALESCE_MS = 30;
interface Pending {
  pair: ReferenceRecordPair;
  cancelled: boolean;
  resolve: (candidate: ReferenceCandidate | undefined) => void;
  reject: (error: unknown) => void;
}
interface Batch { entries: Pending[]; timer?: ReturnType<typeof setTimeout> }
const batches = new Map<string, Batch>();

/** Coalesce cells mounted in the same render, including action parameters. */
export function queueReferenceRecord(workspaceId: string, documentId: string, revision: number,
  structureVersion: number, pair: ReferenceRecordPair) {
  const key = JSON.stringify([workspaceId, documentId, revision, structureVersion]);
  let entry!: Pending;
  const result = new Promise<ReferenceCandidate | undefined>((resolve, reject) => {
    entry = { pair, cancelled: false, resolve, reject };
  });
  let batch = batches.get(key);
  if (batch) clearTimeout(batch.timer);
  else { batch = { entries: [] }; batches.set(key, batch); }
  batch.entries.push(entry);
  const pending = batch;
  batch.timer = setTimeout(() => {
    batches.delete(key);
    const active = pending.entries.filter(item => !item.cancelled);
    for (let offset = 0; offset < active.length; offset += MAX_PAIRS) {
      const chunk = active.slice(offset, offset + MAX_PAIRS);
      void api.referenceRecords(workspaceId, documentId, chunk.map(item => item.pair), revision)
        .then(response => {
          if (response.revision !== revision || response.results.length !== chunk.length)
            throw new ApiError("Reference batch did not match the requested revision or pair count.", 0);
          if (response.results.some((found, index) => {
            const pair = chunk[index]!.pair;
            return found.fieldId !== pair.fieldId || found.recordKey !== pair.recordKey || found.actionId !== pair.actionId;
          })) throw new ApiError("Reference batch did not match the requested field identities.", 0);
          chunk.forEach((item, index) => {
            const found = response.results[index]!;
            if (item.cancelled) return;
            if (found.status === 200) item.resolve(found.candidate);
            else item.reject(new ApiError(found.error, found.status));
          });
        })
        .catch((error: unknown) => chunk.forEach(item => { if (!item.cancelled) item.reject(error); }));
    }
  }, COALESCE_MS);
  return { result, cancel: () => {
    entry.cancelled = true;
    entry.resolve(undefined);
    if (pending.entries.every(item => item.cancelled)) {
      clearTimeout(pending.timer);
      if (batches.get(key) === pending) batches.delete(key);
    }
  } };
}
