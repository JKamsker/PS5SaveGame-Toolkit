# Sample-backed data safety and performance review (#40)

Base: `8140888f`; branch: `review/samples-bg3-cp77`; date: 2026-11-05.

No BG3 production defect was reproduced. Same-container duplicate item references
are rejected by `LsmfInventory.Slots`, so the inventory graph cannot silently collapse
them on supported decoded input. Existing recursive-inventory history/identity and
NewAge boundary tests pass with real samples.

Coverage gap corrected: default conformance fuzz selected only the smallest
analytics LSF. This plugin now selects all six distinct formats, including the
largest LSPK. The authored CI seeds remain independent of private samples.
The final run completed **1,600 mutations** (six sample + two synthetic seeds,
200 each), zero known failures, 11.08 s parser work.

`ExhaustiveSampleReviewTests` opens **all 12 claimed primary/backup files**, walks
every lazy section and every 200-row collection page through DocumentSession,
checks live field snapshots, and verifies byte-identical no-op output.
For files with writable integers it applies one edit, reviews semantic changes,
exports/reopens, then checks byte-exact undo and redo.
Existing tests additionally cover LSF primitives, live scalar families, container
reorder previews/apply, full export and unrelated compressed-entry preservation.
The audit deliberately samples editing rather than changing all **197,464**
writable gameplay fields in one coupled transaction.

Isolated audit observations for the two gameplay copies:
- Open: 0.71–1.05 s, 118.9–119.3 MB managed allocation.
- Full host lazy tree: 10.20–12.22 s, 2.18–2.22 GB cumulative allocation.
- Every collection page: 0.49–0.88 s, 555–557 MB.
- Semantic review after an edit: 0.38–0.47 s, 94.2 MB.
- Edited export: 0.91–0.95 s, 252 MB.
- Each gameplay file publishes 235,289 distinct fields and 344,993 lazy sections.
  These costs alone do not establish super-linear growth; existing 50,000-slot
  graph and paging allocation regressions also pass. Full raw-tree traversal
  remains substantially more expensive than normal bounded browsing.

Baseline: **393 passed / 0 failed / 0 skipped** (76.19 s).
Final with samples: **394 / 0 / 0** (93.81 s, including the new exhaustive audit).
Without samples: **343 passed / 0 failed / 51 expected skips** (4.35 s).
Audit alone: **1 passed**, 40.23 s including runner overhead.
Experimental maturity, warnings and write scope are unchanged; console acceptance
and authoritative gameplay dependencies remain unproven.

## Reproduction

Run from the repository root in PowerShell, setting all scratch in the same process:

```powershell
$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
$env:SAVETOOLKIT_TEST_TEMP = 'D:/Temp/saveeditor/test-scratch/samples-bg3-cp77'
$env:TEMP = $env:TMP = $env:TMPDIR = $env:SAVETOOLKIT_TEST_TEMP
$env:SAVETOOLKIT_FUZZ_TIME_BUDGET = '300'
dotnet test --project plugins/BaldursGate3/SaveToolkit.Plugins.BaldursGate3.Tests -c Release --timeout 10m --report-xunit-xml --results-directory "$env:SAVETOOLKIT_TEST_TEMP/final-bg3"
dotnet test --project plugins/BaldursGate3/SaveToolkit.Plugins.BaldursGate3.Tests -c Release --filter-class '*ExhaustiveSampleReviewTests' --timeout 10m --report-xunit-xml --results-directory "$env:SAVETOOLKIT_TEST_TEMP/audit-BaldursGate3"
```

For the sample-free run, point `SAVETOOLKIT_ARTIFACTS` at an existing empty
scratch directory and use a separate results directory. Missing-sample tests skip;
the authored regression fixtures and synthetic fuzz seeds still execute.
Both full runs use normal .NET 10 MTP. Shared workstation timings include JIT and
vary with contention; these are observations, not latency guarantees.
The API Release build passed with zero warnings/errors. No private sample bytes,
extracted values or hashes were committed. No files outside the two plugin folders
were edited. No push, merge or PR.
