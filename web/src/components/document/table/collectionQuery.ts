import type { CollectionQuery } from "../../../api/protocol";

export const DEFAULT_COLLECTION_PAGE_SIZE = 50;

export const collectionQueryKey = (workspaceId: string, documentId: string, collectionId: string,
  query: CollectionQuery, revision: number, version: number, structureVersion: number) =>
  ["collection", workspaceId, documentId, collectionId, query, revision, version, structureVersion] as const;
