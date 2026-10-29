# Atomic Heart third-party notices

The plugin is part of the GPL-3.0-or-later repository; see ../../LICENSE.

## Derived item-label dataset

Copyright (C) 2026 Skorchekd (Atomic Heart Menu).
This derived dataset is distributed under GPL-3.0-or-later, without warranty.
License text: https://www.gnu.org/licenses/gpl-3.0.html (also in the repository LICENSE).

Source and upstream terms:
- https://github.com/Skorchekd/AtomicHeartMenu/blob/fe36f468baacc996c252a1d3231d0d20009127bd/src/features/features.cpp
- https://github.com/Skorchekd/AtomicHeartMenu/blob/fe36f468baacc996c252a1d3231d0d20009127bd/LICENSE
- https://github.com/Skorchekd/AtomicHeartMenu/blob/fe36f468baacc996c252a1d3231d0d20009127bd/NOTICE

GPLv3 section 7 attribution terms are retained:
Skorchekd (original author); Dumper-7 by Encryqed; MinHook by Tsuda Kageyu;
Dear ImGui by Omar Cornut (ocornut). No claim is made that those projects authored
this plugin. Selected label/object-name pairs only, normalized short IDs,
original categories/caution text; no runtime code or offsets were copied.

## Structural format reference

SaveExtension, Copyright 2015–2020 Piperift, Apache-2.0:
https://github.com/PipeRift/SaveExtension/blob/c40f37a078ce8b9b2b3a811e47729e6ca2d11d48/LICENSE
No verbatim C++ implementation is included. The original C# parser uses the
published layout as research and explicitly documents Atomic Heart fork deviations.

## Shared codec

The plugin references SaveToolkit.Codecs. Its adapted Kraken implementation is
GPL-3.0-or-later, derived from AaryanKh/TekkenMusicModManager and Powzix/ooz.
See ../../src/SaveToolkit.Codecs/README.md and ../../THIRD-PARTY-NOTICES.
No codec source is duplicated in this folder.


## Publisher artwork and English reference additions (2026-10-29)

Official weapon renders, names and short excerpts: Slimao Ltd. / Mundfish,
https://ah.mundfish.com/weapons/ (individual page and image URLs/fingerprints in
`ReferencePacks/atomic-heart.items.en/assets.source.md`). Licence:
`Rights reserved by Slimao Ltd.; used with attribution per owner decision 2026-10-29`.
Twelve cropped/downscaled 64x64 renders; short excerpts, not full descriptions.

NORA screenshots: game content Slimao Ltd. / Mundfish; screenshot source Bilawal
Bashir / WhatIfGaming, https://whatifgaming.com/atomic-heart-crafting-guide/,
2023-02-22, retrieved 2026-10-29. Licence: `Rights reserved by Slimao Ltd. and WhatIfGaming; used with attribution per owner decision 2026-10-29`.
Eight resource glyphs, two KS-23 module glyphs and one Small Neuromed render,
source English names/effects/recipe facts; descriptions paraphrased. Only crops
are embedded, no screenshots/site logos. Full URL/crop/hash audit in the sidecar.

Nine Shok name/cost rows: Md Obyead / GameRiv,
https://gameriv.com/all-abilities-atomic-heart/, 2023-03-09, retrieved 2026-10-29.
Licence: `Rights reserved by GameRiv; used with attribution per owner decision 2026-10-29`.
Names/costs selected and descriptions summarized; authored reference keys are not
native save IDs. See `guide.source.md`. No NC Fandom content is included.

These additions retain the named holders' rights. The original runtime-label
subset retains GPL-3.0-or-later and all credits above; adding publisher content
does not relicense it or claim a publisher redistribution grant.
