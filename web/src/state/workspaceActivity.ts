import { createContext } from "react";

/** Exports must observe all writes accepted before their snapshot, including queued field edits. */
export class WorkspaceActivity {
  private readonly mutations = new Set<Promise<unknown>>();

  get hasPendingMutations(): boolean { return this.mutations.size > 0; }

  track<T>(mutation: Promise<T>): Promise<T> {
    this.mutations.add(mutation);
    void mutation.then(() => this.mutations.delete(mutation), () => this.mutations.delete(mutation));
    return mutation;
  }

  async waitForMutations(): Promise<void> {
    while (this.mutations.size > 0) await Promise.allSettled([...this.mutations]);
  }
}

export const WorkspaceActivityContext = createContext<WorkspaceActivity | null>(null);
