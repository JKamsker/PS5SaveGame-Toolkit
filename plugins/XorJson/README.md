# Obfuscated JSON / Dungeons II (`format.json-xor`)

**Experimental, version 0.2.0.** Lossless token editing for byte-minus-one and
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

The embedded F2 pack `xorjson.community.en` contains **328 item**, **60 effect**
and **420 world** records. The catalog IDs are `xorjson-items`,
`xorjson-effects`, and `xorjson-world`. Labels/categories come from the pinned
MIT community dataset; world display labels are mechanically expanded internal
IDs, not official quest translations. All entries are **Inferred**, carry
revision/source metadata, and explicitly disclaim PS5 compatibility.

`Open(file, references)` uses the host's pinned `IReferenceContext`. Item type
tags publish F4 `ReferenceSpec` fields with `AllowCustom=false`: Core's native
picker supports search/categories and rejects unknown replacements. Character
references are read-only, so their picker cannot change gear. Generic obfuscated
reference fields can use the picker. Existing unknown IDs remain visible and
preserved; the no-pack path falls back to raw IDs without inventing labels.
Descriptions include per-record attribution; the Sources panel exposes each
dataset's licence and provenance. No catalog reads trigger network I/O.

No game artwork, tooltip prose, quest prose, PC caps, effect strengths or tiers
are embedded. The upstream sources mention MetaBot and player-read game text;
only curated identifiers, short labels and categories are extracted under the
community repository's MIT terms. Unlicensed source descriptions and icons are
excluded. CC BY-SA sources remain an approved option for future attributed text.

## Sources and attribution

All community data is pinned to revision
`84ffee67b73e49d5b888e31257ce9767aa8ab826`, retrieved 2026-10-28. Its MIT copyright
notice is retained in the pack's `LICENSE.txt`; dataset modifications are in
`pack.json` and [catalogs.source.md](SaveToolkit.Plugins.XorJson/ReferencePacks/xorjson.community.en/catalogs.source.md).

| Dataset / research | Author and source | Licence / use |
| --- | --- | --- |
| Item IDs, short names, categories | Ishiaki and mcd2-save-editor contributors, [items.json](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/data/items.json) | [MIT](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/LICENSE); base/unique ID-name pairs and optional slot; no descriptions, artwork or caps. |
| Effect IDs, short names, categories | Same contributors, [effects.json](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/data/effects.json) | MIT; no tiers, numbers or tooltip prose. |
| World IDs / generated labels | Same contributors, [world.json](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/data/world.json) | MIT; quest/station/door/area/region/achievement/cutscene identifiers only; labels generated from those IDs. |
| Shifted settings / plain character codec | Same project, [codec.py](https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/codec.py) | MIT research corroboration; inspected as text, not executed. No downloaded Python implementation is shipped or executed. |
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
native reference validation, F5 detached snapshots, mixed Core undo/redo,
stale IDs/revisions, duplicate members, replacement trivia and ambiguous output.
Sample tests skip only when samples are absent; skipped runs are not validation.

This round delivers the engineering portions of phases 1–2 and the catalog/F5
foundations from phases 3–5. **No full phase is declared complete:** controlled
captures, legal gameplay ranges, advanced semantic tables/cards, reward/dependency
validation and console acceptance remain open.

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

## SDK requests

- F1/F2/F4/F5/F10 are available and used; no plugin-local replacements are needed.
- **Core overlay regression observed during F1 validation:** constructing
  `OverlaySaveSet` with only the changed global file made the untouched character
  appear zero-length. Its conditional `content : null` appears to select the
  implicit byte-array → memory conversion before nullable wrapping. The plugin's
  F1 test supplies explicit content for every file as a scoped workaround. Core
  should test/fix absent overrides independently; no Core files were changed.
- Future cross-file character/global transactions and game-specific dependency
  validation remain outside this round. No invented checksum or caps are used.
