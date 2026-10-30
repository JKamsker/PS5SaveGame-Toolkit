# Obfuscated JSON / Dungeons II (`format.json-xor`)

**Experimental, version 0.5.0.** Lossless token editing for byte-minus-one and
synthetic XOR-1 JSON, plus Experimental existing-token and structural writes for
the tested offline Minecraft Dungeons II character shape.
Only an owner can promote console-tested features to Stable.
The registered title is **Minecraft Dungeons II (`PPSA16064`)**; no other title
ID or region is claimed. Generic obfuscated JSON is recognized by format.

## Coverage and evidence

| File / format | Evidence and behavior |
| --- | --- |
| `GlobalSaveDataDefault/ue4savegame.dpx.sav` | One 3,978-byte PS5 snapshot. Adding one modulo 256 to every byte produces valid UTF-8 JSON. XOR-1 is invalid. Existing scalar and F5 structural settings writes are Experimental and carry an integrity warning. |
| `Character*/ue4savegame.dpx.sav` | One 40,062-byte plain UTF-8 JSON snapshot. Requires an unambiguous object `SerializeMeta.HardFormat=FCharacterSaveV1` and object `CharacterSaveV1`; claimed at Engine score, above generic JSON. Existing scalar and F5 structural writes are enabled only for SoftVersion `2`, FormatHash `3777917472`, offline characters. |
| Character versions | Sample: SoftVersion `2`, FormatHash `3777917472`, offline. Other soft versions/hashes and online/unknown modes get explicit warnings and remain read-only. No PC-version compatibility is asserted. |
| Other plain JSON | Declined, including unrelated objects, other hard formats and ambiguous character keys. GenericJson remains independent. |
| Other obfuscated JSON | Format-only detection; full parsing of both candidate transforms rejects ambiguity. Raw add/remove/replace actions are available for these generic documents. XOR-1 has synthetic evidence only. |

The sample has **one generation, two game files, and no backup twins**. F1
`savediff list` and generation `0` → `latest` comparison found **zero changes**.
This is a baseline consistency check, not controlled gameplay evidence.
[EVIDENCE.md](EVIDENCE.md) records reproduction commands and the evidence boundary.

No compression, checksum field, signature or key is apparent in the decoded
JSON. Whether external integrity or console-side validation exists is
**Unknown**. This plugin does not handle PS5 container crypto.

## Enabled writes and their evidence

| Write | Confidence | Evidence | Integrity status | Owner validation needed |
| --- | --- | --- | --- | --- |
| Existing scalar token edits in shifted settings and generic obfuscated JSON | Unknown; Inferred for a matching catalog record | One PS5 settings snapshot plus editor-generated F1 `masterVolume` differential; synthetic token/picker tests for both transforms | No checksum observed in decoded JSON; external checks are Unknown; warning is retained | [Issue #31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): spare-slot import, reload and rewritten-file comparison |
| Existing scalar token edits in offline `FCharacterSaveV1`, including achievement/count and collection-tag fields | Unknown; Inferred for a matching catalog record | One PS5 SoftVersion 2 / FormatHash 3777917472 snapshot; field paths observed, gameplay meaning unproven | No checksum observed in decoded JSON; dependencies and console acceptance Unknown; warning is retained | [Issue #31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): validate each scalar on a disposable offline slot |
| F5 add/remove/replace for valid JSON objects and arrays | Inferred | Token-preserving serializer, detached snapshots, bounded parse/reopen and undo tests | JSON validity is rechecked; game dependencies and external integrity Unknown; warning is retained | [Issue #31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): test one structural operation on a spare slot |
| F6 planned scalar bulk set | Inferred | Deterministic preview/apply, skipped incompatible rows, bounded values and atomic tests | Same Unknown integrity status; preview and action warnings are retained | [Issue #31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): validate individual edits, scope and whole-action undo |
| F6 stored-attribute, inventory-stack, item-ID and rarity actions | Inferred PC catalog policy; gameplay effects unverified | `CompletionTests` checks inferred attribute/stack bounds, merchant skips and Core query-scope apply/reopen/undo; `StackSentinelTests` checks exact `-1` variants | Only existing tokens change; no level/XP, reward, effect or slot dependency repair; warning is retained | [Issue #31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): controlled captures and spare-slot reload for each action |
| F7 character + GlobalSaveDataDefault group edits | Unknown | Same-title, same-generation role validation; staged two-member outputs and memento tests | Each member is reparsed; shared dependencies and external integrity Unknown; warning is retained | [Issue #31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): capture paired files and validate atomic reload |

## Framework feature adoption

| Feature | Status and location | Evidence / reason |
| --- | --- | --- |
| F1 SaveDiff | Used: `XorJsonDecoder` and sample ledger tests | Production SDK hook now shares the bounded token parser; no-op and settings delta tests run against external samples. |
| F2 reference packs | Used: `ReferencePacks/xorjson.community.en` 1.4.0 | MIT identifiers and relationships; 388 deduplicated game icons serve 418 records, with 399 official descriptions and one Overworld background. All carry hashes and provenance. |
| F3 rich table cells | Used: F8 character collections | Inventory/collection image cells and attributed game descriptions; raw details stay lazy. |
| F4 ReferenceSpec | Used: item, effect, enchantment, world, attribute and rarity IDs | Existing-value picker and planned-action parameters use native catalog validation, category filtering and declared sorting. |
| F5 structural edits | Used: `JsonEditModel` / detached mementos | Generic object/array add, remove and replace preserve unknown members and support undo/redo; output is reparsed. |
| F6 planned actions | Used: `JsonActionPlanner` | Added in `16679ae`: scalar, attribute, stack, item and rarity actions preview changes/skips/warnings and accept F8 selected/query scopes through Core. |
| F7 document groups | Used: character + GlobalSaveDataDefault; SDK-04 `DocumentMemberAdapter` / `DocumentMemberRouter` | Same-generation role validation, unchanged role/key rows and navigation policies, complete mementos and staged multi-file serialization. |
| F8 queryable tables | Used | Extended in `16679ae`: seven character collections plus automatic server-backed tables for raw arrays with 100+ rows. |
| F9 graphs / maps | Used: `CharacterSpatialViews` / `CharacterMapView` | Bounded quest DAG and attributed Overworld reference art; saved locations remain unpositioned with text alternatives because no verified ID-to-pixel mapping exists. |
| F10 exact numbers | Used: token parser and `ExactNumberSpec` | Numeric lexemes, nulls, duplicate members and unchanged spelling survive edits. |
| F11 shared codecs | N/A | XorJson is UTF-8 JSON with byte transforms; it has no Kraken, SQLite or Oodle payload. |
| F12 decoder hook | Used: `XorJsonDecoder` | Added in `16679ae`: `ISaveDecoderProvider` exposes `xorjson.tokens`; F1 tests use `HookSaveDecoder`, with depth/value/byte bounds. |
| F13 inspection / browse picker | Used | `json/inspect` is explicitly non-mutating; catalog pickers inherit browse search/category/sort from Core. Float duration: N/A — no evidenced float time unit. |

Shared SDK use includes `BoundSaveDocument`, `ExactJsonNumber`, F7 group
contracts and F8/F6 interfaces. Collection queries, plan revalidation, icon
nodes and quest-catalog enumeration remain local implementations; this plugin
does not use `InMemoryCollection<T>`, `ActionPlans`, `ReferenceViews.Icon`,
`ReferenceInspection` or `ReferenceCatalogReader`. Binary cursor/FString/CRC
helpers are N/A for this JSON layout without an observed checksum. SDK-04 supplies
member identity mapping, scope/plan dispatch and edit partitioning. SDK-06 now
supplies the lossless JSON tree and scalar patch substrate; F5 structural edits,
plugin identities, history and transformed envelopes remain local policy.
F14 empty-slot presentation is N/A: no vacant-slot sentinel is established for
these variable-length inventory records; the stack `-1` means non-stackable.

The pack sidecars and this README document pinned sources, modifications, hashes and PS5 uncertainty. Game artwork and game descriptions are attributed under the owner decision of 2026-10-29; earlier community facts retain their 2026-10-28 policy basis. No saved-location coordinates or wiki text are imported.

## Typed paths, exact values and preservation

The parser retains source bytes, UTF-8 value/member spans, ordered children,
duplicate keys, escapes, BOM and whitespace. Typed paths distinguish object
members with duplicate occurrence numbers from array positions:
`$.member("a/b",0).element(2)`. Names containing punctuation cannot masquerade
as paths. Opaque `value/<record>` references are assigned independently of those
positions and survive sibling insertions/removals within a document.

All editable numbers use F10 `ExactNumberSpec` inside `NullableSpec`. The numeric
lexeme is emitted directly: `-0`, `1.2300E+0002`, integers larger than Int128 and
very large/small exponents never pass through double or decimal. Limits are
16,384 characters and absolute explicit exponent 1,000,000,000. Larger valid
JSON numbers remain verbatim/read-only. Existing nulls use an explicit nullable
number/string/boolean union. Null, empty text and absent members are distinct.
Changing a scalar to null does not delete its member.
An original null retains its explicit nullable/union type selector after a
catalog string assignment, with attributed labels and catalog eligibility for
string replacements. Other published scalar types remain selectable.

Unchanged scalar tokens retain their exact bytes; changed tokens alone are
patched. Every edit batch and serialization reparses/reopens the result and
checks the encoding, structure and scalar values. Invalid Unicode/type edits,
oversized output and ambiguous encodings roll back atomically. No-op and undo
restore the original encoded bytes exactly, including original string escapes.

Bounds: 16 MiB, depth 64, 250,000 values, at most 128 children per lazy expansion.
Malformed grammar, truncated containers and invalid UTF-8 (including member
names) are declined by `Probe`; `Open` and the `xorjson.tokens` decoder reject
them with `SaveFormatException`. Both transforms must be checked: short inputs
such as encoded empty containers are intentionally rejected when ambiguous.
Synthetic corruption tests cover every zero-, one- and two-byte input, every
truncated prefix of a BOM/multibyte character document in all three encodings,
and complete/truncated arrays and objects nested 100,000 levels deep. Existing
accepted/rejected byte, depth and value-count boundary pairs remain covered.
Plugin instances are stateless; documents do no filesystem or network I/O and
depend only on the SDK. The token editor originated in this repository's
GenericJson implementation; this plugin has no reference to that plugin.

## Views and structural operations

Character tabs group Hero metadata/attributes, Inventory, Quests, World,
Collections/achievements and Raw / Unknown. Every unknown field remains
addressable in the bounded lazy tree. Observed global blobs are named Audio,
General, Gfx, Controls, KeyBindings, Interface and Accessibility. Settings still
use conservative raw scalar specs: no game-safe ranges/enums have been proven.

Seven F8 server-backed tables cover stored attributes, inventory entries,
quests, quest tasks, world records, achievements and collection records. Search
and exact column filters cover the complete collection, including rows beyond
the current page. Attribute free-text search also matches the attributed names
already displayed by the pinned catalog/picker (for example, `Echo shards`
matches raw `SpringStone`), case-insensitively. Raw identifiers and values remain
searchable; exact column filters still match raw column text, and unknown IDs
or missing packs retain the raw fallback. Labels do not replace row identities.
Offset pages contain at most 200 rows; sorts are ordinal
lexical (including number columns), with ascending numeric token allocation IDs
as tie-breaks in both sort directions.
No page offset or catalog ID is used as a row identity, so repeated IDs remain
distinct. Only requested rows become view nodes. Raw details stay lazy.

Inventory columns show a local icon, the raw TypeTag, attributed name/category, RarityTag,
EquippedSlot, TargetSlotOverride, StackCount, generated ItemPower and stored
effect count. Target slots are shown verbatim, including merchant tags; they
are not interpreted as proof of ownership or equip eligibility. Details retain
progression, generator data, effects/enchantments and all unknown fields.
Quest tables show stored state, focused-ID equality and task counts; task rows
show their parent quest, raw state and exact PartialProgress. World rows show
stored tags, door/actor IDs where present, raw state and fog data lengths.
Fog bytes, positions and sizes remain available in details; no map coordinates
or exploration percentage are invented. Achievement completion/counts and
collection membership are storage observations, not reward evidence. Existing
completion/count cells and collection-tag scalars are editable under the same
Experimental character gate; computed counts and catalog labels are display-only.

The supplied character yields 9 attribute, 5 inventory, 49 quest, 425 task,
2 world, 32 achievement and 3 collected-record rows. Empty lists stay empty;
missing/ambiguous members display no guessed value, with their original shape
retained in raw views. Duplicate identifiers keep distinct session keys.

Inventory entries show candidate `GenesisRandomSeed + occurrence` identity and
a stable session reference. Missing seeds use the session reference. Seed
persistence/uniqueness across real PS5 captures remains unverified; array index
is never the edit reference.

Generic obfuscated documents expose append/add, remove and replace operations
on array elements/object members. New object keys must be unique. Root removal
or replacement is refused. Existing unknown values, key spelling, colon spacing
and member trivia are retained; new separators/fragments use valid JSON.
F5 `Structural(...)` stores detached model snapshots, original token bytes,
identities and allocation state. Undo/redo can mix scalar and structural steps;
retired identities are not reused for a different new record. Whole-section
lazy IDs stay stable when row counts change, allowing Core to rebind survivors.

Settings and tested offline characters expose warned F5 structural actions. Raw
operations preserve unknown fields and reject root removal or ambiguous output,
but they do not prove game dependencies. Inventory add/delete/equip, effect
synthesis and quest/world semantics remain unproven even when existing tokens can
be edited. No proven PS5 caps, reward behavior or integrity repair is claimed.
Serialization rejects changes that alter the character format, the tested-version
gate or offline status. Read-only characters still publish raw structural
controls, but dispatch rejects writes; the non-mutating inspection remains usable.

The quest graph uses PC catalog prerequisite relationships. Stored `Completed`
maps to Unlocked, `Available`/`Active` to Available and `Unavailable` to Locked;
absent, duplicate and unrecognized states remain Unknown. Graphs and maps show
at most 512 items each, with text alternatives and links to existing fields
(or format inspection when no unambiguous quest field exists). All other data
remains in the tables/raw tree; no PS5 prerequisite mapping is proven.

## Reference catalogs and picker

The embedded F2 pack `xorjson.community.en` version 1.4.0 contains **328 item**,
**60 effect**, **30 enchantment**, **9 attribute**, **5 rarity** and **1,346 world/quest/task** records. Catalog
IDs are `xorjson-items`, `xorjson-effects`, `xorjson-enchantments`,
`xorjson-attributes`, `xorjson-rarities` and `xorjson-world`. This covers every
base/unique item ID, effect/enchantment ID,
quest ID/task ID, 42 short quest names and 502 short task labels in the pinned
MIT source, plus the previous world identifiers. World labels without upstream
names are mechanically expanded IDs, not official translations. All entries are **Inferred**, carry
revision/source metadata, and explicitly disclaim PS5 compatibility. Only 2 of
49 quest IDs and 37 of 425 task IDs in the PS5 sample match that PC catalog;
unmatched values stay raw. Catalog size is not a PS5 coverage claim.

`Open(file, references)` uses the host's pinned `IReferenceContext`. Item type
tags publish F4 `ReferenceSpec` fields with `AllowCustom=false`: Core's native
picker supports search/categories and rejects unknown replacements. Character
references are writable for the tested offline gate, but picker replacements
remain Experimental and warn that item dependencies are unverified. Generic
obfuscated reference fields can use the picker. Existing unknown IDs remain
visible and preserved; the no-pack path falls back to raw IDs without inventing
labels.
Descriptions include per-record attribution; the Sources panel exposes each
dataset's licence and provenance. No catalog reads trigger network I/O.

Round-2 tests exercise the existing-value picker through Core with both byte
transforms, known and unknown original IDs, an escaped original ID, rejected
unknown replacements, exact token-only output, reopen, undo and redo. These
prove the generic token editor's behavior. They do not establish that replacing
a real PS5 inventory ID is safe. Character item replacement, quantities, power,
rarity, effects, equip and creation remain Experimental and carry the owner
warning. New F6 attribute, stack, item-ID and rarity plans only alter existing
tokens, skip merchant/special rows and report unverified PC bounds/dependencies.
Bulk stack actions also preserve every exact numeric spelling of the existing
non-stackable `-1` sentinel, including fractional and exponent notation.
Attribute plans use each catalog record's inferred minimum/maximum; non-XP
attributes require integer spelling. Stack plans require integer spelling in
`1..9999`. Item replacements must keep the original catalog category. All three
inventory actions skip target-slot overrides other than `None`/empty, unknown items and
Cosmetic/QuestItem/Currency categories. Attribute/item/rarity plans require the
corresponding catalog; inventory eligibility also requires the item catalog.
These action-specific bounds do not constrain individual raw scalar edits.
The generic **Set selected scalar values** action uses F6
`IActionPlanningDocument` preview/apply, including skipped incompatible rows and
one atomic undo step; it does not synthesize game-aware presets.
Numeric literals beyond the exact-number editing limits are rejected by this
action; their read-only text fallback is never written as a JSON string.
Catalog-valued edits and their undo/redo request a view rebuild so labels,
provenance and picker specs follow the current ID.

All **60 effects** now include original behavior summaries, reported compatible
gear counts and display percentages for tiers I–III, derived from the
[dungeons.tools effect table](https://www.dungeons.tools/2/effects), snapshot
SHA-256 `fce93f92b4d8fcaf99b9d95069a0de34d7dc0694d81009aea44a61e232f580b6`.
That source describes **PC 1.1.2.0 / Steam build 25754144**. The crosswalk uses
unique exact English names from the pinned MIT ID catalog; it does not prove
that a PC effect maps to the same PS5 behavior. The remaining 16 source entries
have no established catalog ID crosswalk and are not added under guessed IDs.
F4 picker details and scalar descriptions show the summaries with attribution.
Percentages are display facts, not serialized `Strength` values, validators or
PS5 limits. All five sample inventory entries have empty effect arrays; there
are **zero populated PS5 effect records** against which to verify those facts.

The asset round adds icons to all **328 items, 60 effects and 30 enchantments**
(**388 distinct files** after decoded-image deduplication). **309 item, 60 effect
and 30 enchantment descriptions** append the official PC game text; 19 talismans
have no source description and keep that absence. Effect game text is explicitly
the tier I display description, alongside the earlier advisory summaries and tier
observations. It never supplies serialized Strength or PS5 validators.

The **2022 × 1340 Overworld WebP** is a reference background only. Its source
tiles have a documented pixel transform, but the save's location IDs have no
verified crosswalk to those pixels and can belong to other regions. Every marker
therefore stays unpositioned/Unknown in the always-available text list. Without
the pack the background and icons are omitted; raw IDs and controls remain.

New image assets total **974,007 bytes**: icons **784,515 bytes** and map
**189,492 bytes**. Icons are at most 64 × 64, encoded as lossless WebP or PNG-8,
whichever is smaller. All downloaded inputs were header-validated and processed
with isolated Python outside the repository; downloaded scripts were never run.
The exact source URLs, input hashes and record mappings are in
[game-assets.sources.json](SaveToolkit.Plugins.XorJson/ReferencePacks/xorjson.community.en/game-assets.sources.json).
See [game-assets.source.md](SaveToolkit.Plugins.XorJson/ReferencePacks/xorjson.community.en/game-assets.source.md)
for conversion details and [effect-notes.source.md](SaveToolkit.Plugins.XorJson/ReferencePacks/xorjson.community.en/effect-notes.source.md)
for the earlier factual summaries.

CC BY-SA text remains approved with attribution and share-alike;
no wiki text was embedded this round. Direct retrieval of Minecraft Wiki's
`Dungeons_II:Sword`, `Dungeons_II:Echo_Shard` and `Minecraft_Wiki:Copyrights`
returned HTTP 403 on 2026-10-28, so page revisions, applicable licence and an
exact PS5 ID/text mapping could not be verified. A future wiki dataset must
retain its own CC BY-SA attribution, licence/version link, retrieval/revision
and modifications next to the data, in this table, Info.References and the UI.

## Sources and attribution

Identifier data is pinned to revision
`84ffee67b73e49d5b888e31257ce9767aa8ab826`, retrieved 2026-10-28. Its MIT copyright
notice is retained in the pack's `LICENSE.txt`; dataset modifications are in
`pack.json` and [catalogs.source.md](SaveToolkit.Plugins.XorJson/ReferencePacks/xorjson.community.en/catalogs.source.md).

| Dataset / research | Author and source | Licence / use |
| --- | --- | --- |
| Game icons and official descriptions | Mojang Studios/Microsoft; extracted by dungeons.tools: [weapons](https://www.dungeons.tools/2/weapons), [armor](https://www.dungeons.tools/2/armor), [artifacts](https://www.dungeons.tools/2/artifacts), [effects](https://www.dungeons.tools/2/effects), [enchantments](https://www.dungeons.tools/2/enchantments); PC 1.1.2.0 / Steam 25754144; retrieved 2026-10-29 | Rights reserved by Mojang Studios/Microsoft; used with attribution per owner decision 2026-10-29. 418 icon associations, 388 deduplicated 64-pixel files, 399 official descriptions; exact-name matches constrained to gear/book/effect/enchantment classes. Original MIT identifier notices remain. |
| Overworld map art | Mojang Studios/Microsoft; dungeons.tools [map](https://www.dungeons.tools/2/map) and its tile data; retrieved 2026-10-29 | Rights reserved by Mojang Studios/Microsoft; used with attribution per owner decision 2026-10-29. Zoom-3 tiles stitched/cropped to 2022 × 1340, WebP quality 82. Saved ID-to-pixel alignment is unverified; all markers remain unpositioned. |
| Item IDs, short names, categories | Ishiaki and mcd2-save-editor contributors, [items.json](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/data/items.json) | [MIT](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/LICENSE); base/unique ID-name pairs and optional slot; no descriptions, artwork or caps. |
| Effect IDs, short names, categories | Same contributors, [effects.json](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/data/effects.json) | MIT; 60 effects, no tiers, numbers or tooltip prose. |
| Effect summaries, compatible gear counts, tier display percentages | dungeons.tools community author, [effects](https://www.dungeons.tools/2/effects), retrieved 2026-10-29; PC 1.1.2.0 / Steam 25754144; snapshot SHA-256 `fce93f92b4d8fcaf99b9d95069a0de34d7dc0694d81009aea44a61e232f580b6` | No licence published; reused with attribution per owner decision 2026-10-28. 60 unique-name crosswalks; original summaries remain alongside newly appended official descriptions. MIT continues to cover identifiers. Dataset metadata, each record, UI and sidecar retain both sources. No display percentage becomes a write limit. |
| Enchantment IDs / short names | Same contributors, [effects.json](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/data/effects.json) | MIT; 30 `enchantments` ID/name pairs, no strengths, points or tiers. |
| Attribute and rarity catalogs | Same contributors, [hero.py](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/hero.py) | MIT; 9 attribute identifiers/labels with inferred PC bounds and 5 rarity identifiers/labels. Attribute bounds constrain F6 plans, not raw fields, and are not proven PS5 limits. |
| World/quest/task IDs and labels | Same contributors, [world.json](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/data/world.json) | MIT; 420 original identifiers plus 926 task IDs. Quest/task short labels from `names`/`labels`; other labels expanded from IDs. No reward rules or coordinates. |
| Shifted settings / plain character codec | Same project, [codec.py](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/codec.py) | MIT research corroboration; inspected as text, not executed. No downloaded Python implementation is shipped or executed. |
| Character/quest/world paths | Same project, [hero.py](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/hero.py), [story.py](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/story.py), [world.py](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/world.py) | MIT schema research, checked against PS5 bytes where populated. Empty PS5 lists do not prove PC door/actor/effect semantics. No implementation port. |
| JSON grammar | IETF, [RFC 8259](https://www.rfc-editor.org/rfc/rfc8259) | Specification reference; local token writer, SDK exact-number grammar validation. |
| PS5 title identity | [ProsperoPatches PPSA16064](https://prosperopatches.com/PPSA16064) | Title identification only; no data/code embedded. |

## Licence exclusions

| Source (pinned revision / snapshot) | Licence found / terms | Reason excluded | Alternative | What would unblock it |
| --- | --- | --- | --- | --- |
| dungeons.tools community effect descriptions/facts; PC 1.1.2.0, snapshot `fce93f92b4d8fcaf99b9d95069a0de34d7dc0694d81009aea44a61e232f580b6` | No licence published; reused with attribution per owner decision 2026-10-28 | **Resolved:** 60 factual effect summaries imported/regenerated with attribution. | Retain MIT IDs, exact-name crosswalk warnings, source hashes and original summaries. | Resolved; no author permission request pending. |
| MetaBot descriptions, icons and data; [methodology](https://metabot.gg/en/minecraft-dungeons-2/methodology), PC 1.1.1.0, and [terms](https://metabot.gg/en/legal/terms-of-service), effective 2026-04-01; earlier snapshot hashes in sidecar | General personal/non-commercial terms remain; publisher-asset refusal superseded by owner decision 2026-10-29. | Publisher assets are no longer refused. Direct MetaBot import is unnecessary because equivalent game content is sourced from dungeons.tools. | Attributed dungeons.tools assets/text, with original MIT identifier provenance. | No publisher-asset blocker remains; direct community-data reuse would still require reviewing MetaBot's applicable terms. |
| dungeons.tools verbatim game tooltips, icons and art; [privacy notice](https://www.dungeons.tools/privacy); current snapshots in game-assets.sources.json | Rights reserved by Mojang Studios/Microsoft; used with attribution per owner decision 2026-10-29. | **Resolved: imported (owner decision 2026-10-29).** 388 icons, 399 official descriptions and one map background. | Local F2 assets and text, full attribution, unpositioned map fallback. | Resolved; no permission request pending. |

No GPL-2.0-only code or downloaded implementation was imported or executed.

## Validation and remaining phases

Run from the repository root (PowerShell):

```powershell
$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'
$env:SAVETOOLKIT_TEST_TEMP='D:/Temp/saveeditor/test-scratch/docs/accuracy-xorjson'
$env:TEMP=$env:SAVETOOLKIT_TEST_TEMP
$env:TMP=$env:TEMP
New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null
dotnet test --project plugins/XorJson/SaveToolkit.Plugins.XorJson.Tests -c Release
python docs/tests/test_documentation.py
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
```

Tests cover sample no-op, character/version/online gates, named settings,
F1 exact settings deltas, F10 huge numbers/null/typed paths, malformed/corrupt
input, atomic size rollback, bounded lazy views, catalog hashes/provenance,
native reference validation and picker history, F8 full-list search/filter/page
keys and Experimental session writes/undo, F5 detached snapshots, mixed Core undo/redo,
stale IDs/revisions, duplicate members, replacement trivia and ambiguous output.
Sample tests skip only when samples are absent; skipped runs are not validation.

This write-enable round keeps the plugin Experimental: controlled captures,
legal gameplay ranges, populated effect/enchantment cards, reward/dependency
validation and console acceptance remain open. F7 now stages same-generation
character/global members atomically; paired-file membership and shared game
dependencies remain Unknown.

## Owner capture and console checklist

1. Record PS5 title/content ID, region, exact game build, DLC, offline/online mode,
   slot, cloud-sync state and displayed values for every pair. Keep original backups.
2. Capture a control pair with no intentional change, a new offline hero and a
   progressed offline hero (including SerializeMeta SoftVersion/FormatHash).
3. Capture separate before/after pairs for: one currency gain/spend; one XP/level
   gain; one item pickup; one equip/unequip; one rarity/effect change; one quest
   step/reward; one map/station discovery; one master-volume change.
4. Include non-empty effects/enchantments, a talisman, merchant/cosmetic rows and
   several progression states. Note exact item/slot/rarity/effect and visible counts.
5. On a spare offline slot, validate one settings scalar edit first: import,
   load, check the displayed value, save in-game, reload, then capture the rewritten
   files and compare unrelated fields. Do the same for each proposed hero action
   only after the controlled captures establish its mapping and dependencies.
6. With disposable backed-up copies, record the game's response to one controlled
   malformed/altered token. Report rejection, clamping, repair or external-integrity
   behavior; do not infer checksum absence from valid JSON alone.
7. For each proposed picker replacement, capture a natural item change and
   record both exact TypeTags, raw rarity, power, stack, generator seed,
   EquippedSlot, TargetSlotOverride, effects/templates and related changes.
   Include the same base item in merchant stock, the bag and equipped slots.
   Confirm catalog label/category against the PS5 UI. Validate one replacement
   per spare-slot load/save/reload before enabling that ID/slot combination.
8. Capture repeated identical-type items, a reorder and an equip/unequip pair
   to establish seed persistence/duplicates. Provide non-empty effect and
   enchantment records with visible tier/strength and separate quest partial
   progress, completion and reward-claim pairs. Record which achievement,
   collection, door, station, fog and actor values change as side effects.
9. For any requested bulk action/preset, first provide individual proven edits,
   valid limits/dependencies and the expected filtered scope/exclusions. Validate
   its F6 preview, atomic apply and whole-action undo on a spare offline slot.
10. For character/global coordination, capture both files before/after changing
    the active character and a suspected shared progression action. Identify
    membership by slot/title/generation and show which fields must agree.

## SDK requests

- F1/F2/F3/F4/F5/F8/F9/F10/F12/F13 are used directly. F6 is used for the
  enabled scalar bulk action; unsupported gameplay presets remain absent.
- The Core overlay regression is fixed on main (`4532481`). The plugin-local
  all-files workaround is removed: F1 supplies only the settings output and
  verifies the untouched character bytes and unchanged-file report.
- **F7 (#29) merged on main as `7ff21ad` and adopted here.** The plugin uses
  `IDocumentGroupPlugin.ProposeGroups` / `ValidateGroup` / `OpenGroup` and
  `IGroupedSaveDocument.CaptureMemento` to explicitly bind the
  selected character and global files. The plugin checks that both belong to
  the same save set, title, imported generation and backup status, validates
  their parsed character/global roles, and verifies byte-identical no-op outputs.
  It does not establish slot-to-global matching or shared gameplay dependencies.
  Core reserves one owner per output, fingerprints immutable inputs, stages
  every output before publishing anything, and provides one revision/undo/redo
  unit and atomic verified export. Unrelated settings stay independently editable.
  The group prefixes member identities by role, captures complete immutable
  multi-member state restorable into a fresh instance, and serializes every
  selected member exactly once.
  Every proposal includes the selected file; the alternative limit counts only
  its eligible partners. Backups must be explicit members. Group integration tests
  cover mixed scalar/structural history, a rejected later member, stable duplicate
  row identities and exact ZIP outputs through repeated undo/redo for both transforms.
  The group emits warnings because controlled
  paired-file evidence is still absent; no plugin-local transaction coordinator
  bypasses Core.
- No new SDK gap blocks these views or writes. Game-specific dependency validation,
  integrity behavior and PS5 write acceptance still need owner evidence.
- **Adopted: SDK-04 — shared group member adapter.** `XorJsonGroupDocument`
  composes `DocumentMemberAdapter`, `DocumentMemberIdentityPolicy`,
  `DocumentMemberRouter` and `DocumentMemberEdits`, replacing `XorJsonGroupMember`.
  The [migration recipe](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6489105887)
  preserves role/key query rows, traversal scopes, navigation paths, unprefixed
  graph/marker identities and collection-column editability. F6 diagnostics retain
  their existing local IDs through a compatibility projection; changes and skipped
  rows use SDK qualification. Pre-migration view/protocol fingerprints cover both
  byte transforms, including failed plans and byte-identical snapshot restoration.
  Whole-group transactions, snapshots, verification, inspection and disposal remain
  plugin-owned. Restore keeps the same member documents, so the router stays valid.
  The [post-rc.2 review](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/40#issuecomment-6490457978)
  found that malformed same-member selection keys were skipped. Plans now reject the
  entire selection for unknown, retired or noncanonical token IDs; SDK member ownership
  rejects foreign roles without adding another identity parser. Errors name the full
  group key, and rejected previews publish no token or changes. Valid incompatible rows
  still skip normally. Duplicate policy is unchanged: direct plugin plans deduplicate
  ordinally, while Core rejects duplicates before dispatch with its distinct-key error;
  SDK-04 preserves opaque IDs and performs no normalization. Both transforms have
  regression coverage for mixed selections, both key orders, duplicate handling,
  apply revalidation and exact export/undo bytes. The historical protocol fingerprint
  explicitly projects only the formerly skipped unknown-key response into its old shape;
  that response is separately asserted rejected. All other protocol/view/export fields
  retain their pre-migration hashes.
- **Adopted: SDK-06 — shared lossless JSON tree/scalar patch substrate.**
  `JsonTokenTree` delegates strict UTF-8/BOM/trivia/duplicate-property parsing
  and bounded scalar spans to `LosslessJsonDocument`; unchanged-structure writes
  use its document-owned scalar replacement and serialization. The adapter maps
  SDK IDs, `PropertyNameSpan`, decoded names and duplicate occurrences into the
  existing `value/{Index}` identities. The local writer/model still owns F5
  add/remove/replace, detached structural snapshots, F6/F7 history, title gates
  and XOR/shift envelopes. The readiness contract is recorded in
  [XORJSON-SDK06-READINESS.md](XORJSON-SDK06-READINESS.md); migration evidence is
  in [XORJSON-SDK06-HANDOFF.md](XORJSON-SDK06-HANDOFF.md).

The dispositions above match [docs/SDK_REQUESTS.md](../../docs/SDK_REQUESTS.md):
SDK-04 (member composition), SDK-06 (lossless JSON scalar substrate), SDK-22 (F7
transactions) and SDK-23 (overlay preservation) are resolved and adopted here.

## Performance review

See [PERFORMANCE.md](PERFORMANCE.md) for the #40 findings, reproducible D: harness,
baseline/final allocation and latency measurements, regression budgets and cache
invalidation rules. Measurements use Microsoft's
[thread-local allocation API](https://learn.microsoft.com/en-us/dotnet/api/system.gc.getallocatedbytesforcurrentthread);
allocation tests use xUnit's
[isolated collection execution](https://xunit.net/docs/running-tests-in-parallel#selectively-opting-out-of-parallelism).
