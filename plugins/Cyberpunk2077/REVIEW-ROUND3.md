# Sample-backed data safety and performance review (#40)

Base: `8140888f`; branch: `review/samples-bg3-cp77`; date: 2026-11-05.

**Medium, fixed:** combined payload allocation preceded malformed LZ4 rejection.
See the README review section and `ChunkAllocationTests`: two authored cases
failed before the fix at **33,555,096 B**, against the retained **65,536 B** bound.
Every compressed block is now preflighted before allocation. Both real gameplay
copies also reject a final compressed-chunk size mutation within the same bound.
K4os decoding, output-length verification, all existing corruption diagnostics
and no-op preservation tests pass. Optional SDK consolidation is recorded in README.

Valid authored 64/128 KiB chunks allocate 65,680/131,216 B. The sample-free guard
requires doubling bytes to remain below three times the baseline allocation.
This verifies the expected linear allocation scale without a wall-clock assertion.

`ExhaustiveSampleReviewTests` opens both gameplay copies, walks all **173 lazy
sections** and every page of every collection through DocumentSession, checks
all **8,337 distinct fields**, and verifies byte-identical no-op output.
It changes **all 2,770 writable integer fields** in one batch per file, verifies
semantic review, export/reopen values, byte-exact undo and byte-exact redo.
An assertion ensures this covers every writable field, rather than silently
excluding a future noninteger editor. Existing sample tests also cover planned
point/stack actions, cancellation and whole-workspace ZIP export preservation.

Isolated audit observations (both copies, including first-use effects):
- Open: 27–134 ms, 16.2–16.3 MB managed allocation.
- Full lazy tree: 4.5–38.2 ms, 1.17–1.19 MB.
- Every collection page: 131–214 ms, 51.8 MB.
- Semantic review of the full edit batch: 227–293 ms, 45.1 MB.
- Edited export: 12–45 ms, 31.7 MB.
These observations are from the batch-edit audit, not comparative before/after
benchmarks. Preflight adds an allocation-free compressed-byte scan on valid input.

Baseline: **332 passed / 0 failed / 0 skipped** (37.85 s).
Final with samples: **337 / 0 / 0** (33.01 s).
Without samples: **212 passed / 0 failed / 125 expected skips** (11.17 s).
Mutation fuzz: **600 cases**, one sample + two synthetic seeds, zero known failures,
6.67 s parser work. Default selection deduplicates byte-identical backup copies.
Experimental maturity, warnings and write scope remain unchanged.

## Reproduction

Run from the repository root in PowerShell, setting all scratch in the same process:

```powershell
$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
$env:SAVETOOLKIT_TEST_TEMP = 'D:/Temp/saveeditor/test-scratch/samples-bg3-cp77'
$env:TEMP = $env:TMP = $env:TMPDIR = $env:SAVETOOLKIT_TEST_TEMP
$env:SAVETOOLKIT_FUZZ_TIME_BUDGET = '300'
dotnet test --project plugins/Cyberpunk2077/SaveToolkit.Plugins.Cyberpunk2077.Tests -c Release --timeout 10m --report-xunit-xml --results-directory "$env:SAVETOOLKIT_TEST_TEMP/final-cp77"
dotnet test --project plugins/Cyberpunk2077/SaveToolkit.Plugins.Cyberpunk2077.Tests -c Release --filter-class '*ExhaustiveSampleReviewTests' --timeout 10m --report-xunit-xml --results-directory "$env:SAVETOOLKIT_TEST_TEMP/audit-Cyberpunk2077"
```

For the sample-free run, point `SAVETOOLKIT_ARTIFACTS` at an existing empty
scratch directory and use a separate results directory. Missing-sample tests skip;
the authored regression fixtures and synthetic fuzz seeds still execute.
Both full runs use normal .NET 10 MTP. Shared workstation timings include JIT and
vary with contention; these are observations, not latency guarantees.
The API Release build passed with zero warnings/errors. No private sample bytes,
extracted values or hashes were committed. No files outside the two plugin folders
were edited. No push, merge or PR.
