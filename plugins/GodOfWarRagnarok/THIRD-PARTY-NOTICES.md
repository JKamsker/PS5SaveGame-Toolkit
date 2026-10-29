# GodOfWarRagnarok third-party data notices

This notice is plugin-local because the implementation worker may modify only this plugin folder.
No third-party implementation was ported in round 2. The following derived reference datasets
retain their upstream GPL-3.0 licence; they are distributed separately from the original CC BY-SA
resource-name dataset.

| Upstream project / authors | Revision / derived content | Licence |
| --- | --- | --- |
| Apollo Save Tool patch contributors / bucanero | [273f032b4c31c017fed5c1588f209b8faf87ba6c](https://github.com/bucanero/apollo-patches/blob/273f032b4c31c017fed5c1588f209b8faf87ba6c/PS4/CUSA34384.savepatch): 36 unique resource/key-item search IDs and broad category candidates, with explicit unknown names | [GPL-3.0](https://github.com/bucanero/apollo-patches/blob/273f032b4c31c017fed5c1588f209b8faf87ba6c/LICENSE) |
| Alexsander720 / Continue? contributors | [51e19b2a292c60f9acdf3c13c561809657104ea2](https://github.com/Alexsander720/continue/blob/51e19b2a292c60f9acdf3c13c561809657104ea2/plugin/py_modules/ddsaves/god_of_war_ragnarok.py): two raven-related factual identities and hero-wallet research label; no implementation or completion formula | [GPL-3.0](https://github.com/Alexsander720/continue/blob/51e19b2a292c60f9acdf3c13c561809657104ea2/plugin/LICENSE) |
| SaveWizard-Resources contributors, including Pj1980 / Skiller and the source's credited community | [4c32596efde54af7bec390b298265f09c929d94d](https://github.com/sterben-Dev10/SaveWizard-Resources/blob/4c32596efde54af7bec390b298265f09c929d94d/SaveWizard-Codes/GOW%20Ragnarok%20%5BSaveWizard%5D.txt): factual blacksmith-chest ID/role research label | [GPL-3.0](https://github.com/sterben-Dev10/SaveWizard-Resources/blob/4c32596efde54af7bec390b298265f09c929d94d/COPYING.md) |

The source files do not contain individual copyright notices for these mappings. Contributor credit
is retained above; upstream source and licence links are pinned. Changes are described in the
[data sidecar](SaveToolkit.Plugins.GodOfWarRagnarok/ReferencePacks/gow-ragnarok.community.en/community.source.md).
The full GPL text accompanies the [community pack](SaveToolkit.Plugins.GodOfWarRagnarok/ReferencePacks/gow-ragnarok.community.en/GPL-3.0.txt).

Original resource names/categories remain under CC BY-SA 3.0, attributed to God of War Wiki
contributors in their existing sidecar, manifest and README. No new wiki text or game media was
added in round 2. The historical no-licence exclusion is superseded by the owner decision below.

## Unlicensed-source unblock (owner decision 2026-10-28)

Licence for each dataset below: **No licence published; reused with attribution per owner decision 2026-10-28**.
This records the owner's reuse decision, not a new licence granted by the upstream author.
No third-party implementation was copied; the importer and lookup/decoding integration are original code.

| Upstream / authors | Revision / imported facts | Provenance |
| --- | --- | --- |
| mooofin / community spreadsheet contributors, God-of-War-Ragnarok | `7b1730217b92d27ab910d6d421efbd503cc109b2`: `item#.zip`, 961 unique resource/name/category candidates; `gow.CT`, four named skill-tree arguments | [Pinned sources, hashes and transformation](SaveToolkit.Plugins.GodOfWarRagnarok/ReferencePacks/gow-ragnarok.runtime.en/tables.source.md) |
| ProtoBuffers / xcier / God of War IDs spreadsheet contributors, GoW Save Lab | `9342d5f3967ef74bac1161581dc4eab8bb41760d`: `gow2018_items.json`, 1,682 factual ID/name/category/internal-name crosswalk candidates, explicitly marked 2018 / Unknown | [Pinned source, hash and transformation](SaveToolkit.Plugins.GodOfWarRagnarok/ReferencePacks/gow-ragnarok.2018.en/tables.source.md) |

Attribution appears in the F2 manifests/records, README, `Info.References`, F8 source cells,
F4 descriptions and F12 labels. These runtime/2018 datasets contain no publisher art or game
prose; the separately attributed visual catalogue below does. Gear/quest labels are not promoted to verified Ragnarök
semantics; all existing write warnings and conservative ceilings remain intact.

## Visual assets and skill facts (owner decision 2026-10-29)

The plugin embeds selected game images and short weapon descriptions. Publisher assets retain
`Rights reserved by Sony Interactive Entertainment and Santa Monica Studio; used with attribution
per owner decision 2026-10-29`; the mixed gear/rune/map/skill pack also credits MapGenie.
This owner decision is not an upstream licence grant. Retrieved 2026-10-29:

| Dataset / rights holders | Imported material and changes | Provenance |
| --- | --- | --- |
| God of War Wiki contributors; Sony Interactive Entertainment / Santa Monica Studio | Two compact resource icons; existing resource-name facts remain separately CC BY-SA 3.0 | [Resource icon sidecar](SaveToolkit.Plugins.GodOfWarRagnarok/ReferencePacks/gow-ragnarok.ps5.en/assets.source.md) |
| God of War Wiki contributors; Sony Interactive Entertainment / Santa Monica Studio; MapGenie | Eleven visual records: six gear/rune/map references and five skill thumbnails; compact WebP images, short factual descriptions and a 512x512 Midgard tile mosaic | [Asset sidecar](SaveToolkit.Plugins.GodOfWarRagnarok/ReferencePacks/gow-ragnarok.assets.en/assets.source.md) |
| God of War Wiki contributors, retrieved 2026-10-28 | 81 skill-name/branch/requirement/prerequisite facts; CC BY-SA 3.0 adaptations retain attribution/share-alike; no saved-state mapping | [Skill-facts sidecar](SaveToolkit.Plugins.GodOfWarRagnarok/ReferencePacks/gow-ragnarok.skills.en/skills.source.md) |

The skill-facts pack contains no image files. Skill thumbnails resolve from the visual catalogue.
No bulk dialogue, texture or wiki extraction is included, and no map coordinates or ownership,
equipped-state or skill-unlock joins are asserted.

## Gear definition facts (2026-10-29)

[Community gear spreadsheet](https://docs.google.com/spreadsheets/d/17JWS2EyV68tRAajJIungRvhL5mmePIyGUNqekWu1lBc/edit),
cfemen / Fixer / Sasha / Vinny / community gear-sheet contributors, accessed 2026-10-29.
Licence: **No licence published; reused with attribution per owner decision 2026-10-28**.
This is the owner's reuse policy, not an upstream licence grant. The new pack imports 2,092 unique
hash/name/type/level candidates from a SHA-256-pinned CSV export. It preserves duplicate disagreements,
marks missing/conflicting levels Unknown and makes no legal-maxima, equip-state or dependency claims.
No source code, acquisition notes, presets, prose or assets were imported. The importer is original
code and never executes source content. [Source/hash/transformation sidecar](SaveToolkit.Plugins.GodOfWarRagnarok/ReferencePacks/gow-ragnarok.gear.en/gear.source.md)
and [field-discovery source register](FIELD_DISCOVERY.md#source-register-and-licence-decisions).
Attribution appears in the F2 manifest/records, README, plugin references and every new UI/F12 name/level.
