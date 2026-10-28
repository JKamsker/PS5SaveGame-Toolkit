# Corpus evidence and remaining validation

Observed **2026-10-28**. Machine: Windows x64, .NET SDK 10.0.400.
No controlled console capture or load test was performed. The plugin remains Experimental.

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

`CyberpunkEvidenceDecoder` is a read-only **test-side F1 `ISaveDecoder` hook**, independent
of UI pages. It emits version fields, quantities by container + full 16-byte instance identity,
opaque instance-body fingerprints, and raw facts scoped by table node + hash. Table node IDs
are an explicit current-tree scope, not a proven persistent cross-save table identity.
The standard F1 builder reports duplicate identities rather than silently overwriting values.
The hook honors file/value bounds and produces a truncated tree when limits are reached.
It intentionally does not claim complete RED-property/gear-stat decoding.

Tests decode the real sample explicitly and use `SemanticDiffer` to detect a locally constructed
600→601 ammo patch by full identity, plus its changed body fingerprint. That synthetic change
tests the hook and writer; it is **not** evidence from firing ammunition on a console.
The shared CLI `PluginViewDecoder` does not visit F8 rows yet; use the test-side hook for complete
inventory/fact comparisons, rather than treating UI-loaded pages as the whole model.

## Verification

```powershell
dotnet test --project plugins/Cyberpunk2077/SaveToolkit.Plugins.Cyberpunk2077.Tests
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
```

Roadmap round: **85 passed**, no skips with samples; **31 passed / 54 skipped** with an empty
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
- [ ] **Data/rights:** choose the distribution policy for richer text/icons; provide a lawful
  same-build TweakDB/localization export as a separately attributed local F2 pack if desired.
  Include resource hashes, locale, DLC coverage and permissions. No artwork is requested for
  repository bundling without clear redistribution permission; no game/cloud credentials needed.

Later phases still need the plan's level/Street Cred threshold, skill reward, perk-rank buy/refund,
Relic-access, car acquisition/summon, travel discovery and specific quest-stage pairs, plus later
PS5 schema samples (277/2500 only if actually obtained). No new writes are enabled for those areas.

## Phase status

Phase 1 identifier/evidence work is complete with explicitly limited coverage. Full licensed
same-build localization remains CP2077-R2/R8/R18 owner/data work. Phase 2's attributed rich inventory
inspection, whole-list queries and local-pack integration ship; extended stat/attachment parsing,
equipment ownership joins and installed-cyberware semantics remain blocked on CP2077-R1/R2 evidence.
Phases 3–6 remain open. No replacement, structural, world or new progression writers were added.
