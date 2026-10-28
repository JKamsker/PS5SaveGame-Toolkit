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
F4 descriptions and F12 labels. No Sony/Santa Monica art, textures, icons, screenshots, extracted
dialogue or game prose is included. Gear/quest labels are not promoted to verified Ragnarök
semantics; all existing write warnings and conservative ceilings remain intact.
