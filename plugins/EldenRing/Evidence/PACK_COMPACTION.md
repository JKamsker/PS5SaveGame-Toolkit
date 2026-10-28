# Community reference pack compaction

Measured 2026-10-29 against `c63ba1b474eada759fa018854504589310379361`.
Community pack v1.1.0 retains 3,221 item identities, 1,748 flag candidates and 1,072
technical layout records. Pack JSON and its manifest are minified, with regenerated SHA-256 hashes.

## Embedded bytes

Exact file bytes, including the final LF. A removed file counts as zero.

| File | Before | After |
| --- | ---: | ---: |
| `param-rows.json` | 5,967,649 | 0 (removed) |
| `items.json` | 3,425,686 | 616,807 |
| `flags.json` | 2,707,076 | 546,312 |
| `param-layouts.json` | 846,642 | 105,266 |
| `pack.json` | 4,224 | 3,785 |
| `community.source.md` | 20,141 | 20,168 |
| **New community pack total** | **12,971,418** | **1,292,338** |
| All EldenRing reference packs | 18,382,017 | 6,702,937 |

The new community pack shrank 90.0%, to 1.29 MB (1.23 MiB), below the 5 MB target.
The existing packs are unchanged. This follow-up adds a commit; it does not remove the original
large blobs from git history.

## Removed and hoisted data

All 8,651 PARAM row labels from eleven `Names` tables and their standalone dump browser are
removed. A search of catalog consumers showed that no save decoder or inventory, equipment,
progress or regulation view used that catalog: its only consumer was its own browse section.
Keeping arbitrary rows selected from one sample would add a misleading coverage boundary.
The existing complete item-name catalogs and all TGA identities remain available to native
views and read-only pickers. Technical PARAM layout facts remain separate and unchanged.

Dataset-level attribution now holds the default source URL, pinned revision, uncertainty and
flag schema. The sidecar retains every contributing source file's SHA-256 and pinned URL.
Differing item source URLs remain on records. MassItemGib membership joins to exact source
filenames; inherited editor membership joins to the single pinned `items.rs`. Layout field
provenance joins by table/category to its XML definition. Per-table version/type facts live in
the `:schema` summary and field ordinals remain in native keys. Duplicate names, state-group
labels and repeated descriptions are omitted. The sidecar documents these joins, and dataset
modifications remain visible in browser details alongside attribution.

Independent semantic digests computed from the previous commit verify every retained ID,
name, category and used item/group/flag/layout fact. Explicit NC/no-redistribution sources,
game-extracted descriptions and artwork remain excluded.

## First-load measurements

A scratch console project at `%TEMP%/eldenring-pack-benchmark`, outside the repository,
references `SaveToolkit.Testing` and the EldenRing plugin. Both versions were built in Release
with .NET 10 on Windows x64. Each result is the median of five fresh processes with no warmup.
The largest available `PPSA03169` sample's `memory.dat` is **28,967,040 bytes**.

Sample discovery/extraction and plugin construction happen before measurement. After a full
GC, `Stopwatch` and `GC.GetAllocatedBytesForCurrentThread()` surround construction of
`ReferencePackStore([plugin])` and `store.Pin(plugin, titleId)`. This loads all EldenRing
packs, not just the new one. A second measurement surrounds the first `plugin.Open(file,
references)`, including its save-byte read and parse. No view construction or queries are
included. The combined measurement adds these two intervals within each process, then takes
the median; it is not the sum of the two independent medians.

| Operation | Before median ms (range) | After median ms (range) | Before allocated bytes | After allocated bytes |
| --- | ---: | ---: | ---: | ---: |
| Load and pin all packs | 1,080.60 (862.93–1,283.44) | 522.41 (259.73–737.68) | 160,079,552 | 67,206,528 |
| First `Open` | 63.97 (44.00–111.19) | 83.62 (45.54–165.13) | 29,690,960 | 29,689,456 |
| Combined | 1,144.57 (909.79–1,394.63) | 606.03 (331.07–798.20) | 189,770,512 | 96,895,984 |

Combined allocated memory is approximately **180.98 → 92.41 MiB**; pack loading alone is
**152.66 → 64.09 MiB**. These counters measure cumulative allocations on the current thread,
not retained heap size or peak process memory. Timing is illustrative and varies with machine
load and filesystem caching; the save parser itself was not optimized, and its timings vary.
Raw before/after JSONL results remain in that scratch directory, outside the repository.

## Validation

Run with the artifact root exported in the same PowerShell invocation:

```powershell
$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
dotnet test --project plugins/EldenRing/SaveToolkit.Plugins.EldenRing.Tests
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj --nologo -v quiet
```

Final plugin suite: **234 passed, 0 failed, 0 skipped** (including sample-backed conformance,
byte-preserving round trips, catalog paging/filtering, metadata attribution and semantic
compaction checks). API build: **0 warnings, 0 errors**. Scratch Release benchmark build:
**0 warnings, 0 errors**. Changes remain entirely inside `plugins/EldenRing`.
