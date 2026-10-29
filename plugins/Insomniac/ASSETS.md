# Insomniac asset round — 0.8.0 / issue #20

Historical asset-round report, 2026-10-29, branch `plugin/insomniac-assets`.
Measurements and validation below describe that round, before the performance and
maintainability reviews. See [README.md](README.md#validation) for current validation
and [PERFORMANCE.md](PERFORMANCE.md) for the later allocation measurements.
All asset-round changes were inside `plugins/Insomniac/`.

The current evidenced title/catalogues are Rift Apart PPSA01474. Spider-Man has no
mapped item catalogue or title-specific views in this plugin, so adding unreferenced
Spider-Man art would not help the current UI. No broader title support is claimed.

## Delivered content

- Nine Guides4Gamers backgrounds aligned with the existing 403 regional markers.
  Each uses the matching map ID/version and the full original pixel extent.
  Backgrounds are stitched from versioned tiles, never cropped, uniformly resized
  within 2048 pixels and WebP-compressed. Largest: 158,654 bytes.
- 20 weapon icons, 19 upgraded-form render previews and 16 full-set armour thumbnails:
  **55 unique images, 74,526 bytes**, all within 64×64. Pale backings keep black
  silhouette icons legible in both themes. Carbonox/preorder previews share art.
- `assetId` on 20 weapon, 51 armour-piece and 19 upgraded-form records. Weapon and
  armour F8 tables expose ImageNode cells; reference pickers use the same local IDs.
- 91 authored menu-description synopses: 20 base weapon, 51 piece and 20 upgraded form.
  These are explicitly synopses, with CC BY-SA 3.0 provenance and inferred joins.
- A separate, bounded upgraded-form reference table. It does not infer this save's
  level or purchases. RYNO ∞ has no distinct supplied preview; internal Emperor/Reaver
  armour aliases remain without images. Armour thumbnails show sets, not slots.

Map art: **1,165,708 bytes**. Total new embedded image bytes: **1,240,234**
(64 WebP resources), below the 4,000,000-byte plugin budget. Provenance JSON and
catalogues are minified; no original downloads, scripts or private save bytes are
included. Originals and image processing stayed in `%TEMP%/insomniac-assets`.

No individual ModGrid cell icons, prerequisites or costs are assigned: these save
identities remain uncalibrated. Guide pickup states remain Unknown, and no PS5
world/fog-to-guide transform or marker-to-save-bit join is asserted. Existing write
boundaries and warnings are preserved.

## Alignment, attribution and fallback

See [background source/alignment](SaveToolkit.Plugins.Insomniac/ReferencePacks/insomniac.rift.maps.en/backgrounds.source.md)
and [image sources](SaveToolkit.Plugins.Insomniac/ReferencePacks/insomniac.rift.en/images.source.md).
Their provenance JSON pins every tile/image URL, revision/version, retrieved source
hash, output hash and dimensions. README, manifests, Info.References, UI descriptions
and Sources retain attribution. Rights-reserved artwork follows owner decision
2026-10-29; wiki-derived records remain CC BY-SA 3.0.

The F2 store validates/loads images once and shares immutable bytes across pinned
contexts. Plugin code performs no image decode or filesystem/network I/O. Regions
resolve their backgrounds lazily on expansion. F8 image cells materialize per page.
Missing art/provenance retains coordinate-only maps and all text markers. A user
replacement marker catalogue does not inherit the embedded snapshot's background:
its geometry/alignment has not been established. Missing packs retain raw IDs.

## Measurements

Quick stopwatch, Windows x64, .NET 10 Debug, largest primary sample **577,928 bytes**,
external sample store assigned in the same shell. Sample discovery/import occurs
before the timer. One fresh process before and after; no claim of statistical
significance. Allocations use `GC.GetTotalAllocatedBytes(true)` after full GC.

First-load operation: construct ReferencePackStore, pin context, Open, BuildView,
expand all nine community maps, dispose. Before: **176.35 ms / 54,158,128 bytes**.
After: **210.30 ms / 63,365,248 bytes**. Added startup work includes asset loading,
hash/type/dimension validation, catalogue/provenance and initial JIT.

Cached operation: reuse the same store/context, five Open+BuildView+Dispose calls.
Before mean: **37.93 ms / 48,029,609 bytes** per call. After mean:
**37.23 ms / 48,102,400 bytes**. This small timing difference is measurement noise;
reopening documents does not reload the image packs.

## Validation

`$env:SAVETOOLKIT_ARTIFACTS = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'`
was assigned in the same invocation as the plugin tests:

- Full plugin suite: **134 passed, 0 failed, 0 skipped** (five new tests).
- Existing conformance/no-op evidence preserves all 76 supplied game records.
- Existing community-map test now validates backgrounds and all 403 marker positions,
  identity transforms, attribution, bounds and JSON through Core across all 36 primary
  gameplay records, preserving serialized bytes.
- New tests: every image hash/type, deduplication, exact bytes/budget, every catalogue
  image ID, synopsis counts, Core image cells, honest upgraded-form separation,
  missing-art fallback and replacement-marker alignment fallback.
- Independent Pillow header/decode/dimension audit: all 64 resources valid; every
  icon ≤64 pixels, every map ≤2048; source map versions/extents match marker snapshots.
- Nine-region marker-overlay contact sheet and all-image contact sheet visually checked.
- `dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj`: **0 warnings, 0 errors**.
- Whitespace checks pass; every non-generated plugin C# file remains below 300 lines.

No changes outside the plugin. No push or merge in that round. The
[central licence register](../../docs/LICENCE_EXCLUSIONS.md) now records both
Insomniac source decisions as resolved imports: factual markers under the
2026-10-28 decision, and backgrounds/wiki images under the 2026-10-29 decision.
