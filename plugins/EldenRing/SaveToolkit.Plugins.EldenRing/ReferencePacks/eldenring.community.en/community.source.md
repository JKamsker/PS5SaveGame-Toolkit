# Community facts provenance

Licence: **No licence published; reused with attribution per owner decision 2026-10-28**.
Retrieved 2026-10-29. This records reuse authorization, not an upstream licence grant.
No root licence was found in the pinned TGA, Paramdex or ER-Save-Lib trees.

Reproduce with `python -I plugins/EldenRing/Evidence/import-community-catalog.py <scratch>`.
The script reads reviewed archives/text only; it never downloads or executes upstream code.
Archive hashes are pinned in `community_sources.py`. All extracted source-byte hashes follow.

Only literal item/name/category membership and state-group event IDs are imported from TGA.
Script quantities, predicates, runtime pointers and writers are discarded.
ClayAmore items.rs contributes 19 inherited TGA groups / 545 active occurrences (544 distinct memberships); its MIT adaptation
does not relicense the original TGA data. Commented IDs and corrupted name comments are excluded.
Multi-flag groups are separate raw observations, never an inferred Boolean completion test.
Commented groups are excluded. One identical repeated flag is retained with sourceOccurrences=2.
DLC1/DLC3 are upstream group labels; they do not assert PS5 support, installation or ownership.
Flag addressing uses the ER-Save-Lib sparse group map: block * 125 + (event % 1000) / 8,
LSB bit 7 - (event % 8). Unmapped or out-of-range candidates remain in F2 but are not read.

Paramdex subset: equipment/goods/gems, arts, graces, classes, reinforcement and NG+ scaling layouts.
XML contributes only technical declarations and version/type metadata;
DisplayName, Description, defaults, editor bounds, game artwork and all other text nodes are excluded.
No offsets or legal caps are inferred, and no PARAM field values are decoded or written.

## Compact storage (v1.1.0)

JSON is minified. Attribution holds dataset-constant source URLs, pinned revisions, uncertainty and
flag schema eldenring-community-flags/2. All source-byte hashes are listed below rather than repeated
per record. Names are verbatim; upstreamName appears only when F2 whitespace trimming would change it.
Item source overrides remain only when different from the dataset default dropdown. MassItemGib
groups are exact source filenames without .cea (including content folder groups); editorGroups
always come from the pinned ClayAmore items.rs. These joins resolve the complete group provenance.
Flag source and sparse-lookup revision/hash are dataset/sidecar constants. Category event-flag and
candidate schema are dataset-wide. stateGroup duplicated name (before the NG+ persistence prefix).
Layout field sources are ER/Defs/<category>.xml at the pinned Paramdex revision. Per-table type/
version facts live in the :schema record; field order is the :field:<index> native key suffix.

Removed all 8,651 PARAM row labels (eleven Names tables) and their standalone browse catalog.
No save decoder or inventory/equipment/progress/regulation view consumed those labels; the only
consumer was the dump browsing itself. No sample-derived selection or future-content restriction
was introduced: all 3,221 TGA identities, 1,748 flags and 1,072 layout records remain intact.
Inventory names and read-only pickers still use the complete original and TGA name catalogs.

## Dataset default sources

- eldenring-tga-items: https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/MiscWIP/Dropdowns/ItemDropdownBase.xml
- eldenring-tga-flags: https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Event%20Flags/Event%20Flag%20Manager/.cea
- eldenring-param-layouts: https://github.com/soulsmods/Paramdex/tree/ff7245e524329bc3eab00036723d2bd53384cedf

| Source (author/project named in URL) | Original SHA-256 |
| --- | --- |
| https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/items.rs | `38a09ba5bde95f4f67029c5af993514d26cfcd4c78ff11038647e99dccb1ce45` |
| https://github.com/ClayAmore/ER-Save-Lib/blob/664ff9e83b7022a6fdc5a8332e8b7b22dce34e0f/src/res/eventflag_bst.txt | `d3cd15912bf2c4ff7c67a1f3ec2aff78ec887e4d8e435e3cab036f80cb8f4c82` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Event%20Flags/Event%20Flag%20Manager/.cea | `96e34e6fdfb5a40e00fbd3c454df84344851f58b3b3502bd516b4c1659a3e125` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/MiscWIP/Dropdowns/ItemDropdownBase.xml | `9ab46e219d9df19670b72f92685d90a9f223e75ffc78bfd48ccb37e1e2f68b57` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Base%20Game%20--/--%20Goods%20--/Give%20all%20bell%20bearings.cea | `a942a9bfebbb905f0be35edbc801373157722836a5202e11473a8efac3be70c4` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Base%20Game%20--/--%20Goods%20--/Give%20all%20crafting%20materials.cea | `9464a6a15e18f85f6df4619fd46262d369fd860cd3888d5df81c1e1870ac3c2d` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Base%20Game%20--/--%20Goods%20--/Give%20all%20crystal%20tears.cea | `c4f6eb6289c7ad283c365e748ff077b694c4f5d13d0e87aa4436cfca40bbba59` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Base%20Game%20--/--%20Goods%20--/Give%20all%20greases.cea | `518abca7e87b54f63f621674fa880aeba5f7fde65677d08fd57510811618a728` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Base%20Game%20--/--%20Goods%20--/Give%20all%20incantations.cea | `ce54d018d721de16874cad19c6e8e9bafd83a123841aecdc281466ed687a794e` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Base%20Game%20--/--%20Goods%20--/Give%20all%20limited%20quantity%20craftables.cea | `98e6f9158758d3b437d073b841491f4c9c9e523e0b2d3ae9ce37d03b00ad822b` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Base%20Game%20--/--%20Goods%20--/Give%20all%20sorceries.cea | `5bfc02938b4af36e195f5e08edd9b1cdaa69fc5a8f6daa4296b3c72e0ccdef31` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Base%20Game%20--/--%20Goods%20--/Give%20all%20spirit%20ashes.cea | `bb2580ccc8d2c6c993538b64a21c821d792ddf2496238e9bd5c01cb8e1a8f2b9` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Base%20Game%20--/--%20Goods%20--/Give%20all%20upgrade%20materials.cea | `afa5c8daaf55a1a496d5ed74560ac38272921ea816d5072b6efcb68ae10bb45b` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Base%20Game%20--/--%20Goods%20--/Give%20misc%20consumables.cea | `0069fb2beeaa2ea9d6408da7c977a97d251fcafb400c835f8ee9616ea7af2b1a` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Base%20Game%20--/Give%20all%20accessories.cea | `ecc6492ec10d12d3a8cf2126c948b8774441687b70e34ce972400c535a9c4ec8` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Base%20Game%20--/Give%20all%20ashes%20of%20war.cea | `08173d4aeb171af48b058d16f71494b16cf1e616e4196a10f3ac654f8083aa24` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Base%20Game%20--/Give%20all%20projectiles.cea | `0465063f7653a10c5da3cc77a5a378800ca2a4e9b3dd12a1caa773d973d2e301` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Base%20Game%20--/Give%20all%20protectors.cea | `cd9d49f1c210b3c70a6d35b5322427312a97d8f402da9e7b0c34e876648d8d91` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Base%20Game%20--/Give%20all%20weapons.cea | `96eba14156c0e7f17187eb6a5729a727d80193ec4f34d3cf9f505b9cf0ba7c8e` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Shadow%20of%20the%20Erdtree%20--/--%20Goods%20--/Give%20all%20bell%20bearings%20%28SotE%29.cea | `971b1f6e0db5fde40039f04a49e92596c0016cd691eed633dba3f78a9f83d461` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Shadow%20of%20the%20Erdtree%20--/--%20Goods%20--/Give%20all%20crafting%20materials%20%28SotE%29.cea | `9e01dd050dc365d2860616e330d5fc5711f64aca3a7a0d670351d3bfaf5c9541` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Shadow%20of%20the%20Erdtree%20--/--%20Goods%20--/Give%20all%20crystal%20tears%20%28SotE%29.cea | `71a3a3d534ab53a2e359c19d3253bafa849b4375d4e5310f8ec695a9e5b8e9b8` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Shadow%20of%20the%20Erdtree%20--/--%20Goods%20--/Give%20all%20greases%20%28SotE%29.cea | `a9eaab8640f998e3756cac092e80dc049c55ee542a7b2d0cef0ab673239b08e7` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Shadow%20of%20the%20Erdtree%20--/--%20Goods%20--/Give%20all%20hefty%20pots%28SotE%29.cea | `e00db5d66d0ef7f143a260cd71374a3c76550ddb7e442e83b75c29b2f3b06016` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Shadow%20of%20the%20Erdtree%20--/--%20Goods%20--/Give%20all%20incantations%20%28SotE%29.cea | `92628be34249347e77a4fac942b83c66a06e0edafed73148d769fb6f19648eba` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Shadow%20of%20the%20Erdtree%20--/--%20Goods%20--/Give%20all%20remembrances%20%28SotE%29.cea | `f66df5ba980b60638173b441c948c5fa07541a4c7627380f5a908fc51888ffa3` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Shadow%20of%20the%20Erdtree%20--/--%20Goods%20--/Give%20all%20sorceries%20%28SotE%29.cea | `1c44cfc2d7a7ab2d3ad3dc9d4f51a358fbbb6e531e99a0a0decf34f415cde954` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Shadow%20of%20the%20Erdtree%20--/--%20Goods%20--/Give%20all%20spirit%20ashes%20%28SotE%29.cea | `2a502ad250c24a0f3d7d8507b80c1b7d8395676e343407a81721af45255f4f31` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Shadow%20of%20the%20Erdtree%20--/--%20Goods%20--/Give%20misc%20consumables%20%28SotE%29.cea | `7fa0c165f4fa3547eefb5b86bd6e5d5d202c18860ee4fa13703c921a112a18b0` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Shadow%20of%20the%20Erdtree%20--/Give%20all%20accessories%20%28SotE%29.cea | `335c18256fa9a0b1b6d5da751d21c5f975709bad3d097235dc84edf22b9733b5` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Shadow%20of%20the%20Erdtree%20--/Give%20all%20ashes%20of%20war%20%28SotE%29.cea | `4a9147656e7b2f1939ffb343f5bff640a87dcd9203f70e3cb4075cb96925f953` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Shadow%20of%20the%20Erdtree%20--/Give%20all%20projectiles%20%28SotE%29.cea | `b15e88962ceacb013c6e250ec5813eefa7ec10e39d44df9a4c6f89c1d43582ef` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Shadow%20of%20the%20Erdtree%20--/Give%20all%20protectors%20%28SotE%29.cea | `ef8a3ab903829251719627f5f89a6291d04ef3bcb3f83fea2d66796a2fe6c6e4` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Shadow%20of%20the%20Erdtree%20--/Give%20all%20weapons%20%28SotE%29.cea | `f7e4dbd2546f882f3a5609941f61abf1059944e42c80dcce5d07f8276abb3f10` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Tarnished%20Pack%20--/Give%20all%20Spectral%20Steed%20Regalia%20%28TP%29.cea | `dba718fe9eac546dde10aff3452874e318f9f01f0545a5b6ed54c5a661902f75` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Tarnished%20Pack%20--/Give%20all%20protectors%20%28TP%29.cea | `42c108ff959769e38c96e524ae86b57a41178132c0ec0b4b1b916af405053672` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/--%20Tarnished%20Pack%20--/Give%20all%20weapons%20%28TP%29.cea | `971a1bbcca800c7a3fd3710f63e54d72c93deda07bce12c6d08788c8c44ed51e` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/Give%20all%20prattling%20pates.cea | `1feaad18f719ecbb4ad66d161def42d0d0bdbdc1a5730bb607d3bb4a5baeb564` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/CheatTable/CheatEntries/Open%20-%20The%20Grand%20Archives%20-%20Elden%20Ring/Scripts/Build%20Creation/ItemGib/MassItemGib/Give%20build%20starting%20items.cea | `bca646957680037d21ed0b3dfc75b02fd2274b2584ce0fd44ad5b2d6d7d2b69f` |
| https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA/blob/393c439e72ad48128efdc5e30fc3feb4bfbd210f/README.md | `643339abd6fca6d2ca9ca894d0f113cb51091f87d5da7ae1e7825176c984a3e5` |
| https://github.com/soulsmods/Paramdex/blob/ff7245e524329bc3eab00036723d2bd53384cedf/ER/Defs/BonfireWarpParam.xml | `a30f9f66669f57c3d73f1a3f49703d71f80a7620106bd009043c09ce4fcdcc6c` |
| https://github.com/soulsmods/Paramdex/blob/ff7245e524329bc3eab00036723d2bd53384cedf/ER/Defs/CharaInitParam.xml | `13a8745d8198ed0484b995926e803e6a733672ff717ab8dd400ba4fa305936f8` |
| https://github.com/soulsmods/Paramdex/blob/ff7245e524329bc3eab00036723d2bd53384cedf/ER/Defs/ClearCountCorrectParam.xml | `2b62eb5df4d5abc97e3930d8c551997eb8c63ae6b1cbad3c5de6e35134a094c8` |
| https://github.com/soulsmods/Paramdex/blob/ff7245e524329bc3eab00036723d2bd53384cedf/ER/Defs/EquipParamAccessory.xml | `be2c7887c2ed9ee496617ef146033d2f4166346838f96be60dd8f962bf6a47ba` |
| https://github.com/soulsmods/Paramdex/blob/ff7245e524329bc3eab00036723d2bd53384cedf/ER/Defs/EquipParamGem.xml | `ea12311f4deb77ae70380f9f349d9e54bfa4c5e3c84dd0bed42b2a871caea1d0` |
| https://github.com/soulsmods/Paramdex/blob/ff7245e524329bc3eab00036723d2bd53384cedf/ER/Defs/EquipParamGoods.xml | `9a5a67cc265fed624a385ef603827ed4ee4f0beebb310a7c8a778e638ea1e331` |
| https://github.com/soulsmods/Paramdex/blob/ff7245e524329bc3eab00036723d2bd53384cedf/ER/Defs/EquipParamProtector.xml | `97d2c82cfab93387cfef7a49de7f3f9bc01853b1d46e55b0beb5e2e94474c60c` |
| https://github.com/soulsmods/Paramdex/blob/ff7245e524329bc3eab00036723d2bd53384cedf/ER/Defs/EquipParamWeapon.xml | `f31baf241376e6991843f210413f308fb06c54647864ebe07ce4e145c33aa918` |
| https://github.com/soulsmods/Paramdex/blob/ff7245e524329bc3eab00036723d2bd53384cedf/ER/Defs/ReinforceParamWeapon.xml | `751074fdbb96d32d015d066f8ebebb4875a4bc0584257889fb846308980e038a` |
| https://github.com/soulsmods/Paramdex/blob/ff7245e524329bc3eab00036723d2bd53384cedf/ER/Defs/SwordArtsParam.xml | `6c1d46c8c0c7e08632997d7d80affee2e8561a5e40e3e295a52a402ad4186eb4` |
| https://github.com/soulsmods/Paramdex/blob/ff7245e524329bc3eab00036723d2bd53384cedf/README.md | `d891a1a0d43af382ea427f440bf320ee2d738c3399d26ffaa8a691ff20c5d934` |
