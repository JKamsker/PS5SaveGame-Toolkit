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

The new PartyData, EquipmentData, SkillData and common PlayerData readers adapt
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
The unlicensed published ID CSVs were consulted as research and are not vendored
or re-licensed by these notices.

Round 2 extends the CC BY-SA adaptation with pinned Fafnir, Wall of Memories,
Journal, Patron's Whisper and Joshua Rosfield labels, normalized aliases and
factual upgrade suffixes. Four explicitly named item IDs from Nenkai's MIT
UseItem.layout are included with their original notice above and per-record
origin. Skill.layout's three explicit skill-ID annotations remain in the MIT Nex
pack, separate from the save-rank bytes. All joined labels remain Inferred.

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
