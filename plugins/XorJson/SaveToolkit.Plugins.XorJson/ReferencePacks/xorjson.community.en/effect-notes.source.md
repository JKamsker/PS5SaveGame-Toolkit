# Effect reference facts and original summaries

This is the historical pack-1.3.0 extraction record. Pack 1.4.0 retains these
facts and adds official game descriptions/icons under the owner decision of
2026-10-29; see game-assets.source.md and game-assets.sources.json. Its combined
dataset attribution now records publisher rights as well as MIT identifiers.

Author/source: dungeons.tools community author,
https://www.dungeons.tools/2/effects (individual page URLs on every record).
Retrieved: 2026-10-29. Source revision: PC version 1.1.2.0, Steam build 25754144.
The website publishes no immutable revision URL; the downloaded HTML is pinned
by SHA-256 `fce93f92b4d8fcaf99b9d95069a0de34d7dc0694d81009aea44a61e232f580b6`.

Licence: **No licence published; reused with attribution per owner decision 2026-10-28**.
Terms/provenance URL: https://www.dungeons.tools/privacy (not a licence grant).
Privacy HTML SHA-256: `829cb2ecaf92eeec62c6ef8a9da903ab2d7e639fa47a56fe0f53855e17ee877c`.
This pack-1.3.0 factual extraction redistributed no source HTML, game prose or
downloaded images. Pack 1.4.0's separate game prose/image import is documented in
`game-assets.source.md`; no source HTML is embedded.

## Separate identifier provenance

The existing 60 IDs, English names and categories remain from Ishiaki and
mcd2-save-editor contributors, MIT, revision
`84ffee67b73e49d5b888e31257ce9767aa8ab826`:
https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/data/effects.json

The full MIT notice is retained in `LICENSE.txt`; `catalogs.source.md` describes
the original conversion. Every enriched record retains `identifierSource` and
`upstreamRevision`. The combined dataset licence field records the supplement's
status and does not revoke MIT permissions for the original identifiers.

## Extraction and modifications

1. Read the retrieved HTML as data with Python's HTML parser; execute no page
   scripts. There are 76 `a.g2-ht-row` rows. Decode their `data-tip` JSON and
   retain each detail-page link and displayed `GEAR` count.
2. Match all 60 existing catalog names to unique exact English names in those
   rows. No fuzzy names, internal-ID guesses or translated-name assumptions.
   Preserve each original ID, name and category. The 16 unmatched source rows
   are omitted because their catalog-ID crosswalk is unestablished.
3. Record compatible gear counts and percentages from the tier I–III displays.
   Each of the 60 matches has three displayed tiers (180 tier observations).
   Preserve percentage strings; do not convert them into serialized `Strength`,
   legal bounds, effect templates, enchantment values or write constraints.
4. Write original short summaries of the factual behavior; do not copy the
   game's tooltip sentences. Describe the PC version, compatible entry count,
   display percentages, inferred crosswalk and PS5 uncertainty in each description.
   Preserve a raw-ID source link and snapshot hash on every record.
5. Hash normalized LF `effects.json` in `pack.json`; advance the pack to 1.3.0.
   The source snapshot hash identifies upstream input; the manifest hash checks
   the derived embedded resource. They are intentionally different hashes.

Result: **60 enriched effect records**, **180 tier observations**, no new IDs,
no serialized strengths, no assets, no coordinates and no validator changes.
Regeneration must use the pinned HTML; later live HTML can legitimately differ.
Download/conversion scratch files remain in the system temporary directory.

## Evidence boundary and UI

The single PS5 character has five inventory entries and every `Effects` array is
empty. Thus **zero populated effect records** corroborate these PC descriptions
or tiers. Exact-name matching is Inferred, not PS5 compatibility evidence. The
existing Experimental warning, version/online gates and unknown-field behavior
are retained. F4 candidate details and raw scalar field descriptions display
the summaries and dataset attribution; Sources lists the combined provenance.

## Earlier exclusions (superseded for publisher assets)

- MetaBot methodology identifies its descriptions as game translation/template
  text and icons as game textures/models:
  https://metabot.gg/en/minecraft-dungeons-2/methodology
  (PC 1.1.1.0; retrieved 2026-10-29; HTML SHA-256
  `5d3513a297403dca1a157e62855e458580087c8e9702f2a23117eee12f563d00`).
- MetaBot general terms restrict use to “personal, non-commercial use”; they
  separately allow attributed aggregate-data reuse. No direct import relies on
  that exception for publisher game prose/assets:
  https://metabot.gg/en/legal/terms-of-service (effective 2026-04-01;
  retrieved 2026-10-29; HTML SHA-256
  `87affdb5a94632a778185bdcf0666c6e1f151ba727d756d08125bdaf76c19520`).
- The earlier refusal of dungeons.tools game wording/icons/art is resolved by
  the owner decision of 2026-10-29. Pack 1.4.0 imports that content with publisher
  attribution; see game-assets.source.md. MetaBot is not a direct import source
  this round; its separate community-data terms are recorded above.

README `## Licence exclusions` records reasons, alternatives and unblock routes.
