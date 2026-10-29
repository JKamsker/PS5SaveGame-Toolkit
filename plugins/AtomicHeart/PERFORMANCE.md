# Atomic Heart performance evidence

## Current codec allocation (2026-10-29)

Implementation checked: `72cd997` (the merged caller-owned codec adoption).
`SlotDataDecoder.Decode` allocates one exact aggregate buffer, creates one
`OodleWorkspace` for the batch, and calls `OodleCodec.Decompress` with each
frame's destination slice. It no longer allocates a decoded result and copies
it into the aggregate for every frame. The aggregate bound, exact-consumption
codec checks, chunk-offset diagnostics and final record parse remain enabled.

The sample is the sole unique 126,214-byte PPSA10695 payload: 14 streams produce
1,751,391 decoded bytes. `AtomicHeartDecoderAllocationTests` exercises this
production path. It measures current-thread managed allocation and subtracts
an equivalent plain-array copy plus `RecordReader` pass, excluding aggregate and
record-index allocations. File reads, outer framing and comparison assertions
are outside both measured regions. After five warmups it takes the median of
three synchronous differences.

- Documentation audit, Release .NET 10 / SDK 10.0.400, tiered compilation
  disabled: **632,544 allocated bytes (0.63 MB, decimal)** in the focused check;
  **632,528 bytes** in the full-suite run, for the same whole batch.
- Merged adoption review run: **2,384,928 before → 632,544 bytes after**. The
  small variation between audit runs does not change the approximate result.
- Regression cap: **917,504 bytes** (14 × 64 KiB). The earlier approximately
  10.46 MB codec figure predates shared codec improvements and plugin adoption.

This is an allocation diagnostic, not a retained-heap, peak-memory, total Open
allocation or latency measurement. Workspace buffers may be pooled; the warm
measurement does not quantify their first-use allocation or retained capacity.
No timing speedup or console compatibility follows from it.

Reproduce the focused allocation check from the repository root in PowerShell:

```powershell
$scratch='D:/Temp/saveeditor/test-scratch/docs/accuracy-atomicheart'
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
$env:TEMP=$scratch
$env:TMP=$scratch
$env:TMPDIR=$scratch
$env:SAVETOOLKIT_TEST_TEMP=$scratch
$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
$env:DOTNET_TieredCompilation='0'
dotnet test --project plugins/AtomicHeart/SaveToolkit.Plugins.AtomicHeart.Tests --configuration Release --filter-class '*AtomicHeartDecoderAllocationTests' --output Detailed --results-directory $scratch
```

Without the private sample this check skips; a skip is not allocation evidence.
The documentation audit ran the complete plugin suite with the same sample and
runtime settings: **214 passed, 0 failed, 0 skipped**. API Release build with
`--warnaserror`: **0 warnings, 0 errors**. Documentation checks: **14 passed**.

## Regression coverage

Record inspection retains short member segments and materializes full leaf IDs
only on expansion while checking the full identity budget at Open. Record queries
cache one ordered result and a copied filter snapshot; returned nodes are fresh.
Inventory descriptions refresh after reference edits/undo; numeric matching uses
stack-formatted spans. Selected plans resolve native row ordinals. Inventory
queries still filter/sort the complete live model per request.

The suite gates allocations or lookup counts, never elapsed time. It also checks
codec error messages/offsets/inner exceptions, malformed input, byte-identical
no-op/complete undo, changed-output validation and isolated returned buffers.
Serialization caches one validated edited output. New changed output still
reparses/decompresses and compares all intended bytes; repeated exports reparse
outer integrity and return fresh copies without another decode.

The diagnostics below are historical results from earlier implementation stages.
In particular, their Open, inspector and changed-export figures were not rerun
after workspace adoption and must not be presented as current performance.

## Historical query/inspection review (2026-10-29, before workspace adoption)

Record inspection retains short member segments and constructs full leaf IDs only
when expanded. The same complete identity-length budget is still checked at Open.
The immutable record explorer retains one last ordered query result and a copied
filter snapshot; page nodes remain fresh. Combined class labels are reused.
Inventory queries retain one current asset description per slot, refresh it after
asset edits/undo, and match live numeric values using stack-formatted spans.
Selected action plans resolve native row ordinals without scanning unselected slots.
The inventory query still filters/sorts the complete live model on each request.

Historical harness and baseline source snapshot (outside the repository; availability is not guaranteed): `D:/Temp/saveeditor/review-results/perf-atomicheart/`.
The review used `./run.ps1 -Baseline -Label before` and `./run.ps1 -Label after` there.
Release/.NET 10.0.11, SDK 10.0.400, tiered compilation disabled, five warmups
and 21 synchronous measurements per operation. Medians use Stopwatch and
[`GC.GetAllocatedBytesForCurrentThread`](https://learn.microsoft.com/en-us/dotnet/api/system.gc.getallocatedbytesforcurrentthread?view=net-10.0);
managed allocations exclude native memory and are not retained-heap sizes.
Import and pack setup are excluded. Largest/sole unique snapshot: 126,214 bytes,
1,751,391 decoded bytes, 14 chunks, 4,063 objects. Pages request 20 rows.

| Operation | Before → after median ms | Before → after allocated bytes |
| --- | --- | --- |
| Open pinned + first root | 43.4325 → 46.9232 | 34,988,656 → 29,192,544 |
| First root on prepared fresh document | 0.0192 → 0.0168 | 11,104 → 9,560 |
| Inspector construction | 9.0620 → 3.3976 | 9,136,480 → 3,340,504 |
| First class-sorted record page | 0.4824 → 2.3756 | 684,408 → 496,728 |
| Subsequent record page / search / class sort | 0.1428 / 0.4238 / 0.4126 → 0.0056 / 0.0035 / 0.0056 | 213,960 / 13,072 / 684,264 → 17,648 / 11,088 / 17,872 |
| First inventory search | 0.0591 → 0.0538 | 136,208 → 148,112 |
| Subsequent inventory page / search / amount sort | 0.0718 / 0.0864 / 0.0712 → 0.0431 / 0.0479 / 0.0456 | 157,288 / 137,072 / 157,288 → 158,056 / 132,680 / 158,056 |
| One selected retail quantity preview | 0.0179 → 0.0004 | 9,144 → 1,360 |
| Authored 10,000-slot search / one-row preview | 4.3903 / 0.9822 → 1.5468 / 0.0005 | 5,021,184 / 674,368 → 8,528 / 912 |

Tradeoffs: the first record query sorts and retains its complete result instead
of partially sorting one page; subsequent matching pages reuse it. First inventory
search allocates descriptions which remain bounded to the slot count. Rich inventory
page rendering and whole-document previews have no meaningful allocation improvement.
Open allocation falls about 16.6%; total Open latency varied with machine load,
including unchanged codec/parser phases, so no total-Open speedup is claimed.
These are warm-process diagnostics, not cold-start or console benchmarks.

All 15 added sample-free cases pass; seven performance checks failed against the
corresponding pre-fix code. Regression gates assert allocation bounds or catalogue
lookup counts, never elapsed time. Full Release suite: **194 passed, zero failed
or skipped**; API Release build: **zero warnings/errors**. Shared cursor/FString,
robustness, byte-identical no-op, write/reopen, stale-plan and undo tests remain green.

## Merged workspace adoption measurement (2026-10-29, #40)

`SlotDataDecoder` retains its aggregate bound and exact plain buffer, and reuses
one disposable `OodleWorkspace` across all frames. Each frame decodes into its
exact destination slice; contextual errors and final aggregate validation remain.
The codec guarantees exact output or throws, so no separate result-length check
or per-frame result allocation/copy is needed.

The allocation regression exercises the production decoder on the 14-stream,
1,751,391-byte sample. It subtracts an identical plain-array copy and record-parser
pass to exclude aggregate/index allocations; reads, framing and assertions are
outside measurement. With five warmups and the median of three synchronous
measurements (Release, .NET 10, tiered compilation disabled), managed codec
allocations fell from **2,384,928** to **632,544 bytes** against a **917,504-byte**
cap. The earlier codec review's 10.5 MB baseline predates the shared codec changes.
This is allocation evidence, not a latency or retained-memory claim. Exact error
messages, chunk offsets and inner exception types are pinned by sample-free tests.

## Historical completion export diagnostic (2026-10-29)

One run on the sole unique sample measured a first changed export at about
135.7 ms / 29.1 MB allocated, and ten repeated validated exports at about
0.57 ms / 0.50 MB allocated per export. Assertions and returned-buffer copies
are included. This predates codec changes; it is not a current baseline or a
cross-machine benchmark. No timing thresholds are enforced.

## Historical asset first-load diagnostics (2026-10-29)

First-load diagnostic, Release .NET 10, largest/sole unique 126,214-byte sample, three alternating fresh-process runs before/after; no timing assertions or speedup claim. The before project was reconstructed from the original tracked plugin in a temporary folder, using the same SDK/Core/codec dependencies. The stopwatch covers `ReferencePackStore` construction/pinning, plugin Open, BuildView and the first complete 90-slot inventory query; current-thread allocations use `GC.GetAllocatedBytesForCurrentThread`. Sample loading happens before the stopwatch. Medians:

- Cold pack + Open + view + inventory: **274.94 ms / 37,834,744 allocated bytes before**, **250.69 ms / 40,119,880 bytes after**.
- Reusing the pinned pack, Open + view + inventory: **113.33 ms / 36,987,624 bytes before**, **97.75 ms / 37,079,032 bytes after**.

Timings vary with JIT and scheduling. The new cold allocation cost is about 2.29 MB; repeat-open overhead is about 91 KB. The reference guide does not materialize rows until expanded. Hash/type/rights resolution, duplicate rejection, live icon replacement/undo, reference-only joins, unchanged Amount bounds, byte-identical no-op/undo, unknown spatial state and missing-pack fallbacks are covered by tests.
