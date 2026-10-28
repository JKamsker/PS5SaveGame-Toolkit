# Tales of Arise reference pack: sources and share-alike

`talesofarise.catalog` **2.0.0**, English, title `PPSA02745`, retrieved **2026-10-28**.
All eight derived JSON datasets are by **Aselia Wiki contributors**, available under
**[CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/)**. Attribution and
share-alike apply to the derived data, including adapted descriptions; they do not
change the licence of independently authored plugin code. Source licensing:
[Fandom licensing](https://www.fandom.com/licensing). No artwork, icon bytes,
upstream program code, private saves or private game dumps are included.

| Dataset | Records | Coverage / transformation |
| --- | ---: | --- |
| `items` | 271 | 28 consumables, 113 materials/ingredients, 25 accessory materials, 41 valuables, 25 fishing items, 39 artifacts. |
| `equipment` | 675 | All named rows across six weapon types, six armor types, accessories, six costume lists, six style lists and attachments; includes source-described DLC/expansion entries. |
| `artes` | 309 | Named arte rows for all six playable and thirteen allied/enemy/cameo character pages. Shared Boost Strikes remain character-specific entries with each page's requirements. |
| `skills` | 64 | All parameter, action and character-specific skills; published base/DLC stack lists retain user order and unknown values. These are not save cap rules. |
| `titles` | 108 | All 18 panels for each of six playable characters, including DLC panels; prerequisites and completion bonuses retained. |
| `skill-nodes` | 540 | Five distinct position identities per title; retains SP text, title prerequisites and bonus. A dash is not converted into a zero-cost purchase. |
| `characters` | 62 | All named characters and groups from the pinned character navigation template, including unlinked names; six playable short-name/status-index joins remain Inferred. |
| `progress` | 128 | 45 recipes, 44 fish species and 39 artifacts with published effects/acquisition facts; no saved completion or ownership joins. |

Complete coverage means **every named row in these pinned sources**, not every possible
undocumented game ID, every unnamed move/variant, or proven support for DLC saves.
Quest/owl identity tables were not available at the attempted Aselia `ToA - Sub Quests`
and `ToA - Owls` API titles (missingtitle); their save sections remain raw/Unknown.

Whitespace and headings were normalized, images/citation markers/navigation removed,
and source facts/descriptions adapted to record JSON. Continuation rows are retained
as source notes rather than invented distinct records. Duplicate English names with
different source rows retain separate symbolic variant identities. Source errors and
question marks are preserved; published stats, costs and requirements are references,
not claims validated on PS5. No permissive datamine was needed, and no unlicensed
Cheat Engine dropdowns, proprietary patches or extracted game assets were copied.

Except for the 13 legacy inferred consumable identities, keys are symbolic reference
identities and `serializedId` is `Unknown`. The wiki supplies no PS5 numeric join.
Only Apple Gel currently has a PS5 acquisition observation. No numerical `limit` is
set: the pre-existing editing guardrail of 15 is not the effective game cap.

Each record carries its exact revision URL and revision attribute. The manifest has
per-dataset attribution and SHA-256 hashes of LF-normalized record files. This sidecar
lists every pinned page; `source-revisions.json` adds full API-response SHA-256, raw
wikitext SHA-256 and extracted counts. Rendered template expansion is fingerprinted
as retrieved; regeneration fails closed if the same page revision renders differently.
`ToA - Boost Strikes` is a pinned cross-check (zero additional entries), since the
participating characters' arte pages already provide all pairs and requirements.

For reproducible fetching/extraction and synthetic parser tests see
[`ReferenceData/README.md`](../../../ReferenceData/README.md). Runtime performs no I/O.

## Pinned sources

Author for every row: Aselia Wiki contributors. Licence for every row: CC BY-SA 3.0.
Retrieval date for every row: 2026-10-28. Extracted counts below include repeated
artifact rows in items/progress and the repeated character navigation group before deduplication.

| Source | Revision | Extracted records |
| --- | ---: | ---: |
| [ToA - Consumables](https://aselia.fandom.com/wiki/ToA_-_Consumables?oldid=382800) | 382800 | 28 |
| [ToA - Materials](https://aselia.fandom.com/wiki/ToA_-_Materials?oldid=379815) | 379815 | 113 |
| [ToA - Accessory Materials](https://aselia.fandom.com/wiki/ToA_-_Accessory_Materials?oldid=379795) | 379795 | 25 |
| [ToA - Valuables](https://aselia.fandom.com/wiki/ToA_-_Valuables?oldid=379820) | 379820 | 41 |
| [ToA - Cooking](https://aselia.fandom.com/wiki/ToA_-_Cooking?oldid=379806) | 379806 | 45 |
| [ToA - Fishing Items](https://aselia.fandom.com/wiki/ToA_-_Fishing_Items?oldid=379810) | 379810 | 25 |
| [ToA - Artifacts](https://aselia.fandom.com/wiki/ToA_-_Artifacts?oldid=381166) | 381166 | 78 |
| [ToA - Accessories](https://aselia.fandom.com/wiki/ToA_-_Accessories?oldid=361384) | 361384 | 26 |
| [ToA - Skills](https://aselia.fandom.com/wiki/ToA_-_Skills?oldid=378099) | 378099 | 64 |
| [ToA - Fishing](https://aselia.fandom.com/wiki/ToA_-_Fishing?oldid=350354) | 350354 | 44 |
| [ToA - Boost Strikes](https://aselia.fandom.com/wiki/ToA_-_Boost_Strikes?oldid=363183) | 363183 | 0 |
| [ToA - Attachments](https://aselia.fandom.com/wiki/ToA_-_Attachments?oldid=379805) | 379805 | 68 |
| [ToA - Weapons (Swords)](https://aselia.fandom.com/wiki/ToA_-_Weapons_(Swords)?oldid=363915) | 363915 | 45 |
| [ToA - Weapons (Rifles)](https://aselia.fandom.com/wiki/ToA_-_Weapons_(Rifles)?oldid=363926) | 363926 | 36 |
| [ToA - Weapons (Books)](https://aselia.fandom.com/wiki/ToA_-_Weapons_(Books)?oldid=379821) | 379821 | 34 |
| [ToA - Weapons (Gauntlets)](https://aselia.fandom.com/wiki/ToA_-_Weapons_(Gauntlets)?oldid=363201) | 363201 | 33 |
| [ToA - Weapons (Shields)](https://aselia.fandom.com/wiki/ToA_-_Weapons_(Shields)?oldid=382423) | 382423 | 34 |
| [ToA - Weapons (Rods)](https://aselia.fandom.com/wiki/ToA_-_Weapons_(Rods)?oldid=363203) | 363203 | 29 |
| [ToA - Armor (Mails)](https://aselia.fandom.com/wiki/ToA_-_Armor_(Mails)?oldid=379802) | 379802 | 26 |
| [ToA - Armor (Dresses)](https://aselia.fandom.com/wiki/ToA_-_Armor_(Dresses)?oldid=379800) | 379800 | 26 |
| [ToA - Armor (Cloaks)](https://aselia.fandom.com/wiki/ToA_-_Armor_(Cloaks)?oldid=379799) | 379799 | 20 |
| [ToA - Armor (Vests)](https://aselia.fandom.com/wiki/ToA_-_Armor_(Vests)?oldid=379803) | 379803 | 22 |
| [ToA - Armor (Guards)](https://aselia.fandom.com/wiki/ToA_-_Armor_(Guards)?oldid=379801) | 379801 | 18 |
| [ToA - Armor (Capes)](https://aselia.fandom.com/wiki/ToA_-_Armor_(Capes)?oldid=379798) | 379798 | 17 |
| [ToA - Alphen: Artes](https://aselia.fandom.com/wiki/ToA_-_Alphen:_Artes?oldid=378115) | 378115 | 45 |
| [ToA - Alphen: Skill Panels](https://aselia.fandom.com/wiki/ToA_-_Alphen:_Skill_Panels?oldid=378024) | 378024 | 108 |
| [ToA - Alphen: Costumes](https://aselia.fandom.com/wiki/ToA_-_Alphen:_Costumes?oldid=379796) | 379796 | 27 |
| [ToA - Alphen: Haircut, Style](https://aselia.fandom.com/wiki/ToA_-_Alphen:_Haircut,_Style?oldid=379797) | 379797 | 14 |
| [ToA - Shionne Imeris: Artes](https://aselia.fandom.com/wiki/ToA_-_Shionne_Imeris:_Artes?oldid=361403) | 361403 | 45 |
| [ToA - Shionne Imeris: Skill Panels](https://aselia.fandom.com/wiki/ToA_-_Shionne_Imeris:_Skill_Panels?oldid=378077) | 378077 | 108 |
| [ToA - Shionne Imeris: Costumes](https://aselia.fandom.com/wiki/ToA_-_Shionne_Imeris:_Costumes?oldid=379818) | 379818 | 27 |
| [ToA - Shionne Imeris: Haircut, Style](https://aselia.fandom.com/wiki/ToA_-_Shionne_Imeris:_Haircut,_Style?oldid=379819) | 379819 | 27 |
| [ToA - Rinwell: Artes](https://aselia.fandom.com/wiki/ToA_-_Rinwell:_Artes?oldid=363191) | 363191 | 43 |
| [ToA - Rinwell: Skill Panels](https://aselia.fandom.com/wiki/ToA_-_Rinwell:_Skill_Panels?oldid=378043) | 378043 | 108 |
| [ToA - Rinwell: Costumes](https://aselia.fandom.com/wiki/ToA_-_Rinwell:_Costumes?oldid=379816) | 379816 | 20 |
| [ToA - Rinwell: Haircut, Style](https://aselia.fandom.com/wiki/ToA_-_Rinwell:_Haircut,_Style?oldid=379817) | 379817 | 20 |
| [ToA - Law: Artes](https://aselia.fandom.com/wiki/ToA_-_Law:_Artes?oldid=361401) | 361401 | 31 |
| [ToA - Law: Skill Panels](https://aselia.fandom.com/wiki/ToA_-_Law:_Skill_Panels?oldid=378045) | 378045 | 108 |
| [ToA - Law: Costumes](https://aselia.fandom.com/wiki/ToA_-_Law:_Costumes?oldid=379813) | 379813 | 22 |
| [ToA - Law: Haircut, Style](https://aselia.fandom.com/wiki/ToA_-_Law:_Haircut,_Style?oldid=379814) | 379814 | 14 |
| [ToA - Kisara: Artes](https://aselia.fandom.com/wiki/ToA_-_Kisara:_Artes?oldid=361400) | 361400 | 33 |
| [ToA - Kisara: Skill Panels](https://aselia.fandom.com/wiki/ToA_-_Kisara:_Skill_Panels?oldid=378064) | 378064 | 108 |
| [ToA - Kisara: Costumes](https://aselia.fandom.com/wiki/ToA_-_Kisara:_Costumes?oldid=379811) | 379811 | 20 |
| [ToA - Kisara: Haircut, Style](https://aselia.fandom.com/wiki/ToA_-_Kisara:_Haircut,_Style?oldid=379812) | 379812 | 16 |
| [ToA - Dohalim il Qaras: Artes](https://aselia.fandom.com/wiki/ToA_-_Dohalim_il_Qaras:_Artes?oldid=361399) | 361399 | 39 |
| [ToA - Dohalim il Qaras: Skill Panels](https://aselia.fandom.com/wiki/ToA_-_Dohalim_il_Qaras:_Skill_Panels?oldid=378048) | 378048 | 108 |
| [ToA - Dohalim il Qaras: Costumes](https://aselia.fandom.com/wiki/ToA_-_Dohalim_il_Qaras:_Costumes?oldid=379807) | 379807 | 20 |
| [ToA - Dohalim il Qaras: Haircut, Style](https://aselia.fandom.com/wiki/ToA_-_Dohalim_il_Qaras:_Haircut,_Style?oldid=379808) | 379808 | 14 |
| [Template:ToA Characters](https://aselia.fandom.com/wiki/Template:ToA_Characters?oldid=300086) | 300086 | 63 |
| [ToA - Zephyr: Artes](https://aselia.fandom.com/wiki/ToA_-_Zephyr:_Artes?oldid=354337) | 354337 | 10 |
| [ToA - Balseph: Artes](https://aselia.fandom.com/wiki/ToA_-_Balseph:_Artes?oldid=349963) | 349963 | 1 |
| [ToA - Ganabelt Valkyris: Artes](https://aselia.fandom.com/wiki/ToA_-_Ganabelt_Valkyris:_Artes?oldid=349961) | 349961 | 7 |
| [ToA - Almeidrea Kaineris: Artes](https://aselia.fandom.com/wiki/ToA_-_Almeidrea_Kaineris:_Artes?oldid=350002) | 350002 | 6 |
| [ToA - Vholran Igniseri: Artes](https://aselia.fandom.com/wiki/ToA_-_Vholran_Igniseri:_Artes?oldid=350000) | 350000 | 5 |
| [ToA - Helganquil: Artes](https://aselia.fandom.com/wiki/ToA_-_Helganquil:_Artes?oldid=350001) | 350001 | 3 |
| [ToA - Great Astral Spirit: Artes](https://aselia.fandom.com/wiki/ToA_-_Great_Astral_Spirit:_Artes?oldid=351060) | 351060 | 2 |
| [ToA - Nazamil Hildris: Artes](https://aselia.fandom.com/wiki/ToA_-_Nazamil_Hildris:_Artes?oldid=354231) | 354231 | 19 |
| [ToA - Edna: Artes](https://aselia.fandom.com/wiki/ToA_-_Edna:_Artes?oldid=351767) | 351767 | 5 |
| [ToA - Eizen: Artes](https://aselia.fandom.com/wiki/ToA_-_Eizen:_Artes?oldid=351778) | 351778 | 11 |
| [ToA - Chronos: Artes](https://aselia.fandom.com/wiki/ToA_-_Chronos:_Artes?oldid=351781) | 351781 | 1 |
| [ToA - Kirito: Artes](https://aselia.fandom.com/wiki/ToA_-_Kirito:_Artes?oldid=351919) | 351919 | 2 |
| [ToA - Asuna: Artes](https://aselia.fandom.com/wiki/ToA_-_Asuna:_Artes?oldid=351922) | 351922 | 1 |
