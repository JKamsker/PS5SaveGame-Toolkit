# GenericJson asset-round validation — 2026-10-29

Issue #18; branch `plugin/genericjson-assets`. Changes are confined to `plugins/GenericJson/`.

## Delivered and skipped

One official Cyberpunk Performance Mode description, with its source URL, publisher rights notice,
retrieval date and historical/inferred-association warning. The pack is version 1.1.0; all reference
JSON is minified and manifest hashes match. Current embedded JSON totals **2,974 bytes**.

**0 icons, 1 description, 0 map backgrounds; 0 new embedded image bytes.** There are no image asset
IDs or assets datasets to validate. Pack loading/schema/hash checks pass through conformance and
the focused reference tests. No downloaded code was executed; no image processing was needed.

The profile-by-profile assessment, searches, sources and reasons for skipping images appear in
README's Asset assessment section. Ridge Racer has no gameplay catalogue worth illustrating;
It Takes Two has no verified alias catalogue or aligned map background; Cyberpunk's six settings
choices have no established native icon joins. Those are evidence gaps, not licence exclusions.
No attempt was made to import unrelated inventory art or relabel raw difficulty/Quality IDs.

## Load measurement

Quick local Release stopwatch/allocation comparison on .NET SDK 10.0.400, using an external TEMP
console harness. Both runs used:

```powershell
$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
dotnet run --project "$env:TEMP/genericjson-asset-round/AssetBench.csproj" -c Release
```

The harness imports all three titles through `SampleSaves`, filters using `GenericJsonPlugin.Probe`,
and chooses the largest recognized JSON: **settings.json, 45,415 bytes**. Sample import and Probe
are outside the measured interval, so JSON parsing has been exercised before timing. In each fresh
process it performs GC, records `GC.GetAllocatedBytesForCurrentThread()`, starts a Stopwatch, creates
one `ReferencePackStore`, pins it, opens the document, builds its initial view and explicitly resolves
both v136 catalogues. It stops the watch and allocation interval before disposing the document.
This includes initial pack loading/validation and view construction, not archive import or all lazy
UI expansions. It then measures 15 further Open + initial-view calls using the same store and takes
the median. Before is base `9acd492`; after is this round's code and pack data.

- First load: **80.0583 ms / 4,148,096 allocated bytes before**;
  **83.5384 ms / 4,129,904 allocated bytes after**.
- Reused-store median: **6.0689 ms / 3,380,120 allocated bytes before**;
  **3.9297 ms / 3,381,296 allocated bytes after**.
- One pack, zero load errors in both runs. Focused tests assert that repeated catalogue lookups and
  newly pinned contexts share the same loaded catalogue object. The existing plugin lookup cache
  and lazy control expansion remain in use; no repeated pack loading or new I/O was introduced.

These single-process timings include JIT/runtime variation and are not a performance improvement
claim. The largest-sample allocation comparison shows no material growth from the added text.

## Verification

With the artifacts variable above set in the same command:

```powershell
dotnet test --project plugins/GenericJson/SaveToolkit.Plugins.GenericJson.Tests/SaveToolkit.Plugins.GenericJson.Tests.csproj
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
```

- Plugin tests: **200 passed, 0 failed, 0 skipped**, including conformance, embedded pack validity,
  publisher provenance, historical warnings, cached catalogue reuse and real-sample picker text /
  byte preservation. Existing F9 tests validate the unchanged background-free map and raw targets.
- API build: **0 warnings, 0 errors**.
- `git diff --check` passes. No tracked or untracked repository files outside the plugin were changed.
- Existing write warnings, raw choice IDs, title/build pinning, catalogue fallback and save content
  are preserved. Console acceptance remains untested; the publisher sentence is historical context.
