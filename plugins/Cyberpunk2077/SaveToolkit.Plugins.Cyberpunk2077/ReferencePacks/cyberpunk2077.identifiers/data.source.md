# Cyberpunk 2077 community identifier pack 1.0.0

Author/project: **DeweySalt / Deweh and CyberCAT-SimpleGUI contributors**.
Retrieved **2026-10-28**, revision **c1fd00305e7db905a4e7de8cfa360d4f4efee3b2**.
Upstream licence: [MIT](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/LICENSE).
Retain that notice (`../../../CP2077SaveEditor-LICENSE.txt`) when redistributing
these adapted community dictionaries. `pack.json` repeats attribution per dataset;
`upstream.json` pins the downloaded source SHA-256 values. Plugin-authored annotations
describe uncertainty and do not supply new game rules.

| Dataset | Pinned source | Extraction / coverage |
| --- | --- | --- |
| `items.json` | [items.bin](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/items.bin), [ItemClasses.json](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/CP2077SaveEditor/Resources/ItemClasses.json) | 5,248 exact item/ammo identifiers: dictionary names joined to upstream class IDs, plus retained baseline identifiers. Decimal-string CRC32/UTF-8-length keys; upstream Type as inspection category. Only a subset of 10,039 upstream classes resolves through this dictionary. 22/22 current PS5 sample identities match. |
| `facts.json` | [Facts.json](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/CP2077SaveEditor/Resources/Facts.json) | All 19,357 exact FNV-1a/32 identifiers, with canonical strings copied separately to `attributes.canonicalName`. Nine strings have significant leading/trailing whitespace. Never trim them before hashing; F2's display-name normalization does not alter the canonical attribute. 1,129/2,750 sample entries match, counting entries across tables. |
| `vehicles.json` | [Vehicles.json](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/CP2077SaveEditor/Resources/Vehicles.json) | 105 record identifiers with computed decimal-string TweakDB keys. No saved-garage join, ownership, summon or entitlement claim. |
| `travel.json` | [FastTravel.json](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/CP2077SaveEditor/Resources/FastTravel.json) | 232 PointRecord identifiers, computed TweakDB keys, retained MarkerRef and IsEP1 metadata. No coordinates, discovery state or complete map. |

The generator rejects duplicate source keys, hash mismatches and conflicting identities.
All keys remain exact invariant decimal strings; no fuzzy aliases or precision-losing numbers.
Build and DLC completeness are **unknown**. `gameBuilds: []` means labels can be consulted
across builds, not verified availability in every build. The language-neutral pack deliberately
omits locale, descriptions, limits and assets. Record names/categories do not prove item tier,
equipment, quest completion, safe ID replacement or structural editing.

Rights scope: these are factual technical identifiers/categories, extending the already
attributed identifier dictionary. The repository's GPL-3.0-or-later code licence is **not** a blanket
licence for CD PROJEKT RED localization, artwork, icons, dialogue or other expressive game data.
None is included in this identifier pack. No wiki text or upstream GPL implementation is bundled
in it. The separate game-assets pack includes attributed publisher art/text under the owner decision
2026-10-29, not under the MIT dictionary licence. Future localization/art imports need per-dataset
source, rights-holder, revision/date and modification attribution under plugin-guide rule 9;
explicit NC/no-redistribution terms remain refusal reasons. Private resource exports are not bundled.

Regeneration: `python -I plugins/Cyberpunk2077/reference-data/generate.py <scratch-inputs>`;
see `plugins/Cyberpunk2077/REFERENCES.md`. The generator is original offline data processing;
no upstream generator or downloaded executable code is run.
