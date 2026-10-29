# Third-party notices

## Witcher Wiki reference data

`ReferencePacks/witcher3.catalog.en/{items,abilities}.json` and its source index adapt data from
[Witcher Wiki contributors](https://witcher.fandom.com/wiki/Category:The_Witcher_3_items), retrieved
2026-10-28, under [CC BY-SA 3.0 Unported](https://creativecommons.org/licenses/by-sa/3.0/).
Individual articles and alternate labels link pinned revisions; article histories identify contributors.
[Fandom licensing policy](https://www.fandom.com/licensing) supplies the source terms.

PS5 Save Game Toolkit contributors normalized categories, adapted introductory wiki prose, condensed
skill concepts, selected native joins and annotated uncertainty/unpublished IDs. The asset round
adds 150 wiki-transcribed tooltip/effect descriptions with pinned sources and historical/cross-build
scope. These adaptations
and the source index remain CC BY-SA 3.0. Retain notices, pinned article links, modification notices
and the same license when redistributing or adapting this data. The independent plugin code license
is unchanged. See [CATALOG.md](CATALOG.md) and each pack's `data.source.md` for exact scope.

## Gwent community catalog and parser research

The separate MIT `witcher3.gwent.en` dataset adapts six-faction catalog data and reference stats from
[GwentTracker at d8eab8c](https://github.com/vishalgupta-git/GwentTracker/tree/d8eab8c25c409be42a0ea7b43b901a43dfefed18/data).
Full notices: [GwentCatalog-LICENSE.txt](GwentCatalog-LICENSE.txt), copyright 2017 Chris, and
[GwentTracker-LICENSE.txt](GwentTracker-LICENSE.txt), copyright 2026 Vishal Gupta.
Names/types/acquisition notes/copy expectations were normalized and explicit aliases expanded;
annotations distinguish tracker targets from legal limits. The same project's MIT facts parser
informed bounded framing research; no downloaded code was executed.

## Journal format research

Journal/resource/GUID/status and facts research includes
[w3-quest-tracker at 6078f8f](https://github.com/dodojesuslol/w3-quest-tracker/blob/6078f8f64a1ede3a304452bfe3cff710ae1d554e/docs/save-format.md),
MIT, copyright 2026 dodojesuslol. Full notice: [QuestTracker-LICENSE.txt](QuestTracker-LICENSE.txt).
Independent bounded readers are calibrated to the supplied PS5 schema. PC version claims,
heuristics and quest-completion interpretations are not adopted.

## Existing parser adaptations

Existing MIT parser notices remain in [W3SavegameEditor-LICENSE.txt](W3SavegameEditor-LICENSE.txt) and
[SaveConverter-LICENSE.txt](SaveConverter-LICENSE.txt). No game scripts, XML, original localization
archives or letter/card flavor text are bundled. Game-script mirrors remain research-only.

F14 socket occupancy research additionally used CD PROJEKT RED's `PlayerAbilityManager`, mirrored
and modified by Lknechtli/BetterCombatEvolved at
[a85e52c](https://github.com/Lknechtli/BetterCombatEvolved/blob/a85e52c3e3841863999b45afc2a0fd1f284a6bc0/Witcher3/content/scripts/game/gameplay/ability/PlayerAbilityManager.ws).
Rights reserved by CD PROJEKT RED; used with attribution per owner decision 2026-10-29.
No script code is copied; conservative occupancy tests were independently implemented and checked
against PS5 bytes. [Research sidecar](SaveToolkit.Plugins.Witcher3/AbilitySlots.source.md).

## Publisher game artwork

Rights reserved by CD PROJEKT RED; used with attribution per owner decision 2026-10-29.
The 371 icons and five reference maps are separate F2 assets datasets, not MIT/CC BY-SA artwork.
This is an unofficial fan work and is not approved or endorsed by CD PROJEKT RED.
Witcher Wiki, GwentTracker and witcher3map hosting/extraction contributors retain attribution;
the map host identifies its community work as CC BY-NC-SA. No community marker/coordinate dataset
or code is imported. Exact sources, hashes, modifications and processing notices are retained in
the [asset sidecar](SaveToolkit.Plugins.Witcher3/ReferencePacks/assets.source.md), its JSON audit
and [ASSETS.md](ASSETS.md). Original text/catalogue licences and full MIT notices remain applicable.
