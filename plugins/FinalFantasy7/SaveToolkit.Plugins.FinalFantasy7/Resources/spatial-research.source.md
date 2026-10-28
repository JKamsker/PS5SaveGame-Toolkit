# Folio, equipment-reference and research candidate attribution

Source project/author: RyudoSynbios / Game Tools Collection, copyright (c) 2024 RyudoSynbios.
Retrieved 2026-10-28. Licence: [MIT](https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/LICENSE.md).
Full notice is retained in `../../THIRD-PARTY-NOTICES.txt`.

- [Pinned Rebirth template](https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-rebirth/saveEditor/template.ts)
- [Pinned Rebirth shift overrides](https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-rebirth/saveEditor/utils.ts)
- [Pinned Remake enemy names/indices](https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-remake/saveEditor/utils/resource.ts)
- [Pinned Rebirth enemy names/indices](https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-rebirth/saveEditor/utils/resource.ts)
- [Pinned Remake enemy record layout](https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-remake/saveEditor/template.ts)

`remake-enemies.json` / `rebirth-enemies.json`: 161 / 230 unique source enemy indices and names,
retaining Remake main/INTERmission groups. Source display numbers are labels, never record indices.
Independent read-only layouts: Remake `0x42FAC + index*8`, u32 defeated at +4; Rebirth/demo
`0x450D0 + index*16`, u16 defeated at +4. Entry/assessed/staggered are bits 0/1/5. Other flags,
padding and counters are preserved. No battle/assessment/reward writer is implemented.
`research/build_enemy_data.py` checks resource SHA-256 values
`f1209a4913eba248ab545a7fd2c5d7ab6feaa861ede7ef0447c307f6cde051bf` (Remake) and
`28cda367f9c1f3243af28791ae7b1e0db08ec035e549dafbcaf637828d63f761` (Rebirth).

The read-only Rebirth relationship summary uses source `0xA8 + character*0x100` for source
characters 1–5 only (the upstream override hides the other records). Craftsmanship's source
level and XP controls both address `0x3DB8`; this ambiguity is retained as one raw u32, not two
independently editable values or a guessed rank. Relationship values are not labeled percentages.

`rebirth-folios.json`: extracted 238 distinct, visible literal folio bit labels/addresses and
source character/category grouping. `overrideShift` removes the outer character shift for these
global bits; `overrideItem` selects the matching character group. No additional `0x100` shift is
applied. Hidden base-core flags and duplicate addresses are excluded. There is no source for
prerequisite edges, SP costs, purchase dependencies or geographic graph positions; none is invented.

`rebirth-research.json`: extracted 220 crafting and 168 minigame literal bit candidates. Dynamic
offset expressions, hidden/disabled flags and duplicate addresses are excluded. The entire Queen's
Blood minigame group is excluded because opponent bits already appear in the progress collection.
Unlock/done/reward domains retain separate source paths; bits do not imply ingredient consumption,
reward delivery, scores or a complete gameplay transaction. These new research modules are read-only.

Extractors in `research/build_folio_data.py` and `research/build_research_data.py` verify the pinned
template SHA-256 `9ca05dfc4ff2093f705640b2c22a3236ed4acb9dc99c2ee2541712e15fcae32c` and read it as text.
No upstream code is executed. Numeric PS5 reads are tested on every primary retail/demo sample;
semantic identity and gameplay effects remain Inferred, including demo applicability.

Equipment graphs independently expose the template's `0xCC` weapon instance and eight `0xF4` weapon
and eight `0xAC` armor materia reference positions, with character stride `0x100`. These are stored
positions, not verified usable capacity. Names join only unique materia detail instances; unknown
and duplicate references retain raw IDs. No links, socket legality or upgrade purchases are inferred.

Regional F9 marker labels/bit states reuse `progress.source.md`; coordinates and art are not imported.
The [PowerPyx regional-map page](https://www.powerpyx.com/final-fantasy-7-vii-rebirth-full-world-map-revealed/),
by PowerPyx, published 2024-02-29, retrieved 2026-10-28, is an external link only. No reuse licence was
established, so its map artwork, coordinates and prose are not embedded. No remote resources are
fetched by the plugin or used as a map background. Original UI summaries describe limitations.
