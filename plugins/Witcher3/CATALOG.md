# Witcher3 reference catalog snapshot — 2026-10-28

The starter pack is replaced by two attributed catalogue F2 packs; the 2026-10-29 asset round
adds separately attributed icon datasets and a third regional map pack (see [ASSETS.md](ASSETS.md)).
This is the complete extraction of documented
entries in the reviewed source snapshot, including entries with unpublished save IDs. It is **not**
an installed PS5 database or proof that every internal, cut, modded or newer-build ID is known.
Every gameplay mapping remains **Inferred**.

| Dataset | Records | Coverage and identity |
| --- | ---: | --- |
| `items` | 4,011 | 3,670 native IDs, including 190 explicit `NGP ` IDs; 341 additional wiki references with unknown native IDs |
| `abilities` | 107 | 95 named pre-overhaul skills (25 Combat, 25 Signs, 25 Alchemy, 20 General), plus 12 Blood and Wine mutations |
| `gwent` | 225 | All 215 named definitions in the licensed six-faction catalog, expanded to preserve 10 explicit alternate native indices |

Every record has a name, category, description, confidence annotation and pinned source. No pack
sets a legal write limit. Selected icons and 150 wiki-transcribed tooltip/effect descriptions now
ship with separate source/scope metadata. No game scripts, XML, original localization archives,
card flavor text or letter contents are bundled. Wiki-derived text remains CC BY-SA; MIT card
metadata and publisher artwork are separate datasets with their own attribution.
The [source index](SaveToolkit.Plugins.Witcher3/ReferencePacks/witcher3.catalog.en/source-index.json)
lists all 3,464 wiki revision URLs used, including alternate-label sources.

## Wiki extraction and share-alike

Source: [Witcher Wiki contributors](https://witcher.fandom.com/wiki/Category:The_Witcher_3_items),
[CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/), per
[Fandom licensing policy](https://www.fandom.com/licensing). Retrieved 2026-10-28 through the public
MediaWiki API. Article page IDs, revision IDs and timestamps accompany derived records; `?oldid=`
URLs pin their content. Contributor histories remain accessible from those pages. These derived
datasets and their source index remain **CC BY-SA 3.0**. Retain attribution, revision links,
modification notices and the same license when redistributing or adapting them. This does not
change the license of the independent plugin implementation.

The review traversed 73 item/ability/Gwent categories and template transclusions, examining 4,004
pages and 4,299 relevant infoboxes. Balanced templates supplied explicit IDs, labels and types.
Wiki-authored introductory prose supplied descriptions where available; otherwise short classification
summaries and uncertainty notes were written. The initial extraction omitted effects/artwork;
the asset round subsequently adds the selected attributed content documented in ASSETS.md.
Food/drink and base/HoS/B&W junk list tables supply otherwise unrepresented labels. Unkeyed item
infoboxes supply books, notices, enchantments and other references. Redirect-only pages and current
skill/mutation item infoboxes are excluded from unkeyed items.

Native IDs are case-sensitive and preserved exactly. Duplicate IDs use the most specific page
(fewest infoboxes, then shortest title, then ordinal title); alternate labels and pinned sources are
retained. This resolves 258 conflicting-label groups without treating base/legendary labels as proof
of interchangeable items. Explicit NG+ IDs remain separate. Display names alone never join an
inventory record. `wiki-entry/<pageid>/<name-hash>` keys explicitly identify references with
unpublished native IDs: they support browsing only and cannot create saved items. Names already
represented by a native-keyed item are not duplicated as unkeyed references.

## Abilities: older definitions

The 2026 overhaul changed current wiki skill pages. The older sample schema instead uses the
preserved pre-overhaul pages:

- [Combat, revision 649876](https://witcher.fandom.com/wiki/The_Witcher_3_abilities_(obsolete)/Combat?oldid=649876).
- [Signs, revision 649877](https://witcher.fandom.com/wiki/The_Witcher_3_abilities_(obsolete)/Signs?oldid=649877).
- [Alchemy, revision 649878](https://witcher.fandom.com/wiki/The_Witcher_3_abilities_(obsolete)/Alchemy?oldid=649878).
- [General, revision 649879](https://witcher.fandom.com/wiki/The_Witcher_3_abilities_(obsolete)/General?oldid=649879).
- [Mutations, revision 617694](https://witcher.fandom.com/wiki/Mutations_(Blood_and_Wine)?oldid=617694).

Names, categories and condensed concept descriptions derive from these CC BY-SA pages. Numerical
effects, costs and prerequisites are not presented as PS5 rules. Native enum/ability-name/subpath/tier
joins were corroborated against saved definitions and a
[research-only game XML mirror, pinned 66684cb](https://github.com/aschleg/witcher-3-enemy-scaling-and-other-changes/blob/66684cbe57eb20286f9ce65e551a4a0313543592/mod/gameplay/abilities/geralt_skills.xml).
That repository has no reusable license; neither its code nor game descriptions/assets are embedded.
Native suffixes are not display order: `S_Sword_s01` is Whirl; `S_Sword_s21` is Muscle Memory.
All 95 named skills join in all eight samples. Core `S_Perk_08`, six unnamed skill definitions and
the mutation sentinel retain raw/default identities; no friendly skill was invented for them.
Mutation numbers are corroborated by saved icon-path identifiers; the asset round now includes
their matching wiki-hosted publisher icons. No new native gameplay dependency is inferred.

## Gwent: MIT community data

Pinned source: [GwentTracker, d8eab8c](https://github.com/vishalgupta-git/GwentTracker/tree/d8eab8c25c409be42a0ea7b43b901a43dfefed18/data).
The six `catalog/*.yml` files retain Chris's 2017 MIT notice; `card-stats.json` and missable metadata
retain Vishal Gupta's 2026 MIT notice. Both full licenses are shipped as
[GwentCatalog-LICENSE.txt](GwentCatalog-LICENSE.txt) and [GwentTracker-LICENSE.txt](GwentTracker-LICENSE.txt).
Names, factions, kinds, acquisition notes, missable quest/cutoff advice, expected copies and reference
strength/row/ability annotations were normalized into F2 records. Downloaded code was read, never run.

All base/HoS/B&W definitions and four summoned transformations are included. Alternate indices remain
individual keys with `canonicalIndex` as a reference relation; saved copies are never silently merged.
Tracker targets are **not** legal save/deck caps or achievement criteria. Numerical stats entries
26/27/28 lack named catalog definitions and are not assigned invented names. They are absent from
the samples; unknown indices retain raw saved values. All 27 observed indices (35 copies, four
leaders) join. Decks, actual quest cutoffs and availability remain unverified; absence is shown as
“not recorded; availability unknown.”

## Maintenance and validation

Updates must pin fresh revisions/commits, preserve notices, review changed mappings and recompute
manifest SHA-256 values from UTF-8 LF dataset bytes. Do not replace the older ability crosswalk with
the overhaul's skill order. Tests audit every record, keys, descriptions, pinned attribution,
asset resolution/hashes/provenance, absence of write limits, variants, bounded paging and sample joins. Raw source snapshots stayed in
temporary scratch storage; no game/sample dump is shipped. See [EVIDENCE.md](EVIDENCE.md) for gates.
