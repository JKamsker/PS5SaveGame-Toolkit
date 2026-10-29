# Asset round — 2026-10-29, issue #14

Branch: `plugin/eldenring-assets`. Baseline: `a9585cde273a186eafb23b787cca2d78e8ea2a1f`.
Plugin 0.7.0; `eldenring.names.en` 3.0.0. All authored changes stay under `plugins/EldenRing`.
No sample data, full-resolution downloads or upstream implementation is committed.

## Content and size

- 1,245 official descriptions and illustrated catalogue records: 437 weapons/shields, 593 goods,
  115 talismans, nine starter armour pieces, 91 Ashes of War. All 5,770 original identity records remain.
- 1,096 deduplicated WebP icons, at most 64×64: **1,978,666 bytes**.
- Three 1312×1312 map backgrounds: M00 **453,616**, M01 **84,918**, M10 **123,598 bytes**.
- **All new embedded image assets: exactly 2,640,798 bytes**, below 4,000,000 bytes.
- 597 exact event-ID placement references; missing/ambiguous/unsupported layers stay unpositioned.
  Regional nodes choose their dominant layer and retain every candidate in the accessible list/table.
  All statuses stay Unknown; source/calibration/legacy alignment remains Inferred.

The original-byte source pins, official-text joins, reviewed punctuation alias for sample goods 8600,
image transforms and coordinate uncertainty are in
[`visuals.source.md`](../SaveToolkit.Plugins.EldenRing/ReferencePacks/eldenring.names.en/visuals.source.md).
The offline importer uses reviewed scratch files and runs under `python -I`; it never executes upstream code.
Every final asset has a full manifest hash and independently checked WebP header/type/dimensions.
Identity semantic digests, unknown/raw fallback and existing write limits/warnings are retained.

Publisher licence field: **Rights reserved by FromSoftware / Bandai Namco; used with attribution per owner decision 2026-10-29**.
ClayAmore identity labels retain MIT and the complete notice. Text/image/coordinate contributions have
separate documented provenance, with Sources/UI attribution and `Info.References` links.

## First-load measurements

Scratch console project: `%TEMP%/eldenring-assets-round/Benchmark.csproj`, outside the repository.
.NET **10.0.400**, Release, Windows x64; five fresh processes per version, no warmup.
Largest available `PPSA03169` `memory.dat`: **28,967,040 bytes**. Sample extraction and plugin construction
occur before measurement. No confidential fields or character/account identities are printed.

After a full GC, a stopwatch and `GC.GetAllocatedBytesForCurrentThread()` surround construction of
`ReferencePackStore([plugin])` and `store.Pin(plugin, titleId)`. A second interval surrounds the first
`plugin.Open(file, references)`, including its save-byte read and parsing. No BuildView/query timing is included.
Combined time is summed within each process before taking its median. Allocations are cumulative thread
allocations, **not retained memory or peak RSS**. Raw JSONL measurements remain in scratch.

| Operation | Before median ms (range) | After median ms (range) | Before allocated bytes | After allocated bytes |
| --- | ---: | ---: | ---: | ---: |
| Load/pin all packs | 151.63 (138.59–162.87) | 156.81 (152.08–171.64) | 69,743,136 | 162,036,504 |
| First Open | 25.48 (22.80–32.08) | 24.49 (22.79–33.72) | 29,689,456 | 29,689,904 |
| Combined | 182.82 (163.09–185.67) | 185.08 (178.48–196.26) | 99,432,592 | 191,726,408 |

Time is effectively unchanged in this measurement; machine load/cache state varies. Transient allocations
increase substantially: the shared Core `PackFileReader.ReadBounded` allocates a new 81,920-byte scratch
buffer per file. The 1,099 new image files alone account for 90,030,080 scratch-buffer bytes before
metadata/output buffers. These buffers are short-lived. The framework loads each pack once and caches
its image bytes; repeated document `GetAsset` returns the same pinned content. The plugin lazily resolves
the placement catalogue when a regional map is expanded. Pooling/reusing the shared read buffer is a
concrete SDK follow-up; Core is outside this implementation agent's authorized folder.

## Validation

Both commands run with
`SAVETOOLKIT_ARTIFACTS=C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts`
exported in the same shell invocation:

```powershell
$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
dotnet test --project plugins/EldenRing/SaveToolkit.Plugins.EldenRing.Tests
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj --nologo -v quiet
```

- Plugin suite: **247 passed / 0 failed / 0 skipped**, including all sample-backed conformance cases.
- API build: **0 warnings / 0 errors**. Scratch Release benchmark build: **0 warnings / 0 errors**.
- Existing exact identity/name/progress digests remain intact; richer description searches intentionally
  add relevant matches, while exact-name filters remain available.
- Sample inventory and catalogue details resolve bundled icons, including the west Limgrave map.
- All 499 existing and 1,748 community F9 candidates publish through Core with stable field navigation,
  bounded positions/provenance and read-only Unknown states. Independent Church of Elleh / Godrick
  anchors, malformed/unsupported coordinates, missing art and missing-coordinate fallbacks are covered.
- Every new image hash, signature, detected type, dimensions, deduplication and total-byte budget is checked.
- Visual QA outside the repository checked a 65-icon contact sheet, all three annotated world layers,
  and zoomed Church of Elleh / Godrick anchors against the artwork. Legacy calibration stays Inferred.
- Original no-op/edit/undo/redo byte-preservation and write-warning tests pass. All C# files stay below
  300 lines. No repo tooling, sample artifacts, external-source execution, push or merge is introduced.

The central `docs/LICENCE_EXCLUSIONS.md` still needs the integration owner's corresponding EldenRing-row
updates (publisher-asset refusal resolved; Map Genie retained only as an optional link). It is deliberately
not changed here because the task explicitly restricts authored changes to this plugin folder.
