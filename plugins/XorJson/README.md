# Obfuscated JSON / Dungeons II (`format.json-xor`)

**Experimental, version 0.3.0.** Lossless token editing for byte-minus-one and
synthetic XOR-1 JSON, plus a **read-only** Minecraft Dungeons II character view.
Only an owner can promote console-tested features to Stable.

## Coverage and evidence

| File / format | Evidence and behavior |
| --- | --- |
| `GlobalSaveDataDefault/ue4savegame.dpx.sav` | One 3,978-byte PS5 snapshot. Adding one modulo 256 to every byte produces valid UTF-8 JSON. XOR-1 is invalid. Existing scalar settings remain experimental/editable; structural actions are disabled. |
| `Character*/ue4savegame.dpx.sav` | One 40,062-byte plain UTF-8 JSON snapshot. Requires an unambiguous object `SerializeMeta.HardFormat=FCharacterSaveV1` and object `CharacterSaveV1`; claimed at Engine score, above generic JSON. Every field is read-only. |
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

Unchanged scalar tokens retain their exact bytes; changed tokens alone are
patched. Every edit batch and serialization reparses/reopens the result and
checks the encoding, structure and scalar values. Invalid Unicode/type edits,
oversized output and ambiguous encodings roll back atomically. No-op and undo
restore the original encoded bytes exactly, including original string escapes.

Bounds: 16 MiB, depth 64, 250,000 values, at most 128 children per lazy expansion.
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
the current page. Offset pages contain at most 200 rows; sorts are ordinal
lexical (including number columns), with stable session-token IDs as tie-breaks.
No page offset or catalog ID is used as a row identity, so repeated IDs remain
distinct. Only requested rows become view nodes. Raw details stay lazy.

Inventory columns show the raw TypeTag, attributed name/category, RarityTag,
EquippedSlot, TargetSlotOverride, StackCount, generated ItemPower and stored
effect count. Target slots are shown verbatim, including merchant tags; they
are not interpreted as proof of ownership or equip eligibility. Details retain
progression, generator data, effects/enchantments and all unknown fields.
Quest tables show stored state, focused-ID equality and task counts; task rows
show their parent quest, raw state and exact PartialProgress. World rows show
stored tags, door/actor IDs where present, raw state and fog data lengths.
Fog bytes, positions and sizes remain available in details; no map coordinates
or exploration percentage are invented. Achievement completion/counts and
collection membership are read-only storage observations, not reward evidence.

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

**Game settings and all characters expose no structural actions and reject
direct structural calls.** Inventory add/delete/equip, gameplay scalar edits,
effect synthesis and quest/world writes need controlled PS5 evidence and spare
slot acceptance. No caps, reward behavior or integrity repair is guessed.

## Reference catalogs and picker

The embedded F2 pack `xorjson.community.en` version 1.1.0 contains **328 item**,
**60 effect**, **30 enchantment** and **1,346 world/quest/task** records. Catalog
IDs are `xorjson-items`, `xorjson-effects`, `xorjson-enchantments` and
`xorjson-world`. This covers every base/unique item ID, effect/enchantment ID,
quest ID/task ID, 42 short quest names and 502 short task labels in the pinned
MIT source, plus the previous world identifiers. World labels without upstream
names are mechanically expanded IDs, not official translations. All entries are **Inferred**, carry
revision/source metadata, and explicitly disclaim PS5 compatibility. Only 2 of
49 quest IDs and 37 of 425 task IDs in the PS5 sample match that PC catalog;
unmatched values stay raw. Catalog size is not a PS5 coverage claim.

`Open(file, references)` uses the host's pinned `IReferenceContext`. Item type
tags publish F4 `ReferenceSpec` fields with `AllowCustom=false`: Core's native
picker supports search/categories and rejects unknown replacements. Character
references are read-only, so their picker cannot change gear. Generic obfuscated
reference fields can use the picker. Existing unknown IDs remain visible and
preserved; the no-pack path falls back to raw IDs without inventing labels.
Descriptions include per-record attribution; the Sources panel exposes each
dataset's licence and provenance. No catalog reads trigger network I/O.

Round-2 tests exercise the existing-value picker through Core with both byte
transforms, known and unknown original IDs, an escaped original ID, rejected
unknown replacements, exact token-only output, reopen, undo and redo. These
prove the generic token editor's behavior. They do not establish that replacing
a real PS5 inventory ID is safe. Character item replacement, quantities, power,
rarity, effects, equip and creation remain disabled pending the owner checklist.
No bulk gameplay action or preset is exposed. Future bulk operations must use
F6 `IActionPlanningDocument` preview/apply with `SupportsPlanning`, including
F8 query scopes, exclusions, skipped rows and one atomic undo step.
Catalog-valued edits and their undo/redo request a view rebuild so labels,
provenance and picker specs follow the current ID.

No game artwork, tooltip prose, long quest prose, PC caps, effect strengths or tiers
are embedded. The upstream sources mention MetaBot and player-read game text;
only curated identifiers, short labels and categories are extracted under the
community repository's MIT terms. Unlicensed source descriptions and icons are
excluded. CC BY-SA text remains approved with attribution and share-alike;
no wiki text was embedded this round. Direct retrieval of Minecraft Wiki's
`Dungeons_II:Sword`, `Dungeons_II:Echo_Shard` and `Minecraft_Wiki:Copyrights`
returned HTTP 403 on 2026-10-28, so page revisions, applicable licence and an
exact PS5 ID/text mapping could not be verified. A future wiki dataset must
retain its own CC BY-SA attribution, licence/version link, retrieval/revision
and modifications next to the data, in this table, Info.References and the UI.

## Sources and attribution

All community data is pinned to revision
`84ffee67b73e49d5b888e31257ce9767aa8ab826`, retrieved 2026-10-28. Its MIT copyright
notice is retained in the pack's `LICENSE.txt`; dataset modifications are in
`pack.json` and [catalogs.source.md](SaveToolkit.Plugins.XorJson/ReferencePacks/xorjson.community.en/catalogs.source.md).

| Dataset / research | Author and source | Licence / use |
| --- | --- | --- |
| Item IDs, short names, categories | Ishiaki and mcd2-save-editor contributors, [items.json](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/data/items.json) | [MIT](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/LICENSE); base/unique ID-name pairs and optional slot; no descriptions, artwork or caps. |
| Effect IDs, short names, categories | Same contributors, [effects.json](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/data/effects.json) | MIT; 60 effects, no tiers, numbers or tooltip prose. |
| Enchantment IDs / short names | Same contributors, [effects.json](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/data/effects.json) | MIT; 30 `enchantments` ID/name pairs, no strengths, points or tiers. |
| World/quest/task IDs and labels | Same contributors, [world.json](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/data/world.json) | MIT; 420 original identifiers plus 926 task IDs. Quest/task short labels from `names`/`labels`; other labels expanded from IDs. No reward rules or coordinates. |
| Shifted settings / plain character codec | Same project, [codec.py](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/codec.py) | MIT research corroboration; inspected as text, not executed. No downloaded Python implementation is shipped or executed. |
| Character/quest/world paths | Same project, [hero.py](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/hero.py), [story.py](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/story.py), [world.py](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/world.py) | MIT schema research, checked against PS5 bytes where populated. Empty PS5 lists do not prove PC door/actor/effect semantics. No implementation port. |
| JSON grammar | IETF, [RFC 8259](https://www.rfc-editor.org/rfc/rfc8259) | Specification reference; local token writer, SDK exact-number grammar validation. |
| PS5 title identity | [ProsperoPatches PPSA16064](https://prosperopatches.com/PPSA16064) | Title identification only; no data/code embedded. |

## Validation and remaining phases

Run from the repository root (PowerShell):

```powershell
$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'; dotnet test --project plugins/XorJson/SaveToolkit.Plugins.XorJson.Tests
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
```

Tests cover sample no-op, character/version/online gates, named settings,
F1 exact settings deltas, F10 huge numbers/null/typed paths, malformed/corrupt
input, atomic size rollback, bounded lazy views, catalog hashes/provenance,
native reference validation and picker history, F8 full-list search/filter/page
keys and read-only session gates, F5 detached snapshots, mixed Core undo/redo,
stale IDs/revisions, duplicate members, replacement trivia and ambiguous output.
Sample tests skip only when samples are absent; skipped runs are not validation.

Round 2 removes the overlay workaround, extends the read-only portions of
phases 2–4, and strengthens the phase-3 catalog/picker foundation. **No full
phase is declared complete:** controlled captures, legal gameplay ranges,
populated effect/enchantment cards, reward/dependency validation and console
acceptance remain open. F7 cross-file integration is deferred, with no local
transaction substitute.

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

- F1/F2/F4/F5/F8/F10 are available and used directly. F6 is the required route for
  future bulk actions; unsupported gameplay actions remain absent.
- The Core overlay regression is fixed on main (`4532481`). The plugin-local
  all-files workaround is removed: F1 supplies only the settings output and
  verifies the untouched character bytes and unchanged-file report.
- **F7 (#29) merged on main as `7ff21ad`; plugin adoption remains deferred.**
  The final `IDocumentGroupPlugin.ProposeGroups` / `ValidateGroup` / `OpenGroup`
  contract and `IGroupedSaveDocument.CaptureMemento` were reviewed from that
  commit. Future opt-in groups must explicitly bind the
  matching character and global files by title, slot and generation, establish
  one owner per output, fingerprint immutable inputs, validate member schemas
  and cross-file dependencies, stage every output before publishing anything,
  and provide one revision/undo/redo unit and atomic verified export. Unrelated
  settings stay independently editable. Group models must prefix all member
  identities by role, capture complete immutable multi-member state restorable
  into a fresh instance, and serialize every selected member exactly once.
  Backups must be explicit members. The remaining blocker is controlled
  paired-file evidence before proposing membership or shared edits, rather
  than a missing framework API. No plugin-local coordinator is implemented.
- No new SDK gap blocks these read views. Game-specific dependency validation,
  integrity behavior and PS5 write acceptance still need owner evidence.
