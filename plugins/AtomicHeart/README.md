# Atomic Heart (`atomic-heart`)

Experimental inventory editor and inspector for decrypted **PPSA10695**, SaveExtension tag/version 1/2,
package 522, engine 4.27.2. The supplied primary and backup are one identical
126,214-byte snapshot. The game patch, region, DLC and known in-game counts have
not been supplied. Broader versions are not validated.

The plugin uses `SaveToolkit.Sdk` and the shared managed Kraken decoder in
`SaveToolkit.Codecs`. It performs no filesystem/network I/O and loads no game DLL.
Unchanged compressed chunks and all unrelated decoded bytes are retained. No save
bytes, extracted records, game images or private asset dumps are embedded.

## Capabilities and confidence

- Decode every frame and verify exact per-chunk, aggregate and inner array lengths.
- Inspect SlotInfo and SlotData tags, manual subsystem/level/actor/component records,
  both class-path strings, tags, byte spans, native payloads and unknown tails.
- Search/filter/sort all 4,063 records through F8, with at most 200 rows per query.
  Open record details for bounded lazy properties and original hex.
- Inspect 90 serialized inventory slots: resources, special resources, ammunition,
  auxiliary items, quest items and the player grid. Empty slots stay visible.
  Storage has no populated inspected slot records in this snapshot.
- F2 supplies 20 attributed public weapon/ammunition labels. F4 `ReferenceSpec`
  fields keep exact short asset IDs; unknown IDs stay raw. Existing populated
  references accept same-category catalog assets with unambiguous full object
  paths already present in the input. No path or DLC availability is invented.
- F1 can walk all 9,107 exposed scalar values with `PluginViewDecoder`. Native
  state, opaque properties, transforms and velocities still require byte diffs.
  A synthetic relocated-property pair proves identity alignment and detection
  of a single changed value. The actual primary/backup have no differences.

**Verified** means the serialized name, type, value or structural boundary was
read from the bytes. It does not establish a gameplay effect, ownership, legal
maximum or PS5 write compatibility. Public PC labels joined by exact asset name
are **Inferred** on PS5. Unmapped references are **Unknown**. No numeric limits,
upgrade prerequisites, skill ownership or quest completion are invented.

Existing populated slots expose bounded Amount edits and an F6 target-Amount
action. Preview lists direct changes, skipped rows and warnings; selected rows
and whole-document scopes are supported. An empty selection changes nothing.
Each apply is one undo group. Empty slots, raw metadata, record-explorer fields,
native data and instance IDs remain read-only.

`Serialize()` patches explicit payloads and enclosing byte counts, reuses unchanged
compressed chunks, emits changed chunks through `SaveToolkit.Codecs.EncodeStored`,
repairs framing/SHA-1, fully decodes/reparses the output and compares every plain
byte with the intended result. No-op serialization (including undoing all edits)
returns a fresh byte-identical copy of the original, without recompression.
Original download and full-set export are already supported by the host.

## Enabled writes and their evidence

Owner decision, 2026-10-28: **Enable writes. Keep warnings.** These three write
paths are enabled now, with `Inferred` confidence and `Experimental` maturity.
They are not blocked on captures tracked in [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31).

| Write | Confidence | Evidence | Integrity status | Owner validation needed |
| --- | --- | --- | --- | --- |
| Existing populated slot `Amount` | Inferred | Explicit signed IntProperty/Int64Property tags. Values must be 0 through the original maximum in that container, bounded by storage width. Zero changes only the scalar; it does not remove a slot. | SHA-1 and byte counts repaired; output decoded/reparsed. Unknown inner integrity/duplicates and stored-block acceptance mean **the game may reject the edited save**. | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): small material/ammo/resource edit, observe/use/spend, save/restart/reload and diff. Observed bounds are not proven game caps. |
| Existing populated `ItemDataAsset` replacement | Inferred | Exact catalog join plus unambiguous full path already observed in this save, same catalog category. ItemId, Amount, grid position, native instance data and unknown bytes are retained. | FString encoding and affected property/struct-array/object/array lengths repaired; SHA-1 repaired; output decoded/reparsed. **The game may reject the save**, or dependent equipment/module/ownership state may not match. | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): one known weapon/ammo reference, inspect equipment/modules/capacity, use/equip, save/restart/reload. Category matching does not prove compatibility. |
| F6 `inventory/set-amount` | Inferred | Same existing Amount bindings; selected or whole-document preview. Empty/unsupported slots, over-limit targets and unchanged values are listed as skipped. | Same repairs and rejection warning as individual amounts. Plans/outcomes retain warnings; stale/tampered plans fail; one undo/redo covers the batch. | [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31): selected and full eligible batches, compare intended and unrelated values after use/resave/reload. |

Every newly writable description, outcome and action preview says **not validated
on console** and **the game may reject the edited save**. Stored output can grow
substantially; file/allocation limits are not console slot-capacity guarantees.
No independent inner checksum is known. No guessed ownership/equipment repairs
are applied.

No prepared structural or multi-file action existed in the previous round.
Creation/removal/move/split, skill/loadout/progression presets and arbitrary raw
field writes remain unsupported because allocation, opaque instance state,
dependent references and event rewards cannot be safely reconstructed. Warning
text cannot prevent losing those unknown structures. This single-file document
does not declare unrelated partners; identical backup mirroring remains the host's
normal export behavior. Evidence is a validation request, not a gate on the three
enabled paths.

## Outer format and integrity

`sdimg_9/ue4savegame.dpx.sav` is a slot, not GVAS. Its
`sdimg_sce_bu_9` twin is identical, not an independent gameplay state.

| Region | Observed layout |
| --- | --- |
| Header | LE int32 tag 1, save version 2, package 522; engine 4.27.2, branch FString, custom version format 3 and GUID/int32 entries |
| SlotInfo | FString class, int32 byte count 1,061; payload at file offset 1,192; UE property tags, `None`, zero int32 |
| SlotData | FString class, algorithm FString `Oodle`, int32 compressed byte count 123,892; framed data at file offset 2,302 |
| Frames | 14 six-u64 headers: magic `0x9E2A83C1`, block size 131,072, compressed/plain sizes, repeated compressed/plain sizes, followed by the codec stream |
| Codec | Kraken decoder 6 (`8C 06` in this sample); shared stored-block `CC 06` streams also accepted |
| Lengths | 13 decoded chunks of 131,072 and one of 47,455; exact aggregate 1,751,391 |
| Trailer | 20 bytes: SHA1(file excluding trailer), required for compatibility, not authentication |

Other Oodle decoder IDs are rejected with an explicit Kraken-only message.
No Mermaid, Selkie or Leviathan implementation or fallback is included. Compressed
and decoded sizes are bounded before allocation. See the codec README for its
work, scratch, recursion and unsupported-stream limits.

## Decoded grammar and preservation

Offsets here are in **decoded SlotData**, including its prefix; they are not
compressed-file offsets. See [EVIDENCE.md](EVIDENCE.md) for measurements and F1.

1. An int32 byte-array length is exactly the remaining decoded byte count:
   1,751,387. This is the serialized `DataBytes` array inside compression.
2. SlotData's tagged UObject begins at offset 4: `Map`, `TimeSeconds`, then
   FString `None` at `0x8B` and a zero int32. Manual data begins at `0x98`.
3. An int32 count introduces five subsystem records. A record contains a name
   FString, **two class-related FStrings**, int32 data length and data bytes,
   int32 tag count and tag FStrings. The second class string is not a constant
   zero: it can contain a path, including when the first string is `None`.
   Their precise engine/fork meanings remain inferred; both are retained.
4. The main level begins at `0x129B`: name FString, actor count, actors. It is
   followed at `0x11D65` by a count of 261 sublevels and their same-shaped records.
   No stock GameInstance/general-filter/level-script layout is assumed.
5. Actors extend the object record with two byte-sized bit flags, 40 native
   transform bytes, int32 movement flag, optional 24 velocity bytes, component
   count and components. Components extend the object record with 40 transform
   bytes. `None` record names consume only their name. Native numeric semantics
   are not exposed as editable fields.
6. Each payload retains its full span. Tagged sequences are inspected only at
   the known payload start, never by scanning for plausible names. Recognized
   inventory structs, selected struct arrays and the grid's int64→struct map
   are bounded and consumed exactly. Native/unrecognized property payloads
   and any post-tag tail remain opaque, with hex access.
7. The player inventory component has a manual count/FString prefix (six
   identifiers here), followed by tags. That list is not treated as stack
   quantities. Slot `Amount`, `ItemId` and `ItemDataAsset` fields are read from
   explicit tags. Grid/map identities and empty-slot identities remain distinct.

The parser consumes the complete outer record grammar to the final byte.
Nested payload inspection is deliberately partial: an unrecognized native body
is retained, not asserted valid as a tagged UObject. No independent inner
checksum has been established. A valid outer SHA-1 is not gameplay validation.
Record IDs use parent/kind/both-class-strings/name; duplicate names receive occurrence suffixes.
Those suffixes are ambiguous across reordered duplicates until controlled
captures prove a stronger identity. Scalar static-array indices are retained.

Limits: 128 MiB file/decoded aggregate; 4,096 custom versions and levels; 1,024
frames, metadata properties or properties per tagged sequence; 32,768 records
or collection entries; 100,000 total property tags; depth 16; 4,096 code units
for names; 1,048,576 for scalar strings; 16 million code units each for record
and property identity paths. Lazy expansions contain at most 100 top-level
nodes; tables and blobs are paged. Impossible framing/counts, unsupported codec
variants and malformed inspected structures throw `SaveFormatException`.

## Validation

Run from the repository root:

```powershell
$env:SAVETOOLKIT_ARTIFACTS='C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts'; dotnet test --project plugins/AtomicHeart/SaveToolkit.Plugins.AtomicHeart.Tests

dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
```

The sample-backed suite covers the complete lazy view, conformance, pack validity,
known layout/spans, every synthetic inner/outer truncation, valid-hash malformed
inner data, unsupported Oodle variants, 500 seeded inner mutations, exact no-op
preservation, F1 alignment/corpus evidence, unknown/native retention, F8 whole-index
queries, and F4 replacement/unknown-reference behavior. Sample-dependent tests skip
when private artifacts are absent; that is not counted as sample validation.

A separate scratch Python parser independently consumed the decoded manual
record grammar and confirmed all record/level counts and the final boundary.
It used the shared decoder's output, so this is **not independent codec agreement**.
The write suite independently reads every new stored `CC 06` frame as literal
bytes without the production parser or shared decoder. Reused compressed streams
must match the original exactly and use the original decoded baseline. This proves
new stored output agrees byte for byte with the intended edit; it is **not** a
second compressed-Kraken implementation. Full independent compressed-corpus
agreement and PS5 console reload remain open. Amount writes get an F1 byte diff
limited to the intended payload; reference writes test growing/shrinking strings
and retention of unrelated records, leaf payloads and opaque tails. Scalar and
selected-bulk session tests cover undo/redo, stale plans and atomic invalid batches.
The write-round suite passed **46 tests, 0 failed, 0 skipped**; API build passed
with **0 warnings and 0 errors**.

## Sources and attribution

Retrieved 2026-10-28. Sources were read, not executed as downloaded programs.
The repository and derived public-tool label dataset are GPL-3.0-or-later.

| Dataset / reference | Source and author | Licence / changes and use |
| --- | --- | --- |
| Embedded weapon/ammunition names | [AtomicHeartMenu features.cpp, fe36f468](https://github.com/Skorchekd/AtomicHeartMenu/blob/fe36f468baacc996c252a1d3231d0d20009127bd/src/features/features.cpp), Copyright (C) 2026 Skorchekd | [GPL-3.0-or-later](https://www.gnu.org/licenses/gpl-3.0.html), including [upstream attribution terms](https://github.com/Skorchekd/AtomicHeartMenu/blob/fe36f468baacc996c252a1d3231d0d20009127bd/NOTICE). Selected 12 weapon and 8 ammunition label/object-name pairs; normalized repeated object names to exact short keys; added categories and original caution text. No runtime code/offsets, limits, artwork or game descriptions copied. Credits retained: Skorchekd; Dumper-7 by Encryqed; MinHook by Tsuda Kageyu; Dear ImGui by Omar Cornut (ocornut). Dataset stays GPL-3.0-or-later. |
| SaveExtension format research | [FileAdapter.cpp](https://github.com/PipeRift/SaveExtension/blob/c40f37a078ce8b9b2b3a811e47729e6ca2d11d48/Source/SaveExtension/Private/FileAdapter.cpp), [Records.cpp](https://github.com/PipeRift/SaveExtension/blob/c40f37a078ce8b9b2b3a811e47729e6ca2d11d48/Source/SaveExtension/Private/Serialization/Records.cpp), [SlotData.cpp](https://github.com/PipeRift/SaveExtension/blob/c40f37a078ce8b9b2b3a811e47729e6ca2d11d48/Source/SaveExtension/Private/SlotData.cpp), [LevelRecords.cpp](https://github.com/PipeRift/SaveExtension/blob/c40f37a078ce8b9b2b3a811e47729e6ca2d11d48/Source/SaveExtension/Private/Serialization/LevelRecords.cpp); Copyright 2015–2020 Piperift | [Apache-2.0](https://github.com/PipeRift/SaveExtension/blob/c40f37a078ce8b9b2b3a811e47729e6ca2d11d48/LICENSE). Structural reference for original C# implementation; fork order and boundaries measured independently. No verbatim C++ port. |
| Shared Kraken implementation | [TekkenMusicModManager, 8737709](https://github.com/AaryanKh/TekkenMusicModManager/blob/8737709e840c74efc935e23ac0d33cdb6aaef8f5/src/Tmm.Core/Pak/Kraken.cs), [Powzix ooz, 0503806](https://github.com/powzix/ooz/blob/05038060aa68f9187ae9923b2388ca8db40e58d1/kraken.cpp) | GPL-3.0-or-later. Used through the shared codec, no plugin-local port. See [codec notices](../../src/SaveToolkit.Codecs/README.md) and root THIRD-PARTY-NOTICES. |
| Stored-block framing research | [hl-savetools hlsave.py, e568ded](https://github.com/producedbytmsh/hl-savetools/blob/e568dedcda2611f54cc793e6d0237c749ae25094/hlsave.py) | MIT. Comparative framing lead, rechecked for the write round; not evidence that a rewritten PS5 Atomic Heart save loads. No Python writer ported; output uses the existing shared managed codec. |
| SHA-1 reference | [RFC 3174](https://www.rfc-editor.org/rfc/rfc3174) | Algorithm/vector reference; hashing uses .NET. |

Attribution lives beside the data in `ReferencePacks/atomic-heart.items.en/items.source.md`,
in the pack manifest, this table, `PluginInfo.References`, per-field descriptions
and the host's Sources panel. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
The Atomic Heart Fandom pages checked declare CC BY-NC-SA; their text/images are
not embedded. No permission decision is pending for the shipped catalog.

## Owner capture and validation checklist

- [ ] Provide console game build, region, DLC and slot labels for every capture.
  Retain a clean recovery copy. Use `PPSA10695_<event>_before.zip` and `_after.zip`.
- [ ] Capture an idle load/save pair without gameplay changes to identify churn.
- [ ] Capture one known Neuropolymer gain, then one spend; note displayed counts.
- [ ] Capture one material pickup and one craft/spend, recording exact materials.
- [ ] Capture one ammunition pickup and one fired round/reload, recording loaded
  and reserve counts separately. Include one small healing-item use.
- [ ] Move one identified stack backpack→NORA storage and back; record instance,
  amount and cell positions. Include an empty→populated slot transition.
- [ ] Buy/refund one skill; equip/upgrade one weapon; learn one blueprint, each
  in a separate pair. Record resource costs, level and effects.
- [ ] Collect one Chirper, one Testing Ground reward and one objective advance,
  separately. Provide early/open-world/late/NG+/DLC states before broad claims.
- [ ] Compare original compressed chunks with a separately reviewed independent
  Kraken decoder. Resolve unknown inner integrity and duplicated state separately;
  the stored-output literal oracle does not establish these.
- [ ] On a disposable copy, apply one small existing Amount edit. Record output
  growth and slot capacity, restore, load, inspect inventory, travel, save, restart
  and reload. Record rejection/normalization as well as success. The changed frame
  uses stored blocks; PS5 acceptance is unproven.
- [ ] Test one same-category weapon replacement and one ammunition replacement
  separately. Record raw old/new asset IDs and ItemId; inspect equip/use, modules,
  reserve/loaded ammo, ownership and capacity; save/restart/reload and re-export.
- [ ] Test a selected multi-slot Amount batch and a full eligible-slot batch
  separately, with F6 preview/skipped rows. Observe/use/spend, save/restart/reload;
  compare intended and unrelated decoded spans against an idle control. Include
  Amount zero; check normalization without assuming structural removal.
- [ ] Record title/build/region/DLC/slot, visible before/after values, growth,
  normalization, rejection and persistence on
  [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31) and
  [#12](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/12).
  Only the owner may promote maturity to Stable.

## SDK requests

None for these writes: F1/F2/F4/F6/F8 and F11 suffice. F11's managed Kraken decoder
and stored writer are used directly. Non-Kraken variants remain unsupported.
Independent compressed-codec agreement and console captures are validation
requests. Coupled skill/loadout, structural and story operations need a safe
serialization/dependency model; no framework changes are requested here.
