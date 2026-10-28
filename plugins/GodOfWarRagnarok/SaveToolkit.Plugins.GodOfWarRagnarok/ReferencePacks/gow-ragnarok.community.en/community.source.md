# Ragnarök community evidence pack attribution

Retrieved: 2026-10-28. Pack version: 1.0.0. Language: English. PS5 evidence scope:
PPSA08330, revision 0x00513040. Literal IDs retain the eight little-endian bytes stored in the save.

## community-ids.json

- Source: [Apollo CUSA34384 patch, commit 273f032b4c31c017fed5c1588f209b8faf87ba6c](https://github.com/bucanero/apollo-patches/blob/273f032b4c31c017fed5c1588f209b8faf87ba6c/PS4/CUSA34384.savepatch).
- Authors/project: Apollo Save Tool patch contributors / bucanero. The upstream file has no
  individual copyright notice. Preserve upstream contributor credit when redistributing.
- Licence: [GNU GPL version 3](https://www.gnu.org/licenses/gpl-3.0.html), upstream
  [LICENSE at the same revision](https://github.com/bucanero/apollo-patches/blob/273f032b4c31c017fed5c1588f209b8faf87ba6c/LICENSE).
- Changes: extracted the unique literal IDs from complete type-8 searches under Resources,
  Unique Resources and Key Items, removed IDs already in the original 31-record catalog,
  retained broad section categories as candidates. No write commands, preset quantities,
  implementation, descriptions or legal maxima were copied. All 36 item names remain Unknown.
- Confidence: upstream PS4 category evidence only; exact PS5 row presence can vary by save.
  A search hit outside counted rows is not inventory ownership. Categories are Inferred.

## collectibles.json

- Source: [Continue? Ragnarök reader, commit 51e19b2a292c60f9acdf3c13c561809657104ea2](https://github.com/Alexsander720/continue/blob/51e19b2a292c60f9acdf3c13c561809657104ea2/plugin/py_modules/ddsaves/god_of_war_ragnarok.py).
- Authors/project: Alexsander720 / Continue? contributors. No individual copyright notice
  appears in the source file. Preserve upstream contributor credit when redistributing.
- Licence: [GNU GPL version 3](https://www.gnu.org/licenses/gpl-3.0.html), upstream
  [plugin/LICENSE at the same revision](https://github.com/Alexsander720/continue/blob/51e19b2a292c60f9acdf3c13c561809657104ea2/plugin/LICENSE).
- Changes: extracted two factual raven-related ID/role mappings, converted numeric u64 values
  to literal little-endian byte keys and added original PS5 occurrence evidence. No Python
  code, completion formula, game text, media or maxima were copied. Labels are concise paraphrases.
- Confidence: PC-to-PS5 role transfer is Inferred. The unexchanged/raven-item ID appears in counted
  hero rows in 40/76 primary gameplay records; the gift ID appears in 56/76. Zero absence is not
  inferred and no derived raven completion total is presented.

The derived JSON datasets remain GPL-3.0. A verbatim [licence copy](GPL-3.0.txt) accompanies them.
These datasets are separate from the existing CC BY-SA resource pack. Every record has its pinned
source; pack dataset attribution, README, Info.References and UI carry the same provenance.
