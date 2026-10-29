# Rift Apart reference data, version 3.0.0

Retrieved 2026-10-28. Sources are pinned where revisions are available.

`schema.json`, `item-types.json` and `world.json`: Neptuwunium / Rivet,
[rcra_ddl.json at a631c96ed6455f9873a646825d37883f99defa6e](https://github.com/neptuwunium/rivet/blob/a631c96ed6455f9873a646825d37883f99defa6e/docs/ddl/rcra_ddl.json),
under [CC0-1.0](https://creativecommons.org/publicdomain/zero/1.0/), confirmed by the
[file-specific license](https://github.com/neptuwunium/rivet/blob/a631c96ed6455f9873a646825d37883f99defa6e/docs/ddl/rcra_ddl.json.license).
Selected 86 field names/containing types, five item type IDs/names and twenty
location/spybot labels from the schema enum ordinals. The ordinal/PS5 array and bit
joins are inferred, cross-checked against every supplied primary gameplay record.
Location index 0 and reserved spybot bits remain unresolved. Omitted runtime
offsets, defaults, prose and all EUPL generated C#. Added original uncertainty notes.
Schema widths are not PS5 offsets. Type ID joins are checked against real record headers.

`weapons.json`: Ratchet & Clank Wiki contributors,
[Rift Apart menu transcript, revision 403465](https://ratchetandclank.fandom.com/wiki/Rift_Apart_menu_transcript?oldid=403465).
This derived dataset, including original short annotations and candidate path joins by
PS5 Save Game Toolkit contributors, is distributed under
[CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/).
Retain attribution, source links, modification notice and share-alike terms.
Selected twenty names and short paraphrases. All twenty config paths hash to observed
PS5 inventory tokens. This proves the path/token join, not the localization alias,
ownership, level or availability. No prices or gameplay limits. Asset-round additions below supersede the earlier media exclusion.

`armor.json`: Ratchet & Clank Wiki contributors,
[Rift Apart armor, revision 402364](https://ratchetandclank.fandom.com/wiki/Rift_Apart_armor?oldid=402364),
plus the CC0 schema above. Selected set names and paraphrased bonus categories;
combined them with 57 schema VanityBundleSaveId names/ordinals and original inferred
friendly set aliases. Emperor/Reaver entries are explicitly internal/unresolved.
Catalog records do not establish that content is available in this save/build.
No bonus percentages, color limits or equipped-state rules are supplied.
The entire derived armour dataset, including toolkit annotations/joins, remains
under [CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/).

Revision IDs were resolved through the wiki's MediaWiki revisions API; public page
text was researched through indexed wiki pages. Direct page/content requests returned
HTTP 403 during this round. Keep author/source/modification notices and share-alike
terms with both derived wiki datasets; these terms do not relicense plugin code.

Scope: PPSA01474, English labels, unknown game build. The pack supplies no ownership,
entitlement, difficulty, level, ammo-cap or wallet rules. Unknown paths
and hashes remain visible, and Open(file) works without any reference context.

## Asset round — 2026-10-29

Added 20 base-weapon and 51 armour-piece authored menu-description synopses, plus
`weapon-upgrades.json` with 20 upgraded-form reference names/synopses. Menu source
remains revision 403465, armour bonus facts remain revision 402364. The entire
derived records datasets remain CC BY-SA 3.0, including toolkit annotations.
The 51 changed armour records now link to the menu source containing their piece
text; existing set/bonus facts still derive from armour revision 402364 above.
Added `assetId` for 20 weapons, 51 armour candidates and 19 upgraded forms.
Artwork has separate rights-reserved provenance in [images.source.md](images.source.md)
and `images.provenance.json`; record share-alike terms do not relicense artwork.
No pricing, costs, numerical caps, ownership or grid-node joins were added.
