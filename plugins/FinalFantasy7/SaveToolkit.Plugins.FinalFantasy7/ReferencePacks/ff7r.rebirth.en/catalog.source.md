# FFVII Rebirth catalog provenance

Source: https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-rebirth/saveEditor/utils/resource.ts

Author: RyudoSynbios / Game Tools Collection. Copyright (c) 2024 RyudoSynbios.
License: MIT; full notice in LICENSE.txt. Retrieved 2026-10-28.

The three datasets extract raw type IDs, names, category labels and AP arrays. Weapon
names are merged into the item catalog without changing their type identity. Descriptions
are original summaries of these source facts, not game effect text or gameplay legality.
Materia AP thresholds drive experimental ordinary AP/star transitions after the write-enable
decision; summon/repeated-zero and incoherent original rows remain protected.
User-pack metadata cannot widen the plugin's embedded write policy.

Rebirth card labels retain source numbering, which duplicates 012 and omits 013. Booster
packs have a separate derived category; no sequential type-ID-to-card-number rule is used.

Reproduce using `python -I plugins/FinalFantasy7/research/build_reference_data.py <scratch>`.
The script reads pinned files with `git show`, parses text without executing TypeScript,
and normalizes generated JSON to UTF-8/LF for manifest hashes. No private save bytes are used.
