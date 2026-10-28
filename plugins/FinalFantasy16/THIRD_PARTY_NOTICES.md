# Nenkai / 010GameTemplates

Schema research used by this plugin comes from
https://github.com/Nenkai/010GameTemplates/blob/f31430e32f099d66cc22d609a7926988e041c409/Square%20Enix/Final%20Fantasy%2016/FaithSaveFile_Sub.bt

MIT License

Copyright (c) 2024 Nenkai

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## Additional schema research and reference annotations

The PartyData, EquipmentData, SkillData and common PlayerData readers and
fixed-width writers (including the shared context layout and range-measurement
research script) adapt
the wire layout in Nenkai/010GameTemplates, revision
`4a3685f9b63f72ca49e24ac2f18420aaad2762b8`. The 2024 MIT notice above applies.
Downloaded templates were inspected as text and never executed.

The `ffxvi.nex.en` map/summon datasets adapt MIT research annotations from
[Nenkai/ffxvi-nex-layouts](https://github.com/Nenkai/ffxvi-nex-layouts/tree/d3ad0627a29af168916ad574dadbca6759e2ac48).
Item/EquipItem/Skill/ParamGrow layouts were consulted for boundary/dependency
research; no unproven caps, skill-index mapping or growth data were manufactured.
FF16Tools save checksum research was also consulted (revision
`b87c2f22a8ea630cb03f7b2198af84c7709a1b0c`); no PC wrapper is emitted or executed.
Both projects carry the following notice:

MIT License

Copyright (c) 2025 Nenkai

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## Wiki-derived reference data (separate from code)

`ReferencePacks/ffxvi.wiki.en/items.source.tsv` and `items.json` adapt English
names/categories from Final Fantasy Wiki contributors, with an original curated
factual ID crosswalk. Retrieved 2026-10-28. This adaptation remains under
[CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/).
Retain attribution, links to source revisions/history, modification notice and
share-alike terms when redistributing or adapting this data. No wiki prose,
images, game icons or art are included. Third-party media can have different terms.
The source URL/version, per-record provenance, modifications and coverage are
listed in the pack's `data.source.md` and `pack.json`, and in README.
The published ID CSVs are now imported in a separate pack under the owner decision
below. They are not relicensed by the wiki pack's CC BY-SA notice.

Round 2 extends the CC BY-SA adaptation with pinned Fafnir, Wall of Memories,
Journal, Patron's Whisper and Joshua Rosfield labels, normalized aliases and
factual upgrade suffixes. Four explicitly named item IDs from Nenkai's MIT
UseItem.layout are included with their original notice above and per-record
origin. Skill.layout's three explicit skill-ID annotations remain in the MIT Nex
pack, separate from the save-rank bytes. All joined labels remain Inferred.

## Published community identifiers — Nenkai/ffxvi-modding

Author: Nenkai and ffxvi-modding contributors. Retrieved 2026-10-29 from
[revision 4b5e7eb349d2decd776b60b0f2d0f2af7aaaac03](https://github.com/Nenkai/ffxvi-modding/tree/4b5e7eb349d2decd776b60b0f2d0f2af7aaaac03/docs/resources/ids).

**No licence published; reused with attribution per owner decision 2026-10-28**.
This is an owner-authorized data import, not a claim of an MIT or CC BY-SA grant.
The licence fields and source-tree links state the actual source status.

`research/published-ids` retains the five identifier CSVs and their pinned hashes;
`ReferencePacks/ffxvi.published.en` contains 879 named item/equipment IDs, 98 map
IDs (110 variant rows), 1,758 battle NPC IDs and 1,085 event NPC IDs. Named facts
are imported; unnamed rows are omitted in derived catalogs, documented display
tags are removed, map variants grouped, and original inferred categories/warnings
added. The raw CSV facts remain unchanged. Source sidecars and README record
per-file provenance, modifications and hashes. Every dataset/record carries
attribution through F2 and the UI, and Info.References lists all five pinned files.

No upstream code, art, icons, images, prose, save bytes or map coordinates were
imported. Earlier CC BY-SA and MIT packs keep their own notices and licences.
No other community source was refused solely for lacking a licence in this plugin.

## Eikon ability reference progression (completion round)

`ReferencePacks/ffxvi.abilities.en/abilities.source.tsv` and `abilities.json`
adapt 48 names, Eikon relationships and selected individual AP facts from Final
Fantasy Wiki contributors, retrieved 2026-10-28. Every source revision is pinned
in the TSV and records; the base-game cross-check is
[navbox revision 3770527](https://finalfantasy.fandom.com/wiki/Template:Navbox_abilities_FFXVI?oldid=3770527).
Leviathan and Ultima additions use pinned character articles. Original slug keys,
reference stage chains and warnings are added. The adaptation remains
[CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/); retain attribution,
revision/history links, modification notices and share-alike terms. No art,
icons, prose, game saves, save skill-index joins or completion state is included.
Missing AP annotations remain Unknown. See the pack sidecar and README source table.

## Vanilla Eikonic feat references — Logos-Unleashed

`ReferencePacks/ffxvi.feats.en` adapts seven vanilla command-name pairs and eight
summon-mode relationships from mThund3R's restoration code in
[Logos-Unleashed, revision 6424cb4e60bd31005ef7f0cfb42d4e5360660fc6](https://github.com/mThund3R/Logos-Unleashed/blob/6424cb4e60bd31005ef7f0cfb42d4e5360660fc6/LogosUnleashed/Mod.cs).
No upstream code is imported/executed; custom/modded commands and ambiguous
restoration paths are omitted. See the pack sidecar for selection and limitations.
Original [licence](https://github.com/mThund3R/Logos-Unleashed/blob/6424cb4e60bd31005ef7f0cfb42d4e5360660fc6/LICENSE):

MIT License

Copyright (c) 2025 mThund3R

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
