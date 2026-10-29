# FFVII Remake catalog provenance

Source: https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-remake/saveEditor/utils/resource.ts

Author: RyudoSynbios / Game Tools Collection. Copyright (c) 2024 RyudoSynbios.
License: MIT; full notice in LICENSE.txt. Retrieved 2026-10-28.

The three datasets extract raw type IDs, names, category labels and AP arrays. Weapon
names are merged into the item catalog without changing their type identity. Descriptions are attributed summaries of these source facts plus official Square Enix / PlayStation materia and weapon-ability guidance; they are not a complete effect table or gameplay legality.
Materia AP thresholds drive experimental ordinary AP/star transitions after the write-enable
decision; summon/repeated-zero and incoherent original rows remain protected.
User-pack metadata cannot widen the plugin's embedded write policy.

The historical metadata/progress extractor is
`python -I plugins/FinalFantasy7/research/build_reference_data.py <scratch>`.
It requires a GTC Git checkout at `<scratch>/gtc` containing the pinned revision and reads it
with `git show`, without executing TypeScript. It writes UTF-8/LF JSON directly into the plugin
checkout, including version-1.0.0 manifests for both games. It does not reproduce the later
version-1.1.0 icons, effect summaries or asset datasets and would overwrite their JSON/manifest
additions. Run it only in an isolated checkout when researching the original extraction.
No private save bytes are used.


## Asset and text sources

Retrieved 2026-10-29. Publisher images and guidance use
`Rights reserved by Square Enix; used with attribution per owner decision 2026-10-29`.
Guide summaries use
`Rights reserved by Gamer Guides; used with attribution per owner decision 2026-10-29`.
These notices are separate from the MIT licence on GTC metadata.

* [Final Fantasy Wiki Remake item images](https://finalfantasy.fandom.com/wiki/Category:Final_Fantasy_VII_Remake_item_images) supplied the selected item, materia and weapon image files. Rights remain reserved by Square Enix; the pack records the owner-authorized attribution basis, and images are downscaled to 64x64 WebP.
* [Gamer Guides consumables reference](https://www.gamerguides.com/final-fantasy-vii-remake-intergrade/guide/appendix/items/consumables) supplies short attributed effects for 21 common consumables; these summaries do not establish Rebirth-wide item semantics.
* [Square Enix Remake screenshots and materia overview](https://www.square-enix-games.com/en_US/news/final-fantasy-vii-remake-screenshots) supplies the attributed materia descriptions. [PlayStation Blog's weapon-ability overview](https://blog.playstation.com/archive/2019/11/25/new-final-fantasy-vii-remake-screens-feature-moogles-weapon-abilities-materia-mini-games-and-more) supplies the attributed weapon-ability summary. Text is paraphrased where practical and remains marked unverified for save-ID/effect joins.
