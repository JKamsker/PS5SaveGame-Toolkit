interface QueuedRequest { start: () => void }

/** FIFO request limiter. Aborted waiters are removed before their network call can start. */
export class RequestQueue {
  private active = 0;
  private readonly pending: QueuedRequest[] = [];

  constructor(private readonly concurrency: number) {
    if (!Number.isInteger(concurrency) || concurrency < 1) throw new Error("Request concurrency must be a positive integer.");
  }

  run<T>(request: () => Promise<T>, signal: AbortSignal): Promise<T> {
    return new Promise<T>((resolve, reject) => {
      if (signal.aborted) { reject(signal.reason); return; }
      const cancel = () => {
        const index = this.pending.indexOf(entry);
        if (index !== -1) this.pending.splice(index, 1);
        reject(signal.reason);
      };
      const entry: QueuedRequest = {
        start: () => {
          signal.removeEventListener("abort", cancel);
          this.active++;
          // Hold the slot until the actual request settles, even if its observer is cancelled.
          Promise.resolve().then(() => {
            signal.throwIfAborted();
            return request();
          }).then(resolve, reject).finally(() => {
            this.active--;
            this.drain();
          });
        },
      };
      signal.addEventListener("abort", cancel, { once: true });
      this.pending.push(entry);
      this.drain();
    });
  }

  private drain() {
    while (this.active < this.concurrency && this.pending.length > 0) this.pending.shift()!.start();
  }
}
