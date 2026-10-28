# English item/equipment label extract — CC BY-SA 3.0

Author: Final Fantasy Wiki contributors; original selection and factual ID crosswalk
by PS5 Save Game Toolkit contributors. Retrieved 2026-10-28.

Sources:
- [Final Fantasy XVI translations, revision 4007473](https://finalfantasy.fandom.com/wiki/Final_Fantasy_XVI_translations?oldid=4007473), English columns of Weapons, Belts, Vambraces, Accesssories (upstream spelling), Consumables and Materials.
- Resource labels: [Gil, revision 4063996](https://finalfantasy.fandom.com/wiki/Gil?oldid=4063996), [Renown, revision 3741166](https://finalfantasy.fandom.com/wiki/Renown?oldid=3741166), [Ability Points, revision 4035790](https://finalfantasy.fandom.com/wiki/Ability_Points?oldid=4035790).
- [Fandom licensing guidance, revision 3983537](https://community.fandom.com/wiki/Help:Licensing?oldid=3983537) specifies CC BY-SA 3.0. The source wiki's API `action=query&meta=siteinfo&siprop=rightsinfo` declares `CC-BY-SA`, linking [the licensing page](https://www.fandom.com/licensing).

The extracted names/categories, reviewed `items.source.tsv`, and generated
`items.json` adaptation remain **[CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/)**.
Retain attribution, source links, modification notices and share-alike terms.
The source page/history links identify the contributing authors. Code is separately licensed.

Modifications: selected 202 wiki-backed item/equipment labels plus 3 resource labels;
normalized categories to lower-case singular keys; assembled a minimal factual numeric
crosswalk; emitted plain text only. No wiki prose, Japanese translations, images,
icons, caps or game availability rules are included. The source wiki has incomplete
coverage; names absent there, including upgraded variants, are **not** filled by guessing.

Nenkai's [published numeric ID research](https://github.com/Nenkai/ffxvi-modding/tree/4b5e7eb349d2decd776b60b0f2d0f2af7aaaac03/docs/resources/ids)
was consulted to research identifier associations. Its unlicensed CSV files are not
vendored or claimed to be licensed by the wiki. This pack is an independently
curated selection of wiki-derived labels and factual identifiers, not a copy of the
CSV dataset. Those joins remain **Inferred**, pending controlled gameplay checks.

Locale `en`; build-agnostic **labels only**. No assertion of PS5 patch, DLC or NG+
compatibility. 105/172 distinct observed retail main IDs and 2/2 demo IDs are named;
all other IDs, including 900000, remain explicitly unmapped. Zero stacks are retained.

Regenerate deterministically offline:
`python -I plugins/FinalFantasy16/research/build_catalog.py`.
The converter validates canonical/duplicate IDs and plain-text names, and hashes
LF UTF-8 JSON for F2. The host validates the embedded manifest and hashes again.
No runtime network or filesystem access is used.
