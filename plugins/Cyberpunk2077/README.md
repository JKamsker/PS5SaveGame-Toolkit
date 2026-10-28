# Cyberpunk 2077 (`cyberpunk2077`)

Title: **PPSA04027**. Maturity: **Experimental**. Round 2 adds five player attributes,
unspent attribute/perk/relic points, and named player ammo/consumable stack quantities. Edits are
restricted to **save 265 / game 2210 / archive 195** (the supplied PS5 2.21 save). Scriptable progression
also requires **RED package 4 / flags 0 / sections 6**. Unsupported versions show a reason and retain
the applicable read-only explorers. No console load has been tested; only a human can set Stable.

**Deliberately incomplete goal:** level, street cred and their current XP are decoded but read-only.
The sample omits `expToLevel`, `maxLevel` and `currentExp`; no PS5 2.21 transition table has been
verified. A displayed-level patch alone does not establish correct level-up rewards/unlocks. The UI
explains why these edits are refused. Quest facts remain read-only.

## File roles and scope

- `sav.dat`: gameplay CSAV; claimed with matching title, filename and VASC/FZLC signatures.
- `settings.json`, `metadata.9.json`: owned by GenericJson; never claimed or rewritten here.
- `user.gls`: separate global/profile format; not claimed or modified.
- `screenshot.png`, system files and backup siblings: never rewritten as a side effect.

The plugin performs no filesystem/network I/O. All parsing and writing use the SDK's supplied bytes.

## Verified container layout

All integer words are little-endian. Magic strings show their on-disk byte order.

| Region | Layout |
| --- | --- |
| `0x00..0x18` | `VASC`, save version at 4, game version at 8, opaque clock/date/header bytes, archive version at 21 |
| `0x19` | `FZLC`, int32 chunk count |
| `0x21` | Descriptor triples: absolute file offset, stored size, expanded size (12 bytes per chunk) |
| First descriptor offset | Data begins after reserved table space; sample offset `0xC21` / 3105 |
| Compressed chunk | `4ZLX`, repeated int32 expanded size, raw LZ4 block; stored size includes the 8-byte wrapper |
| Uncompressed chunk | No wrapper; stored and expanded sizes must agree |
| Node directory | `EDON`, packed count, then packed ASCII name + next ID + child ID + virtual offset + size |
| Final 8 bytes | int32 directory **file** offset, `ENOD` |

Expanded node offsets use a virtual file with the same header/data start: subtract the first chunk's
file offset to index the concatenated payload. Each node starts with its int32 ID; parent sizes include
children. IDs, ordered ranges, containment, sibling chains, child links, chunk ranges, wrapper sizes
and exact LZ4 output lengths are checked.

Packed integers have six initial magnitude bits, sign/continuation bits, then seven magnitude bits per
subsequent byte. Names use negative byte lengths. Positive/UTF-16 name encoding is unsupported.
Unknown padding, gaps, directory extensions and opaque node bodies are retained.

Limits: 128 MiB input, 64 MiB total expanded data, 4096 chunks, 100,000 nodes, 128 nesting levels,
1024 bytes per node name. Invalid/oversized input raises `SaveFormatException`.

## Inventory, fields and confidence

The verified inventory schema has an int32 sub-inventory count after its node ID, then uint64 container
ID and int32 item count. Each item has matching 16-byte item-info prefixes before and inside its
`itemData` node: uint64 TweakDB ID, uint32 seed, byte structure, uint16 unique counter, byte flags.
After the node ID and prefix are byte flags, uint32 creation time and optional uint32 quantity.
Quantity is at node-relative offset 25. Extended item bodies remain opaque.

The prefix length matters: old MIT CyberCAT uses a 15-byte variant. Its item parser cannot be applied
literally to this sample. Current WolvenKit's wire layout and the actual PS5 bytes agree on 16 bytes.

`Items.money` resolves to **0x0000000BF5E188EC**: CRC32/IEEE of its UTF-8 name in the low word, name
length 11 in the next byte. The sample's unique container-1 money item is node 39, quantity **300**.
This meaning is **Verified** by the hash match, structure and upstream money editor.

The editable inventory table now resolves names from a **36-entry trimmed dictionary**, embedded in
`Resources/ItemNames.json`. These are upstream record identifiers, not invented/localized display
names. Every embedded name is checked against its recorded CRC32 + byte-length hash at runtime.
The source is CP2077SaveEditor's `items.bin` at the pinned commit below; the existing `ItemClasses.json`
and inventory controls provide category/quantity evidence. Unknown names display their complete hash.

| Sample node | Verified record | Quantity |
| --- | --- | --- |
| 44 | `Items.FirstAidWhiffV0` | 1 |
| 45 | `Items.BonesMcCoy70V0` | 1 |
| 49 | `Ammo.HandgunAmmo` | 600 |
| 50 | `Ammo.RifleAmmo` | 600 |
| 51 | `Ammo.ShotgunAmmo` | 120 |

Only container-1, structure-0, exactly 29-byte quantity-only records with a dictionary category of
Ammo, Component or Consumable are additionally writable. Recipe records, player body placeholders,
unknown hashes, other containers and extended items stay read-only. Crafting components and sniper
ammo are dictionary/source verified and exercised using synthetic item identities in the sample's
quantity-only layout; **the supplied save does not contain those records**. Consumable quantity bytes
are verified; the game's 2.x charge/replenishment behavior after loading is untested.

`FactsTable` contains packed count, a uint32 hash plane, then a uint32 value plane. Count and node
size must agree exactly. Hashes stay unresolved; fact meanings are **Unknown**, all read-only.
No generic fact edits or quest-unlock actions are offered.

## Player development and dependent stats

`ScriptableSystemsContainer` is an int32 node ID, int32 package length, then a RED package. The verified
package starts with byte version 4, byte flags 0, uint16 section count 6, uint32 component count,
four uint32 relative pool offsets, uint32 root-ID count, and uint64 root IDs. Offsets are relative to
the byte after that root-ID list. Name descriptors hold a 24-bit offset and 8-bit null-terminated
ASCII size. Chunk descriptors hold a uint32 type-name index and uint32 relative offset.

Each class starts with uint16 field count and `(uint16 name index, uint16 type index, uint32 offset)`
headers. Field offsets are relative to the class start. Struct arrays start with int32 count;
primitive members determine each record's end. Only the specifically needed flat struct types are
traversed. Unknown package chunks and unknown outer property bodies stay opaque.

The plugin follows `PlayerDevelopmentSystem.playerData`'s single handle, requires the referenced
`PlayerDevelopmentData` type and `ownerID.hash == 1`, and rejects ambiguous records. Zero-valued
primitive properties can be omitted. The cited enum defines missing development-point `type` as
`Attribute` (0); `Espionage` means relic points and `Primary` means perk points, as mapped by the
upstream player-stats editor.

Sample-verified development values: **level 1, street cred 1, both current XP 0, all five base
attributes 3, unspent attribute points 7, perk/relic points 0**. The zero perk/relic `unspent`
properties are absent. Their edits add one 8-byte property header and one 4-byte Int32 per record.
Spent balances, learned perks, secondary points and opaque data remain unchanged. Changing an
attribute does not consume/refund points or grant/remove perks; this matches the upstream editor's
independent attribute and point controls.

Attributes map Body to `Strength`, plus `Reflexes`, `TechnicalAbility`, `Intelligence`, and `Cool`.
Each edit also patches its unique additive `gameConstantStatModifier` in the entity-1 `StatsSystem`
`forcedModifiersBuffer`. FNV-1a/64 hashes of the stat names identify these entries. Both original
values must agree; missing, duplicate, nonconstant or mismatched mirrors disable the affected
attribute edits with a reason. Every changed mirror is reported as a derived `ValueChange`, exposed
as a read-only field, and recomputed through framework undo/redo. These meanings and byte layouts
are **Verified** against the cited sources and both supplied gameplay entries. In-game acceptance,
undocumented dependencies and behavior of extreme values remain **Unknown**.

## Writing, compression and integrity

Money, named stack quantities and attribute/stat pairs use fixed-size patches. The money cap is
100,000,000; other stack caps are 999,999; point-balance caps are 100,000. Existing larger values stay
representable. These are editor policies, **not verified engine limits**. Attributes use the upstream
3..20 range, extended to retain an existing out-of-range value. Values are validated atomically.

Point edits rebuild only the affected development-point records, their array, the containing player
class and package chunk descriptors. Existing opaque fields/chunks, name pools, root IDs and class
padding are retained. Package length, containing node size, subsequent virtual node offsets, chunk
expanded/stored lengths, chunk file offsets and the final directory pointer are repaired. No items,
chunks or CSAV nodes are inserted. Node IDs and links are unchanged.

No-op serialization returns the original bytes, including after undo. Changed chunks are re-encoded
with K4os raw LZ4. Unchanged compressed chunks, reserved bytes and inter-chunk gaps are retained.
Fixed-size patches retain expanded chunk boundaries; growth is assigned at the end of the replaced
node. Serialization re-decompresses and compares the complete intended payload, revalidates the
node tree/inventory/facts, re-parses progression and checks edited points/attributes and stat mirrors.
The raw payload viewer shows fixed patches at original offsets; omitted point properties materialize
in the exported package, as its label explains.

The supplied decrypted file needs no further encryption. The cited CSAV readers/writers define no
container checksum/signature; there is no known container checksum to recompute. TweakDB CRCs identify
names, not save integrity. Unknown internal bytes are preserved. Structural correctness and edit/reopen
preservation are tested; game acceptance and undocumented semantic dependencies remain untested.
This is why maturity remains Experimental.

## Views and tests

Overview, player development, inventory, facts, node tree and container tabs provide stats,
128-entry item/fact pages, lazy named children and paged original/payload/node bytes. Chunk tables
are bounded to 4096 rows. Value IDs stay stable across edits.

Sample evidence: **one primary record and its identical backup**, 1,302,721 stored bytes, 22 chunks,
5,607,178 expanded bytes, 159 nodes and 22 items. This is one distinct save, not a multi-session corpus.
Every supplied gameplay entry, including backups, is tested. Opening and initial views remain well
below the approximately two-second target on the development machine.

Tests cover conformance with a mixed money/ammo/attribute/point edit; individual round trips for all
five attributes, three point balances, three ammo stacks and two consumables; known sample values;
exact independent payload patches; preservation outside the resized node and of opaque package
chunks/properties; repaired node offsets and sizes; derived changes; framework undo/redo; byte-identical
no-op/undo; atomic rejection; unknown schema/package refusal; mismatched stat refusal; corrupt package
bounds; and independent package insertion/padding fixtures. Existing CSAV/LZ4 corruption, gap and
codec-vector tests remain. Hash vectors cover CRC32/TweakDBID and FNV-1a/64.

```powershell
dotnet test --project plugins/Cyberpunk2077/SaveToolkit.Plugins.Cyberpunk2077.Tests
```

Validation: **62 passed**, no sample skips. With an empty sample directory: **18 passed / 44 skipped**,
zero failures. API build: zero warnings/errors. Core: **43 passed**. No hooks, framework changes or
additional NuGet dependencies were added.

## Open problems

- Verify PS5 2.21 level/XP transition thresholds, rewards and unlock dependencies before enabling
  level/street-cred/XP edits. Stored values alone and an upstream GUI setter are insufficient evidence.
- Verify extended/unknown items, other schema/package versions, other regions and later-game samples.
- Validate component records in an actual PS5 sample, consumable charge behavior and all new edits
  by loading on a console. Establish separate global/profile integrity before editing `user.gls`.
- Modded/missing/nonconstant player stat buffers are intentionally unsupported for attribute writes.

## Sources and licences

Research/sample verification: 2026-10-28. Downloaded source was read as text in scratch storage;
no upstream application/script was executed. This is independently written format code. No GPL source
is copied or linked into the plugin. The managed K4os package is its only third-party runtime dependency. The trimmed reference dictionary
is attributed to CP2077SaveEditor (MIT); its notice is retained in [CP2077SaveEditor-LICENSE.txt](CP2077SaveEditor-LICENSE.txt).

- [CyberCAT at 50db326](https://github.com/WolvenKit/CyberCAT/tree/50db3263a94247531853ba13c2e200f2e784ba8d75/CyberCAT.Core):
  `Classes/CompressionHelper.cs` for FZLC/4ZLX and rebuilding, `Classes/SaveFile.cs` for directory/footer
  offsets and links, `ExtensionMethods.cs` for packed integers/names, `Classes/Parsers/FactsTableParser.cs`
  for fact planes. MIT; notice retained in [CyberCAT-LICENSE.txt](CyberCAT-LICENSE.txt).
- [WolvenKit at 65ea74b](https://github.com/WolvenKit/WolvenKit/tree/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Save):
  `Helper/InventoryHelper.cs`, `Classes/ItemData.cs`, `Classes/ItemInfo.cs` for current item wire facts;
  `IO/CyberpunkSaveReader.cs` / `CyberpunkSaveWriter.cs` for a second framing reference.
  GPL-3.0; format reference only, no source copied.
- [CP2077SaveEditor inventory control at c1fd003](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/CP2077SaveEditor/Views/Controls/InventoryControl.cs):
  confirms `Items.money` quantity in player container 1. MIT; no source copied.
- [CP2077SaveEditor player stats at c1fd003](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/CP2077SaveEditor/Views/Controls/PlayerStatsControl.cs):
  attribute names, Attribute/Primary/Espionage balance mapping and the dependent forced-stat updates;
  read together with [SaveFileHelper.cs](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/CP2077SaveEditor/Utils/SaveFileHelper.cs)
  for handle traversal and player owner/entity IDs. MIT; format/behavior reference, no source copied.
- [CP2077SaveEditor items.bin at c1fd003](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/items.bin):
  source of the trimmed embedded record-name dictionary. Names independently matched to CRC32/length
  hashes, including all 22 sample inventory identities. MIT notice retained. Also consulted
  [ItemClasses.json](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/CP2077SaveEditor/Resources/ItemClasses.json)
  and [ItemDetails.cs](https://github.com/Deweh/CyberCAT-SimpleGUI/blob/c1fd00305e7db905a4e7de8cfa360d4f4efee3b2/CP2077SaveEditor/Views/ItemDetails.cs)
  for item types and quantity editing. The dictionary records names; it does not claim every record
  appears or behaves identically in PS5 2.21.
- [WolvenKit package reader at 65ea74b](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Archive/IO/RedPackageReader.File.cs)
  and [RedPackageReader.cs](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Archive/IO/RedPackageReader.cs):
  package pools, scriptable root-ID list, class properties, name indices and handles;
  [RedPackageStructs.cs](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Archive/Buffer/RedPackageStructs.cs)
  defines descriptor bit layouts. [PackageParser.cs](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Save/Parser/PackageParser.cs)
  supplies the int32 package-length wrapper. GPL-3.0; format reference only.
- [WolvenKit PlayerDevelopmentData.cs](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Types/Classes/PlayerDevelopmentData.cs),
  [SProficiency.cs](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Types/Classes/SProficiency.cs),
  [SDevelopmentPoints.cs](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Types/Classes/SDevelopmentPoints.cs)
  and [cp77enums.cs](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Types/Enums/cp77enums.cs):
  progression field schemas and enum-zero defaults. GPL-3.0; format reference only.
- [WolvenKit ModifiersBufferReader.cs](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Archive/IO/ModifiersBufferReader.cs)
  and [SaveHashHelper.cs](https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Save/Helper/SaveHashHelper.cs):
  packed class name, FNV-1a stat hash, modifier kind and float value. GPL-3.0; format reference only.
- [PixelRick CyberpunkSaveEditor](https://github.com/PixelRick/CyberpunkSaveEditor): alternate tree/
  scriptable-system research lead; no parser copied or schemas inferred from its feature list.
- [LZ4 block specification](https://github.com/lz4/lz4/blob/dev/doc/lz4_Block_format.md): block grammar
  underlying literal and overlap vectors.
- [K4os.Compression.LZ4](https://github.com/MiloszKrajewski/K4os.Compression.LZ4): managed raw codec,
  version 1.3.8 from the existing central package list, MIT.
- [Repository reconnaissance](../../docs/formats/PPSA04027.md): initial sample dimensions, independently
  rechecked against every supplied gameplay entry.

## SDK requests

None. Existing lazy sections, tables, bound fields and paged blobs cover this implementation.
