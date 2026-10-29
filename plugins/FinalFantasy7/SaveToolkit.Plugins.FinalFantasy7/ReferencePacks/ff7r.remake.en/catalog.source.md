# FFVII Remake catalog provenance

Source: https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-remake/saveEditor/utils/resource.ts

Author: RyudoSynbios / Game Tools Collection. Copyright (c) 2024 RyudoSynbios.
License: MIT; full notice in LICENSE.txt. Retrieved 2026-10-28.

The three datasets extract raw type IDs, names, category labels and AP arrays. Weapon
names are merged into the item catalog without changing their type identity. Descriptions are attributed summaries of these source facts plus official Square Enix / PlayStation materia and weapon-ability guidance; they are not a complete effect table or gameplay legality.
Materia AP thresholds drive experimental ordinary AP/star transitions after the write-enable
decision; summon/repeated-zero and incoherent original rows remain protected.
User-pack metadata cannot widen the plugin's embedded write policy.

Rebirth card labels retain source numbering, which duplicates 012 and omits 013. Booster
packs have a separate derived category; no sequential type-ID-to-card-number rule is used.

Reproduce using `python -I plugins/FinalFantasy7/research/build_reference_data.py <scratch>`.
The script reads pinned files with `git show`, parses text without executing TypeScript,
and normalizes generated JSON to UTF-8/LF for manifest hashes. No private save bytes are used.


## Asset and text sources

* [Final Fantasy Wiki Remake item images](https://finalfantasy.fandom.com/wiki/Category:Final_Fantasy_VII_Remake_item_images) supplied the selected item, materia and weapon image files. Rights remain reserved by Square Enix; the pack records the owner-authorized attribution basis, and images are downscaled to 64x64 WebP.
* [Gamer Guides consumables reference](https://www.gamerguides.com/final-fantasy-vii-remake-intergrade/guide/appendix/items/consumables) supplies short attributed effects for 21 common consumables; these summaries do not establish Rebirth-wide item semantics.
* [Square Enix Remake screenshots and materia overview](https://www.square-enix-games.com/en_US/news/final-fantasy-vii-remake-screenshots) supplies the attributed materia descriptions. [PlayStation Blog's weapon-ability overview](https://blog.playstation.com/archive/2019/11/25/new-final-fantasy-vii-remake-screens-feature-moogles-weapon-abilities-materia-mini-games-and-more) supplies the attributed weapon-ability summary. Text is paraphrased where practical and remains marked unverified for save-ID/effect joins.
