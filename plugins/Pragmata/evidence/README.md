# Pragmata evidence v1 (2026-10-28)

`manifest.v1.json` records all 27 gameplay section classes and 138 direct fields
across the eleven gameplay saves: canonical class/field paths, MIT schema types,
stored types, minimum/maximum child counts and value-variant counts. Its 960
numeric or opaque-ID observations cover inventory, gadgets, weapons, perks,
objectives, occurrence records/events, checkpoints and chapters. Value confidence
is Verified (observed bytes); gameplay meanings remain Unknown. Section grouping
is Inferred from hash-confirmed names. GUIDs are SHA-256 fingerprints. No plaintext
payloads, artwork, game descriptions, account IDs or identity strings are included.

Source: [alphazolam RE_RSZ / praydog RSZ dumps](https://github.com/alphazolam/RE_RSZ/blob/871a1d5c4c7b81c60c966d73ee63f4c4413ab56e/rszpragmata.json),
[MIT license](https://github.com/alphazolam/RE_RSZ/blob/871a1d5c4c7b81c60c966d73ee63f4c4413ab56e/LICENSE),
retrieved 2026-10-28. Extracted the observed class/field subset, cross-indexed types
and added original sample statistics. Schema SHA-256:
`67e5ed72e0e9efff0287214ad36737a8959d799aa1510b6b76591731a21f4df2`.
Full MIT notice is in `../THIRD_PARTY_NOTICES.txt`. Original descriptions and
computed observation facts by Save Toolkit contributors are [CC0-1.0](https://creativecommons.org/publicdomain/zero/1.0/).
Provenance/tracking: [issue #21](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/21).

## F1 findings and reproduction

F1 lists **only generation 0**, twelve main directories and no backup twins in
`PPSA02530_saves_dec(1).zip`. Generation 0 vs latest is twelve unchanged files,
zero changed bytes. There are no appended generations available to establish a
controlled before/after operation.

Three exploratory comparisons from slot 0 to slots 1/2/3 found respectively
1,759,924 / 1,832,140 / 1,816,709 changed plaintext bytes and
74,523 / 75,948 / 80,661 canonical semantic changes, without input truncation.
These are uncontrolled slots, **not three causal operation captures**. No churn
is masked because no controlled no-op capture proves a safe mask. A second index
run reproduced `manifest.v1.json` byte-identically.

The production plugin publishes the SDK hook `pragmata-canonical-v1`. Sections
align by unique class hash, records by unique scalar IDs/fingerprinted GUIDs, and
unkeyed/duplicate records by positions with explicit incomplete-input warnings.
Unique primitive arrays align as value sets; physical reorder remains visible
in the byte diff. Strings and opaque values are fingerprinted. Unproven positional
identities set `inputTruncated` even if every node was emitted; capped/missing
paths never prove removal. Native-hook byte reports describe the encrypted DSSS
container; the historical exploratory counts above came from plaintext research
comparisons. Keep detailed reports under `%TEMP%`, outside the repository and
artifacts, and review them before sharing.

```powershell
$archive = Join-Path $env:SAVETOOLKIT_ARTIFACTS 'PPSA02530_saves_dec(1).zip'
$diff = 'tools/SaveToolkit.SaveDiff'

dotnet run --project $diff -- list $archive --format json --out (Join-Path $env:TEMP 'pragmata-generations.json')
dotnet run --project $diff -- list-decoders

dotnet run --project $diff -- compare $archive $archive --before-gen 0 --after-gen latest --decoder pragmata-canonical-v1 --format json --out (Join-Path $env:TEMP 'pragmata-f12-native.json')

# View decoder: F12 pages F8 inventory/evidence collections within shared budgets.
dotnet run --project $diff -- compare $archive $archive --decoder pragmata --max-query-rows 100000 --max-query-pages 1000 --max-query-bytes 33554432 --format json --out (Join-Path $env:TEMP 'pragmata-f12-view.json')
```

`compare` exits 0 for equal, 1 for different, 2 for error. No research executable
or production Testing dependency remains. The historical manifest is preserved;
its schema/index provenance remains above. Test-only plaintext fixtures exercise
the production hook without duplicating decoding or identity logic.

## Scope and owner checklist

The [completion audit](completion.md) supersedes earlier round-2 read-only/query
limitations and classifies every requirement. F9 graphs/maps, F13 browse/inspection
and F8 inventories above 200 integrate with the fixed-size writer. F12 is now
adopted through the SDK hook and shared bounded traversal. No structural or new
gameplay-family write is added in this follow-up.

Phase 1 indexing/harness and Phase 2 reference integration/read-only navigation
are implemented. Causal semantics, actual item dictionaries, mapped locations and
verified completion totals remain open. The write-enable decision supersedes the
old Phase 3 evidence gate: the six prepared existing-value surfaces now write with
warnings and conservative editor bounds. Captures validate those effects separately
in [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31); Phase 4 still
requires a lossless structural writer. See the README's enabled-writes table.
Round 2 adds F8 evidence/quest panels, six attributed MIT mission-item links and
F6 review for the existing quantity operation. [Round-2 findings](round2.md)
separate synthetic storage-preservation evidence from missing causal captures.
Fixed-size no-op identity is not proof of a lossless structural serializer. F5 is
available, but no structural operation is exposed and maturity stays Experimental.

- Record title ID, build, region, DLC, difficulty/mode, slot and displayed before/after
  values. Include system and gameplay files; keep identity data private.
- First capture an unchanged save/re-save pair to measure clock/timestamp churn.
- Supply **three independent before/after pairs per family**: one known item pickup,
  use and purchase; gadget/shortcut equip and unequip; weapon purchase/equip;
  perk upgrade/equip; objective and occurrence-event completion; checkpoint unlock;
  chapter transition; named collectible pickup. Capture each operation separately,
  changing only one thing per pair, with screenshots or notes identifying the value.
- Include empty/full inventory and examples across difficulty, chapters and cleared/NG+
  modes. Supply licensed ID-to-name evidence or a user-owned extraction pack.
  PC runtime observations need matching PS5 deltas; PC format equivalence is unconfirmed.
- On a spare console slot: restore/export unchanged, load, re-save, reload; then test
  one small existing-quantity edit through the same cycle and confirm the displayed
  effect. Next test one existing value through each newly enabled Gadgets, Loadout,
  Progress, Quests and World tab, plus one F4 item-ID replacement. Record exact
  paths and displayed old/new values, inspect dependencies and sibling files,
  and re-export after re-save/reload. Structural tests follow only after a lossless writer exists.
  Only a human may mark a tested subset Stable.
- For the six mission-opening-condition IDs `558265447`, `1887847758`, `1942031930`,
  `3036423139`, `3090659029`, `3746788365`, record the displayed item and the three
  mission unlocks separately; include before/after inventory quantities and mission
  selectability. Do not assume these are named Training Data without that observation.
- Supply an attributed ID/name extraction with the exact uint32 ID, internal
  `app.ObjectIDs` name, `getKindHash` category, localized `getItemData` name/detail,
  build, locale, platform and source/license. Include a quantity cap only with an
  observed boundary pair (last accepted pickup/purchase and next rejected/clamped
  operation), plus the relevant difficulty/mode. Do not substitute reward amounts
  or storage bounds for caps. Omit account/identity fields and credentials.
- Validate one existing quantity change through F6 preview/apply, undo, redo,
  export/re-import and console load/re-save/reload. Check displayed quantity,
  equipped state, unlock state and sibling system/gameplay files independently.
- For a real inventory above 200 rows, repeat with a filtered all-matching scope
  and one excluded row. Confirm all selected quantities and the unchanged exclusion.
- Pair a single checkpoint/hatch unlock with its displayed location and raw hash;
  capture mission selectability/chapter prerequisites separately from inventory
  quantities. The F9 map/graph deliberately shows Unknown saved states; no geographic
  mapping or full unlock rule is implied by the reference data.
