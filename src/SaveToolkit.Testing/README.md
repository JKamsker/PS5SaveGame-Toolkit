# Allocation guards

Allocation budgets in this repository are calibrated for **Release** builds. Every
`AllocationProbeResult` assertion enforces its unchanged bound in Release. In Debug,
the same assertion writes every sample, the minimum and the bound to xUnit test
output without enforcing the byte bound. Correctness and call-count assertions
still run normally. Debug code can allocate substantially more than Release code;
a multiplier would hide that distinction and require a second set of budgets.

`AllocationProbe.Measure` runs synchronously on the calling thread. By default it
warms the complete operation once, measures it three times and returns the minimum
along with every ordered sample. This keeps one-off tiered/OSR JIT work from
deciding an allocation guard. It measures managed allocations on that thread only;
do not pass asynchronous operations or dispatch measured work to other threads.

```csharp
var allocation = AllocationProbe.Measure(() => document.Query(id, query), out var page);
allocation.AssertLessThan(PageBudget);
Assert.Equal(PageSize, page.Rows.Count);
```

Use `beforeRun` and `afterRun` for setup and cleanup outside each counter interval,
including warm-ups. Reset counters per invocation, and use fresh fixtures when
measuring first views, first searches or mutations so a warmed document cache or
already-applied change cannot make the measured operation disappear. `MeasureOpen`
handles fresh disposable documents and leaves the final document with the caller.
The generic `Measure` overload returns the last result, independently of which
sample was smallest. `TransformSamples` supports per-operation normalization or
subtracting a separately measured baseline without discarding samples.

For an operation repeated in a loop, derive the total bound from a per-operation
budget instead of requiring a tiny absolute total across the entire loop. For
example, `allocation.AssertLessThan(iterations * BytesPerOperationBudget)` can
require less than one byte per cached lookup while tolerating a few KB of
one-off tiered/OSR JIT work. A per-call string would still exceed that budget by
orders of magnitude. Multiply the budget by the invocation count rather than
rounding fractional bytes per operation down. Keep existing Release-calibrated
bounds when migrating guards; changing a budget needs separate evidence.

`collectBetweenRuns: true` performs collection, waits for pending finalizers and
collects again before each invocation. It is optional and disabled by default.
Allocation guard classes use a non-parallel xUnit collection (the shared
`AllocationTestCollection`, or their existing plugin-specific collection) to reduce
interference from other tests. No elapsed-time thresholds are enforced.
