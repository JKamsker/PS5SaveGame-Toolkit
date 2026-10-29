# Asset-round validation — 2026-10-29

Baseline: `a9585cde273a186eafb23b787cca2d78e8ea2a1f` (plugin 0.6.0).
Asset implementation: plugin 0.7.0, `plugin/cyberpunk2077-assets`.

## Size and coverage

- 16 unique WebP icons, each ≤64×64: **27,826 bytes**.
- One 1,792×1,792 WebP Night City background: **557,120 bytes**.
- Total new image bytes: **584,946**. Complete embedded new pack including JSON,
  manifest and provenance sidecars: **710,750 bytes** with LF text.
- 146 exact item-ID/name/caption/icon records (16 distinct captions); 92 are modern cyberware IDs.
- 193 official CDPR perk names/descriptions keyed by exact modern perk enum.
- Icons and map inspected visually. Pillow verified every WebP header, decoded image and size.
- New pack loaded by the real host loader: manifests, hashes, MIME types, dimensions,
  every record asset link and provenance validated. Corrupted map bytes rejected by hash.
- The same-build local-item override and absent-pack/raw-ID fallback remain intact.
- Maps validate with local F2 background provenance and no invented saved-marker positions.
  Unpositioned markers retain status, navigation and the accessible text list.

## First-load stopwatch and allocations

Largest supplied `sav.dat`: **1,302,721 bytes**. Release/net10.0, same machine and host projects.
The baseline plugin was reconstructed from the immutable baseline commit into `%TEMP%`, with its
SDK path adjusted only for the temporary benchmark project. A separate temporary console project
used the current plugin. Both binaries print the plugin version, confirming 0.6.0 versus 0.7.0.
Three fresh-process runs alternated baseline/current; no other build/test ran during measurement.
Sample discovery/import occurs before timing. Start `Stopwatch` and `GC.GetTotalAllocatedBytes(true)`
before `new ReferencePackStore([plugin])`, pin PPSA04027, then open the largest file and `BuildView()`.
Report deltas immediately after pinning and after the root. Figures include JIT; elapsed time is noisy.

| Metric | Before, median of 3 | After, median of 3 |
| --- | --- | --- |
| First pack load + pin | 151.39 ms / 73,924,328 allocated bytes | 177.60 ms / 78,241,536 allocated bytes |
| First pack load + pin + open + root | 372.56 ms / 136,135,608 allocated bytes | 370.18 ms / 140,845,168 allocated bytes |
| Individual total times | 418.58 / 372.56 / 354.12 ms | 449.54 / 370.18 / 369.91 ms |

The extra full-load allocation is **4,709,560 bytes (3.46%)**; the timing does not establish a speedup.
Host `ReferencePackStore` loads embedded packs eagerly once. Documents pin immutable assets and
share the same `AssetContent` instances; repeated document opens do not decode or reload packs.
Plugin image cells materialize only on requested rows; background lookup happens on lazy map expansion.

## Checks

Sample-enabled command (environment set in the same shell):

```powershell
$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
dotnet test --project plugins/Cyberpunk2077/SaveToolkit.Plugins.Cyberpunk2077.Tests
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
```

**152 passed / 0 failed / 0 skipped**. API: **0 warnings / 0 errors**. Includes conformance,
no-op bytes, edit/reopen, undo, sample evidence and new asset/picker/map tests. Every plugin C# file
remains below 300 lines; whitespace checks pass. Changes stay under `plugins/Cyberpunk2077/`.
No downloaded code executed; no private saves or extracted sample data committed. No push or merge.
Shared licence-register updates are described in README for the coordinator; the shared file is untouched.
