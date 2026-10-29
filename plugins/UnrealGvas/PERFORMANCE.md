# Performance and memory review — #40

Branch: `review/perf-unrealgvas`. Baseline: `0930e56`. Review date: 2026-10-29.
Only `plugins/UnrealGvas/` changes; no format, write eligibility or maturity changes.

## Findings and fixes

| Severity | Finding | Commit |
| --- | --- | --- |
| medium | Hogwarts frame decode grew a stream, copied it and copied again to remove its prefix. Validate every declaration first, then copy bounded decoded chunks into one exact-size database result. Both prefix lengths, SQLite magic, codec validation and total bounds remain enforced. | `a7cd053` |
| medium | Each F8 cell repeated catalogue lookup/attribution formatting, including raw columns, and every default page re-sorted the complete index. Cache immutable records/captions by distinct key, bypass them for raw columns, and retain ordinal default order once. Scalar values and explicit value sorts remain live. | `2538255` |
| low | Raw lazy parents eagerly built descendant pages and field bindings. Construct each child page only inside its loader. | `074b6f9` |
| low | Each 100-row Jedi summary rebuilt all 27,224 actor index entries. Build only visible entries; count the tree without creating paths/records. Full indexes also avoid unused ancestor arrays at leaves. | `22612dc` |

No confirmed findings remain deferred or reclassified as not a bug. Initial
findings and the additional measured summary finding were posted to issue #40
before their respective production fixes.

## Measurement method

The standalone .NET 10 console harness, project/build outputs and raw results are
retained on D:, under `D:/Temp/saveeditor/test-scratch/review-perf-unrealgvas/`.
`bench/Program.cs` and `bench/Bench.csproj` reference this worktree's plugin and
Testing projects. No samples, extracted data or benchmark executables are committed.

Both sides used Release, `DOTNET_TieredCompilation=0`, two warmups and seven
measurements per operation, forced GC between measurements, `Stopwatch`, and
`GC.GetAllocatedBytesForCurrentThread`. Reported times are operation medians;
the after range covers two separate process runs. Input bytes, in-memory save-file
construction, sample import and pinned reference-pack loading are outside timings.
Fresh-view/page tests create a new document outside the timed operation; the
combined open/view/page test includes all three operations and disposes afterward.
Repeated-page/search/summary tests reuse a document. No-op output is compared with
the original bytes in both benchmark runs.

The baseline comparison temporarily restored only the five changed production
files from `0930e56`, ran the identical harness, then restored the committed fixes
in a `finally` block. Initial measurements with normal tiered JIT are also retained,
but are not mixed into the table below.

Samples were selected as the largest successfully probed file per title, breaking
ties by ordinal relative path, rather than picking a convenient small save:

- Hogwarts `PPSA01603`, `HL0014/ue4savegame.ps5.sav`: 2,171,238 bytes.
  Its independent database images are 4,534,272 and 4,231,168 bytes, 84 tables each.
- Jedi `PPSA02199`, `SaveGame00/ue4savegame.dpx.sav`: 76,781 bytes.
  The largest collection is `jedi/actor-tags`, 27,224 rows.

## Results

MB below means decimal managed bytes / 1,000,000. Native SQLite allocations,
resident/peak process memory, HTTP serialization and browser rendering are **not**
included. Allocation results were consistent across repeats; wall times varied
considerably on the shared machine, including unchanged operations. These results
establish allocation reductions, not a reliable end-to-end latency improvement.

| Sample / operation | Before ms | After ms (two run medians) | Before MB | After MB |
| --- | ---: | ---: | ---: | ---: |
| Hogwarts envelope parse | 0.414 | 0.410–0.419 | 2.379 | 2.379 |
| Hogwarts main image framing/Kraken | 61.610 | 55.586–62.266 | 53.001 | 31.814 |
| Hogwarts exclusive image framing/Kraken | 53.549 | 49.449–66.927 | 50.722 | 29.844 |
| Hogwarts main SQLite open/integrity/schema | 13.508 | 13.943–15.905 | 9.593 | 9.593 |
| Hogwarts exclusive SQLite open/integrity/schema | 14.592 | 11.664–21.716 | 8.987 | 8.987 |
| Hogwarts plugin open | 152.359 | 139.895–141.251 | 127.612 | 85.545–85.547 |
| Hogwarts fresh BuildView | 1.196 | 1.158–1.250 | 0.783 | 0.783 |
| Hogwarts fresh first 50-row page | 0.390 | 0.362–0.376 | 0.293 | 0.293 |
| Hogwarts open + view + first page | 159.070 | 126.283–156.340 | 128.675 | 86.615 |
| Hogwarts absent-text query (4,496 world rows) | 22.388 | 14.158–22.354 | 0.028 | 0.028 |
| Hogwarts no-op Serialize | 0.766 | 0.749–1.289 | 6.918 | 6.918 |
| Jedi envelope parse | 0.028 | 0.019–0.036 | 0.089 | 0.089 |
| Jedi plugin open | 31.924 | 28.850–47.070 | 35.833 | 34.195 |
| Jedi fresh BuildView | 0.135 | 0.118–0.141 | 0.026 | 0.026 |
| Jedi fresh first 50-row actor page | 3.273 | 0.152–0.171 | 3.796 | 0.073 |
| Jedi repeated first actor page | 1.984 | 0.129–0.141 | 3.796 | 0.046 |
| Jedi open + view + first page | 33.173 | 40.853–42.900 | 39.655 | 34.294 |
| Jedi absent-text actor query | 67.957 | 16.661–18.555 | 89.296 | 1.551 |
| Jedi repeated decoded summary | 3.445 | 1.924–2.924 | 9.972 | 0.066 |
| Jedi no-op Serialize | 0.090 | 0.131–0.132 | 0.251 | 0.251 |

The combined Jedi latency did increase in these runs despite much faster isolated
first-page queries; open-only timings overlap the baseline. Do not attribute an
end-to-end speedup to this patch. Its repeatable benefit is lower transient managed
allocation. The default ordinal sort is now paid once during index construction.

Remaining measured costs are intentional: Hogwarts validates both images and
table identities before enabling edits; Jedi validates/decompresses the actor
stream before enabling gameplay edits. Serialization reparses output, including
no-op GVAS output. SQLite search scans full raw columns for correct complete-model
semantics. Removing those checks or changing framework codecs is outside these
fixes; no new defect is claimed for them.

## Regression evidence and validation

- Baseline suite: 152 passed, zero failed/skipped.
- Four initial regression cases failed against unchanged production code: allocation
  budget, decode-before-late-frame-rejection, repeated metadata lookups and eager
  descendant binding. The allocation fixture was corrected to use several bounded
  SQLite blobs, then confirmed failing for the intended allocation assertion.
- The additional largest-Jedi-summary test failed at 9,973,208 allocated bytes for
  100 rows before its fix.
- Four prefix-split controls (frame boundaries at decoded bytes 1, 7, 8 and 9)
  preserve acceptance of prefixes spanning independent frames.
- Final suite: 161 passed, zero failed/skipped. This includes all sample integrity,
  byte-identical no-op, write/reopen, undo, corruption, F8 and conformance coverage.
- Release API build: zero warnings/errors. Whitespace and scope checks pass.

Tests and builds export the brief's required `SAVETOOLKIT_ARTIFACTS`,
`SAVETOOLKIT_TEST_TEMP`, `TEMP` and `TMP` values. In-tree bin/obj outputs remain
permitted; benchmark/test scratch is on D:. Benchmark evidence is retained for
review rather than deleted; no cleanup was blocked.

Performance API references: Microsoft's
[MemoryStream.ToArray documentation](https://learn.microsoft.com/en-us/dotnet/api/system.io.memorystream.toarray?view=net-10.0)
documents its copy; [Enumerable.OrderBy documentation](https://learn.microsoft.com/en-us/dotnet/api/system.linq.enumerable.orderby?view=net-10.0)
documents deferred sorting. Existing README/Hogwarts format sources remain the
basis for framing and codec behavior; no new format hypothesis was introduced.
