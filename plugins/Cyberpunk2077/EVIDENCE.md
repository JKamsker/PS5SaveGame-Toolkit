# Corpus evidence and remaining validation

Observed **2026-10-28**. Machine: Windows x64, .NET SDK 10.0.400.
No controlled console capture or load test was performed. The plugin remains Experimental.

## Completion round

Plugin 0.6.0 adopts F9 GraphNode/MapNode and F10 NullableSpec without adding a write category.
The complete requirement/acceptance classification is in [COMPLETION.md](COMPLETION.md), and the
README contains the F1–F13 adoption table. The follow-up adopts the merged F12 decoder hook
and F13 read-only catalogue inspection; see the README for concrete scope and validation.

- Six saved modern perk groups, no allocated perks: graph states Unknown; no edges, prerequisites,
  costs, effects or entitlement inferred. Populated ranks use independent synthetic fixtures only.
- 232 travel catalog/saved markers, exactly one saved registration. Found denotes registration,
  every other marker is Unknown. No background/coordinates/geographic joins; the official Piggyback
  map is an attributed external link only. Missing packs retain the raw saved ID.
- Lazy pages publish at most 128 spatial entries with stable identities, text alternatives and
  read-only field targets. Host publication tests expand every page before loading any F8 row,
  verify each target/path/binding, reject edits and retain a clean session and byte-identical output.
- Merged fact details distinguish typed null/absence from uint32 zero through F10, retain the existing
  presence filters and exact values, sort numerically, and follow raw saved-fact edits/undo/redo.
- Root/child sibling overlap is now rejected independently of containment and next-link validation.
  Equipment/world/stat joins use indexes while preserving duplicate-identity/hash ambiguity.

**Final sample validation:** 141 passed / 0 failed / 0 skipped; API build 0 warnings / 0 errors.
Separate absent-artifacts run: 57 passed / 84 expected skips / 0 failed; all new sample tests skip
correctly. This is an absence check, not sample validation.
The baseline and final-ZIP/backup tests remain passing. Largest sample is still the sole distinct
1,302,721-byte gameplay save; no new console evidence was created. A focused development-machine
open/root measurement was approximately 0.21 seconds; parallel-suite measurements vary and no timing
threshold is asserted. The test suite retains stopwatch output for open plus expanded spatial views.

### Owner checks for the added presentations (#31)

- [ ] On a mature PS5 2.21 save, capture one named ordinary perk rank and refund separately, then one
  Relic rank and its access state; compare exact technical identifiers/ranks and spent/unspent points.
  Provide lawful same-build prerequisite/rank/point/entitlement data to establish a full rules tree.
- [ ] Discover one named travel point in a controlled pair, record map label/district/availability,
  and capture use or quest-controlled locking separately. Verify that the same full record ID changes
  registration; no coordinate or official-map location join is assumed.
- [ ] Confirm the F9 map's registered list agrees with the captured FastTravelSystem state, while
  catalog-only rows remain Unknown. No in-game edit is requested for these read-only presentations.

Earlier exact capture and enabled-write validation checklists below remain open in #31. Only the
owner may promote Stable. No private save data, game artwork or local resource exports are bundled.

## F1 baseline

Run with `SAVETOOLKIT_ARTIFACTS` pointing to the owner's private artifacts directory:

```powershell
$archive = Join-Path $env:SAVETOOLKIT_ARTIFACTS 'PPSA04027_saves_dec.zip'
dotnet run --project tools/SaveToolkit.SaveDiff -- list $archive
dotnet run --project tools/SaveToolkit.SaveDiff -- compare $archive $archive --file sav.dat --semantic cyberpunk2077 --all
```

Results: **one generation**, two `sav.dat` entries (primary + **one identical backup twin**),
**one distinct gameplay save**, zero changed bytes/semantic changes on self-comparison.
F1 short-circuits identical file hashes; that comparison does not independently validate decoding.
There is no before/after gameplay event or idle-save churn pair in this archive. Nothing is
masked as churn and no backup is counted as a second observation.

The nonprivate corpus manifest is embedded in tests as `Evidence/corpus.json`:

- Archive SHA-256: `79b29921e4e0e158dc7b2507461afcea7f21ad52a4b29e14f955ddb4b115e293`.
- Gameplay SHA-256: `7a67235f642fbbfc1ad4c0e96d83f05f0d7a068840b84aebac60f35c4f9ac9f6`.
- Save/game/archive **265 / 2210 / 195**, 1,302,721 stored / 5,607,178 expanded bytes,
  22 chunks, 159 nodes, 22 inventory entries, 2,750 fact entries in ten tables.
- F2 joins: 22/22 item IDs and 1,129/2,750 fact entries. Hashes repeat across fact tables;
  counting all entries is distinct from counting unique fact hashes.

`CyberpunkEvidenceDecoder` is a read-only **production F12 `ISaveDecoderHook`**, registered as
`cyberpunk2077-evidence` and independent of UI pages. It emits version fields, quantities by container + full 16-byte instance identity,
opaque instance-body fingerprints, and raw facts scoped by table node + hash. Table node IDs
are an explicit current-tree scope, not a proven persistent cross-save table identity.
The standard F1 builder reports duplicate identities rather than silently overwriting values.
The hook honors file/value/depth bounds and produces a truncated tree when limits are reached.
It intentionally does not claim complete RED-property/gear-stat decoding.

Tests decode the real sample explicitly and use `SemanticDiffer` to detect a locally constructed
600→601 ammo patch by full identity, plus its changed body fingerprint. That synthetic change
tests the hook and writer; it is **not** evidence from firing ammunition on a console.
The shared CLI `PluginViewDecoder` now traverses bounded F8 rows. Select the native hook through
`--decoder cyberpunk2077-evidence` for presentation-independent inventory/fact evidence. Tests use
the framework `HookSaveDecoder` adapter; no test-only decoder implementation remains.

## Verification

```powershell
$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'; dotnet test --project plugins/Cyberpunk2077/SaveToolkit.Plugins.Cyberpunk2077.Tests
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
```

Roadmap round 1: **85 passed**, no skips with samples; **31 passed / 54 skipped** with an empty
sample directory. API build: **zero warnings/errors**. Existing no-op, mixed edit, undo,
unsupported-version refusal, corruption and opaque preservation tests pass. New tests verify:
all F2 hashes/attribution and counts; canonical whitespace and conflict rejection; 22 item/1,129
fact joins; local build pinning/fallback; catalog metadata cannot expand write permissions;
host F4 candidate validation and inspection without clearing undo; F8 paging/search/filter/numeric
sort across 1,000 synthetic rows; stable keys, bounded cell construction, and live quantity queries.

Final ZIP test: an existing ammo quantity edit is exported through the actual WorkspaceExporter,
the primary and identical gameplay backup receive exactly the intended payload patch, and all
other exported files remain byte-identical. This verifies software preservation/backup mirroring,
not console acceptance. Save bytes and extracted game assets are never committed.

Round 2: **107 passed, 0 failed, 0 skipped** with the private samples exported in the same command.
An additional empty-artifacts run gives **45 passed / 62 skipped**, zero failures, checking only
missing-sample behavior. The API build succeeds with **0 warnings / 0 errors**. Tests also cover
all new collections, modern nested perk defaults/ranks, full-identity equipment/refusal, extension
bounds/encoding, typed garage hashes, item hash vectors/modifier bounds, exact wiki IDs/revisions,
catalog-only versus saved facts and cyberware, and existing point edit/undo query consistency.

## Owner capture / validation checklist

Use a spare slot and preserve pristine originals outside the repository. For every pair, record
PS5 title/region, displayed build, native header versions, DLC entitlement, language, lifepath,
slot, exact action and observed item/stat values. Omit account identifiers and credentials.

- [ ] **Idle control:** save A, reload, save B in the same place without taking any action. Export
  both so churn can be measured before interpreting gameplay changes.
- [ ] **Stack purchase/use:** save before/after buying exactly one known ammo/component stack;
  record exact item, money cost and quantities. Separately fire/use exactly one unit and capture
  before/after quantities/charges, including any automatic healing/grenade replenishment.
- [ ] **Equipment:** capture before equip, after equip and after unequip of one existing weapon
  or clothing item. Record slot, base name, tier, stats, modifiers/attachments and quantity.
- [ ] **Stash move:** move exactly one existing item between player and stash; capture both
  inventories before/after, noting quantity and whether the same instance is retained.
- [ ] **Upgrade:** upgrade one existing item once; record old/new tier, shown stats and material
  costs. Keep other equipment/actions unchanged.
- [ ] **Cyberware:** install then remove one existing implant in separate pairs; record slot,
  capacity before/after, contributing bonuses and other implants. Include a mature/DLC inventory.
- [ ] **Console acceptance of current writers:** edit one supported money/stack/attribute/point
  value at a time, restore to a spare slot using the owner's existing working path, load, observe
  the effect/use/equip behavior, save and reload, then compare unrelated inventory/progression.
  Log exact build and results. Only the owner can promote Stable.
- [ ] **Same-build data:** provide a lawful same-build TweakDB/localization export as a separately
  attributed local F2 pack if desired. Wiki embedding with attribution/share-alike is already approved.
  Include resource hashes, locale, DLC coverage and permissions. No artwork is requested for
  repository bundling without clear redistribution permission; no game/cloud credentials needed.

Later phases still need the plan's level/Street Cred threshold, skill reward, perk-rank buy/refund,
Relic-access, car acquisition/summon, travel discovery and specific quest-stage pairs, plus later
PS5 schema samples (277/2500 only if actually obtained). The write-enable round below supersedes
the old evidence gate for raw stored progression/fact values and prepared stack actions.

### Exact round-2 additions

- [ ] **Duplicate-instance equipment:** obtain two existing items with the same base record; equip
  one, save, equip the other in the same slot, save, then unequip and save. Record which instance,
  exact slot and visible tier/stats; retain the equip/unequip pairs separately. Include fists versus
  weapon/cutscene slots to resolve the sample's unmatched special `RightArm` identity.
- [ ] **Attachment/stat dependency:** attach exactly one named mod or attachment to an existing
  item, capture before/after, then remove it in a separate pair. Record attachment slot, displayed
  old/new stats, tier, quest/removal restrictions and material costs, if any. Do not combine with upgrading.
- [ ] **Modern perk and Relic ranks:** buy exactly one rank in one named ordinary perk; capture
  rank, attribute level, unspent/spent budget and visible effect before/after. Refund that rank in
  a separate pair. Repeat separately for a Relic perk with PL access, and capture access unlock alone.
  This early save has six modern groups but no stored allocated perks; synthetic ranks are not console evidence.
- [ ] **Cyberware capacity contributors:** capture install/remove of one named implant as separate
  events, including slot, capacity used/limit, implant tier, discount/bonus sources and every other
  implant. Separately change exactly one capacity-granting perk or shard, recording old/new contributor
  and capacity. Supply at least one mature base-game and one mature PL save with installed cyberware.
- [ ] **Garage lists:** capture acquisition of one named car; then summon, despawn and favorite it
  in separate pairs. Record variant, whether purchased/rewarded/stolen, UI ownership/availability,
  and exact event. The current save has empty active slots and omitted unlock arrays; acquisition
  behavior and populated spawned/unregistered structures are not demonstrated by that absence.
- [ ] **Travel registration versus accessibility:** capture discovery of exactly one named dataterm;
  record map label, district and visible availability. Capture using it separately. If possible capture
  one quest-controlled travel lock/unlock as its own event; registration alone does not establish accessibility.
- [ ] **Quest/journal progress:** capture one precise objective transition with displayed quest name,
  objective text/status and tracking state, including one gig and one base/PL collectible as separate
  pairs. Also toggle tracking alone in a separate pair. Journal records are opaque and cannot currently
  be mapped to completion from matching fact prefixes or the tracked-path candidate.
- [ ] **XP/rewards:** capture a single Level, Street Cred and skill reward threshold separately,
  with XP, level, points, rewards and newly available perks before/after. No XP/reward transition is inferred.

## Phase status

Phase 1 identifier/evidence work is complete with limited coverage; full same-build localization
remains data work, while 15 exact wiki base-record labels now ship under CC BY-SA 3.0.
Round 2 implements the evidence-supported read-only portions of Phases 2, 4 and 5. It adds bounded
extended part parsing, item/part stat-key diagnostics, full-identity owner-1 loadout joins, cyberware
slots with Unknown capacity/rules, all stored skills/point budgets and modern/legacy perk records,
F8 merged facts, typed garage/travel status and raw journal diagnostics. It does not claim the
complete gameplay semantics or write-oriented acceptance criteria of those phases.
Phase 3 actions, allocation/build mutations, world/story changes, structural inventory and new
versions remain open. No new setters or bulk actions were added in round 2. The following write-enable
round adds raw stored setters and shared F6 stack actions without claiming those full phases complete.

## Write-enable round (owner decision: enable writes, keep warnings)

Plugin 0.5.0 enables **nine write capabilities**: six Level/Street Cred level/XP/threshold fields,
existing raw saved-fact values (2,750 in this corpus), set-supported-stack-target and refill-to-opening-
quantity actions. Existing money, attributes and point writers remain. See the README's
`Enabled writes and their evidence` table. Console evidence is tracked separately in
[#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31); no field becomes Stable.

The six progression scalars stay Inferred; fact semantics stay Unknown. Descriptions/previews warn
that rewards, unlocks, callbacks, related facts and XP curves are not recalculated. CSAV has no known
container checksum, but opaque/console integrity algorithms are unknown; the game may reject edited
saves. Bounds distinguish editor policies, the observed fact range and original out-of-range values.
Exact version/package guards are unchanged. Missing `currentExp`/`expToLevel` names are appended
without renumbering old names. Class/package/node/chunk sizes and offsets are repaired and the
complete intended output is decompressed and reparsed inside Serialize(). Unknown bytes survive.

**Validation:** 125 passed, 0 failed, 0 skipped with `SAVETOOLKIT_ARTIFACTS` set in the same command;
API build 0 warnings / 0 errors. Tests include every new setter's persistence and undo/redo, invalid
values, exact four-byte fact patches checked by F1 ByteDiffer, all nonempty fact planes, fixed-size
already-materialized XP/threshold patches, untouched RED fields/chunks, omitted names and padding,
mixed attribute/point/proficiency edits, live inspection values and F6 atomic single-step action undo.
F6 tests cover document/explicit/empty/query scope, unloaded matches, exclusions, skipped money and
unsupported rows, warnings, invalid parameters, tampered/stale plans and direct-invoke refusal.
These are software checks, not console acceptance or controlled in-game deltas.
Conflicting stored/forced level copies refuse the level setter; a synthetic disagreement regression
checks this refusal, byte-identical preservation, and independently working XP editing.

### Owner validation for enabled writes (#31)

Use a spare slot, preserve originals, and record exact build/region/DLC/slot. Capture the idle control
pair first. For each edited save: restore, load, inspect the effect, re-save, reload, re-export and diff.

- [ ] Edit stored Level alone; record displayed level, stat mirror, points, rewards, perks/unlocks and
  unchanged inventory. Earn one level separately to reveal the actual transition dependencies.
- [ ] Edit Street Cred alone; record displayed cred, unlocks/rewards and persistence. Cross one normal
  in-game cred threshold in a separate controlled pair.
- [ ] Edit Level XP and Street Cred XP separately; note displayed XP and whether it is retained,
  clamped or recomputed. Capture one threshold crossing for each, with rewards and points before/after.
- [ ] Edit each stored XP threshold separately; record whether the game retains, ignores or recomputes
  it, then earn XP normally and inspect progression. Do not infer a legal XP curve from the editor cap.
- [ ] Edit one already-present, known noncritical fact at its exact table/hash; record visible before/
  after state, related quest stage/callbacks and subsequent forward play. No absent-fact insertion or
  complete-all preset is requested. Return a controlled in-game pair for that same event.
- [ ] Set targets for two supported existing ammo/component stacks; confirm the preview's skipped
  money/unsupported rows, actual quantities/use behavior and unrelated state after reload. Test healing
  charge/replenishment behavior separately from ammunition.
- [ ] After lowering two stacks in the editor, preview refill-to-opening-quantities; verify both in game
  and after reload. Confirm a third stack above its opening quantity is unchanged.
- [ ] Repeat existing money/attribute/point acceptance checks from the earlier checklist independently.

### Still disabled because the mutation path is incomplete

Inventory structure/ID/attachment/equipment changes require counts, duplicate prefixes, link/identity,
counter and dependent-stat repair not implemented here. Perk/cyberware mutations require effect and
capacity records that a scalar rank/slot toggle cannot maintain. Garage/travel creation and quest
presets require omitted-array/reference/event mutation paths. Other skills and global/profile files
have no prepared writer. Enabling these partial patches could leave unknown structure or references
inconsistent; warnings cannot supply the missing serializer. F5/F7 infrastructure is available but no
prepared structural or multi-file Cyberpunk writer existed in #13 to enable. No new SDK requests.

## Round-2 sample observations and limitations

Independent scratch Python framing/package inspection and the C# tests agree on 36 equipment areas,
7 occupied slots, 6 exact inventory matches, 10 extended records, 3 Unity child parts, 20 proficiencies,
4 point budgets, 6 modern groups without allocated perks and 116 legacy areas. Every cyberware slot
in this early save is empty. Item/part stats yield 48 rows; missing part keys are reported rather than
inferred from base IDs. Read-only budget queries reflect existing point edits and undo.

The 53,742-entry persistency node contains one garage class with 3 empty active slots and omitted
unlock lists. FastTravelSystem contains one point, exact TweakDB ID `0x0000002486F51E9C`, marker
`#hey_gle_dataterm_04`. Catalog-only vehicles are not labelled unavailable and catalog-only points
are not labelled discovered/locked. Populated modern perk/garage records have synthetic format
coverage; they still need owner captures. Journal diagnostics retain 594 opaque 15-byte records,
0 extra uint64 entries, 1 extra four-uint32 entry and 16 trailing bytes.

The merged facts collection holds 20,978 rows: 2,750 saved entries plus 18,228 hash-verified catalog
facts absent from the save. Absence is not zero. Canonical whitespace and all unknown saved hashes
are preserved, and no quest completion is inferred. Collection query results materialize at most
200 rows; attachment pages at most 128. Initial open/view measurements were roughly 0.46–0.5 seconds
in sample tests on the development machine (not a cross-hardware benchmark).
