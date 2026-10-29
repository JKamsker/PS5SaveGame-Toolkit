# Atomic Heart evidence, updated 2026-10-29

Checked against implementation `72cd997` after the roadmap, write, completion,
asset, F12/F13/F14 and review rounds. Scope: PPSA10695 only, observed engine 4.27.2,
package 522. Patch/region/DLC and known in-game counts are unknown.

Current documentation-audit validation (2026-10-29): **214 passed, 0 failed,
0 skipped** with private samples in Release; API Release build with
`--warnaserror`: **0 warnings/errors**; documentation checks: **14 passed**.
Earlier round counts below are historical. Current codec batch allocation is
about **0.63 MB**, measured after caller-owned workspace adoption; measurement
scope and historical diagnostics are in [PERFORMANCE.md](PERFORMANCE.md).

## Corpus and F1

`PPSA10695_saves_dec.zip` contains two gameplay entries: main slot 9 and its
console backup. There is **one generation and one unique payload**. Neither a
backup twin nor comparison of the archive to itself is a controlled event pair.
No churn masks or invented event fixtures were added.

Reproduction from the repository root:

```powershell
$archive='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts/PPSA10695_saves_dec.zip'
dotnet run --project tools/SaveToolkit.SaveDiff -- compare $archive $archive --semantic atomic-heart --all --file ue4savegame.dpx.sav

$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'; dotnet test --project plugins/AtomicHeart/SaveToolkit.Plugins.AtomicHeart.Tests
```

Observed F1 result: 2 unchanged files, 1 identical backup twin, 0 changed bytes,
0 semantic changes and 0 decode errors. F1 short-circuits identical files; the
sample tests additionally decode and walk both plugin views with a 20,000-section
budget. The write-round views exposed 9,107 scalar values without truncation or duplicate-path
warnings, and their semantic diff is empty.

An entirely authored fixture reorders two tagged properties while changing only
one `Amount` value. F1 reports exactly that value change, with no additions or
removals. This tests the adapter; it does not prove gameplay semantics.

## Decoding and boundaries

All 14 compressed chunks decode through `SaveToolkit.Codecs`' managed Kraken
implementation: 13 × 131,072 + 47,455 = 1,751,391 bytes. The inner array prefix is
1,751,387, exactly the remaining decoded length. Frame duplicate lengths, exact
codec input/output consumption, aggregate size and the outer SHA-1 are checked.

One disposable `OodleWorkspace` is reused across all streams in a decode batch.
Each stream writes directly into its exact slice of the bounded aggregate;
there is no separate per-stream decoded result to copy into it.

Root UObject tags occupy the beginning at offset 4; `None` begins at `0x8B`,
followed by a zero int32. Manual records begin at `0x98`. Five subsystem records
end at `0x129B`; the main level has 70 actors. The sublevel count at `0x11D65`
is 261. Across 262 levels there are 3,502 actors and 556 components, plus the five
subsystems: **4,063 indexed objects**. The final level ends exactly at 1,751,391.

Each index entry retains parent, kind, both class-related strings, name, full
record span, data span, tags, inspected properties and any opaque tail. Both
class strings are parsed as FStrings: some nonempty second strings occur even
when the first is `None`. Interpreting every second string as a zero int32 fails
the real corpus. Stock SaveExtension's GameInstance/filter/level-script sequence
is not substituted for this measured fork.

The 90 inspected slots comprise 12 ResourcesList, 7 SpecialResourcesList,
5 AmmoList, 10 AuxList, 50 QuestItemsList and 6 ItemsGrid entries. Counts include
empty slots; they do not establish capacity limits or player ownership. The
manual six-ID inventory prefix is separate from quantities. Storage's raw
properties remain inspectable even though it has no populated inspected slots.

A separately authored scratch Python grammar reader consumed the shared-decoded
stream to the same end and independently counted those records. Its input came
from the shared codec: **independent grammar inspection, not a second decoder**.
No scratch program, decoded dump or private sample was added to the repository.

## Phase status and limits

- Phase 1 implementation: managed decoding, exact length checks, manual grammar,
  bounded indexes, F8 search, span/opaque inspection and F1 corpus evidence done.
  Independent codec agreement remains an open acceptance item.
- Phase 2 partial: explicit serialized inventory fields, exact asset joins, unknown
  retention and an attributed 48-entry F2 item pack (45 weapon/ammunition pairs
  plus three screenshot-based resource/consumable joins). Separate 8-entry guide
  and 9-entry Shok catalogues have no native save joins. Controlled gameplay
  observations and wider build/NG+/DLC coverage remain open.
- F4 integration: existing populated references now accept same-category catalog
  assets whose unambiguous full paths occur in the input. Unmapped/empty references
  remain read-only; their quantities may be edited when explicitly signed/populated.
- Phase 3 implementation: explicit Amount edits plus F6 selected/document target
  quantities, bounded to original container maxima; stored changed chunks, retained
  unchanged compressed chunks, all affected lengths and SHA-1 repaired. No-op and
  complete undo serialize byte-identically. `Serialize()` decodes/reparses each new edited output
  and compares all intended plain bytes. Maturity moved to Experimental only after
  sample-backed edit conformance passed.
- Phases 4–5: no allocation, structural move/split/removal, skill/loadout, event
  reward or progression writer exists. Their opaque/dependent state cannot safely
  be reconstructed. No writes are held off merely for missing owner evidence.
  No PS5 acceptance claim or Stable promotion.

Historical write-round validation (2026-10-28): **46 tests passed, 0 failed, 0 skipped**, with SAVETOOLKIT_ARTIFACTS set
in the test command. API build passed with 0 warnings and 0 errors. The suite
includes complete lazy traversal, catalog hashes/attribution, malformed inner
counts and lengths with valid SHA-1, synthetic truncations, 500 seeded inner
mutations, alternative-codec rejection, native-tail preservation, query paging,
duplicate-property bindings and byte-identical serialization of both real files.

## Write-round preservation and independent output checks

Each eligible sample Amount is edited, serialized and reopened. F1's `ByteDiffer`
finds changed decoded bytes only inside that explicit integer payload. All eligible
same-category reference pairs are exercised, including FString growth/shrinkage.
After aligning the changed scalar, F1 permits differences only in its explicit
enclosing length fields; every unknown/native byte is retained. Additional checks
compare record identities, leaf payloads, opaque tails, Amount and ItemId. Combined
quantity/reference edits and chunk growth/shrinkage/new-frame boundaries pass.

A separately authored literal-block oracle parses output framing without the
production output parser or `OodleCodec.Decompress`. New streams must contain
exactly `CC 06` followed by the declared bytes. Reused compressed streams must be
byte-identical to their original stream and use the original decoded baseline.
The resulting aggregate equals the intended edit, with exact array length and
SHA-1. This independently validates **new stored output**; it does not independently
decode the original compressed Kraken corpus. That remains an explicit acceptance
item in [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31).

Core DocumentSession tests verify individual Amount/reference undo/redo,
whole-selected-batch undo as one group, redo byte identity, rejected direct planned
action invocation, stale/single-use preview handling and atomic invalid batches.
Empty selections never mean document scope. All writable fields remain Inferred;
fields, outcomes, action descriptions and previews warn that the game may reject
the save because PS5 stored-block acceptance and inner integrity are unproven.

See [README.md](README.md) for the exact owner capture/console checklist, source
attribution, format safety limits and remaining SDK/evidence requests.

## Completion evidence, updated 2026-10-29

- F8 inventory queries cover all 90 slots before paging, search current catalog
  names/IDs/categories and numeric values, filter exactly, sort Amount/ItemId
  numerically and retain stable keys. An authored 450-slot model verifies paging
  and a match beyond the first 200 rows. F6 #27 session tests resolve unloaded
  matching rows, honor exclusions, preserve empty selections and undo/redo as one
  group. The inventory uses a single F8 table; its duplicate eager/lazy inventory
  pages were removed by F14. F1's shared `PluginViewDecoder` traverses F8 and
  explicitly includes empty rows. The SDK-only F12 hook
  `atomic-heart.saveextension-v1` emits parsed scalar tag paths; native/opaque
  payloads remain byte-level evidence. F13's read-only catalogue inspection uses
  the shared `ReferenceInspection` helpers and supports browse-only pickers and
  category/key/name ordering. No timer field needs a duration hint.
- F2's current 48-entry item catalog retains the 45 public label pairs
  (37 weapons, 8 ammunition) and their pinned GPL source/credits, with three
  screenshot-based joins added in the asset round. F3 detail cells expose raw IDs, provenance,
  descriptions and bytes. Unmapped labels/categories are explicitly identifier
  formatting/container/path inference. No private ID dictionary is embedded.
  F4 read-only record fields annotate exact catalog-matched item/blueprint IDs.
- F9 inspects three top-level skill collections; all have two serialized zero
  counts. Two existing weapon references are also linked to their original slot
  bytes. Every graph state stays Unknown, with no prerequisites, upgrade levels
  or costs fabricated. Weapon labels track replacements and undo. The map lists
  262 saved levels in 99-marker pages with Unknown status and external guide links;
  no coordinates/artwork imported. Tests resolve every sample graph/map target
  and validate maximum-length identifiers, duplicate skill tags and 4,096 levels.
- Compact level identities include the full name and duplicate occurrence;
  navigation IDs fit F9's bounds without relying on offsets. Level inspection
  pages are directly reachable under the Levels section.
- Each distinct edited export still uses shared EncodeStored, repairs framing
  and SHA-1, decodes/reparses and compares all intended plain bytes. Frame reuse
  decisions determine an exact allocation before writing. The document retains
  one private validated result and compares actual edits before reuse; every
  output reparses outer integrity and returns a fresh buffer. No-op/complete undo
  reuse the original Open-validated decode and remain byte-identical. Cache tests
  cover returned-buffer damage, changed Amount/reference, complete undo and redo.
- Historical export diagnostics predate the codec changes. Their measurements
  and the current reusable-workspace allocation check are separated in
  [PERFORMANCE.md](PERFORMANCE.md); no elapsed-time regression threshold is used.

Historical completion validation (2026-10-29): **59 passed, 0 failed, 0 skipped**, with the artifacts
environment variable exported in the same command. Full API build: **0 warnings,
0 errors**. No file outside `plugins/AtomicHeart` changed. See README's complete
AH-R1–R19 audit and F1–F14 adoption table; missing owner evidence remains #31.
F12/F13 are now used, not pending framework adoption. F14 retains all 90 sample
slot identities while hiding 79 confirmed `None`/zero vacancies by default.


## Asset round (2026-10-29)

Sample-ID inspection found `DA_Item_Resources_Metal_Parts`,
`DA_Item_Synthetic_Material_Resource` and `DA_Consumable_aid_small`; they now have
Inferred source-backed labels and icons. `DA_Item_Jelly_Resource` stays unmapped.
Existing weapon asset joins now show official English names/renders/excerpts.
Reference-only additions are ineligible for asset replacement, while original
Amount specs and warnings are identical with/without the pack.

NORA screenshots establish the Small Neuromed 50-health effect and 2 Synthetic
Material + 4 Biomaterials recipe, KS-23 Basic Grip level-1 Additional Gas Vent
cost of 19 Metal Parts, and basic Cartridge Module cost of 25 Metal Parts +
12 Synthetic Material + 3 Chemistry. These facts describe the source screenshots;
no maximum level, console build match or installed state follows from them.

Separate reference catalogues contain six resource labels without native joins,
two module references and nine Shok name/cost summaries. Their authored
`reference/...` keys cannot be confused with native skill/module IDs; the saved
skill graphs retain Unknown states/edges/costs. No supported geographic transform
exists for the 262-level location list, so no background/coordinates were added.

Pack audit: 23 unique 64x64 WebP icons, exactly 12,624 bytes. Every hash and MIME
signature validates; all 23 record asset IDs resolve. Attribution is in sidecars,
manifest, README, PluginInfo and visible record/Sources metadata. Image processing
occurred outside the repository with isolated Python; no downloaded code executed.
Historical asset-round suite (2026-10-29): 66 passed, 0 failed, 0 skipped. API build: 0 warnings/errors.
PERFORMANCE.md records historical three-run median first-load and pinned-load
diagnostics, including allocation scope; they predate codec adoption. No console/Stable
claim, new cap, prerequisite, native mapping or writer is implied.
