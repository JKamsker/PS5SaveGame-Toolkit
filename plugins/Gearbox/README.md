# Gearbox (`gearbox`)

**Experimental** editing for decrypted PS5 **Borderlands 3 (PPSA01463)** and **Tiny Tina's Wonderlands
(PPSA01493)**. **PPSA01494 / Borderlands 4 is excluded.** Console-layer encryption must already have
been removed. Edited files have not been loaded on a console.

## Features and confidence

| Area | Support | Evidence / limits |
| --- | --- | --- |
| Character | Editable name; class asset paths; Wonderlands primary/secondary branches | Schema names and sample strings; class display names are not guessed |
| Level / XP | Editable; BL3 levels 1–72, Wonderlands 1–40; existing recorded-level stat updated | Upstream exact XP tables; all sample XP/stat values agree |
| Skills | Editable unspent points and tree grade; Wonderlands upward levelling awards points | Schema and upstream behavior: one per level, two at 20, three at 40 |
| Currency | Money, eridium / moon orbs | Documented hashes `618814354` / `3679636065` match all character samples |
| Keys | Golden / skeleton keys; create absent category only on a nonzero edit | Published path/hash algorithm; `2420693941`; all supplied profiles omit this record |
| Ammo | Editable finite resource-pool amounts | Schema floats and full resource paths; game capacity may clamp them |
| SDUs | Read purchased levels and full asset paths | Schema; no upgrade/capacity editing |
| World | Read missions/statuses, playthrough indices/completion, Mayhem/Chaos and unlocked Chaos | Separate game schemas; no mission/difficulty/unlock editing |
| Inventory | Read item counts, indices, serial lengths, flags, pickup order, equipped-slot mappings | Opaque item serials are never edited; no invented item names |
| Profile | Read bank/lost-loot counts; BL3 guardian rank/XP; Wonderlands myth XP when present | Schema; no rank/reward editing |
| Raw | Lazy schema-labelled tree; unknown tags and bytes shown as opaque data | Unknown meanings remain unknown; account-like metadata is hidden |

Curated field meanings use `Confidence.Verified` because they are named in cited schemas/upstream
tools, with sample evidence below. This does **not** mean edits have been verified in-game. Limits
are editor policies, not universal game limits: money/eridium/keys ≤2,000,000,000, moon orbs ≤16,000,
ammo ≤100,000. Existing out-of-range amounts remain read-only. Name length uses the stored limit
when reasonable, otherwise an inferred fallback of 32 UTF-16 units. Invalid Unicode/control
characters are rejected.

Level/XP edits preserve mission unlocks and milestone challenges. Levelling down preserves spent
skills and does not remove unspent points. Repeated down/up levelling in Wonderlands can therefore
award extra points; review the explicit points field. BL3's level-one tree is enabled when raised
above level one. Missing level statistics/ability messages are not fabricated. Warnings describe
these limits in the UI.

## File roles and layout

`profile` is the profile; positive numeric filenames such as `1` are character slots. Probe requires
the supported title ID, file role and `GVAS` magic. Open validates branch/class/version as well.
Only the opened file is returned; console backups are separate documents, not silently rewritten.

GVAS framing, little-endian:

1. `GVAS`, int32 save version **2**, int32 package version.
2. uint16 engine major/minor/patch (**4.20.3**), int32 build number.
3. FString build branch (`OAK-*` / `DAFFODIL-*`).
4. int32 custom-version format **3**, int32 count, count × (16-byte GUID + int32 version).
5. FString save class: BL3 `OakSaveGame` / `BP_DefaultOakProfile_C`; Wonderlands
   `BPSaveGame_Default_C` / `OakProfile`.
6. int32 payload length, obfuscated protobuf bytes, optional opaque trailing bytes.

FStrings support positive UTF-8 byte lengths or negative UTF-16 code-unit lengths, including checked
terminators. All header/custom-version metadata is retained verbatim; only the payload length is
patched on size changes. Opaque tails are retained, although supplied samples have none.

## BogoCrypt, protobuf and integrity

The published **PS4** character/profile mask pairs decode **both** PS5 games. Separate game schemas
are essential; separate Wonderlands PS4 masks were not needed here. PC mask trials failed protobuf
parsing on all twelve primary records. PS4 trials parse all twelve, plus all twelve backup twins.
This proves the supplied builds, not every PS5 release.

Plaintext byte `i` is ciphertext XOR the role's XOR mask at `i mod 32`, XOR either the prefix mask
at `i` for the first 32 bytes or ciphertext at `i−32`. Encoding uses already produced ciphertext
as feedback. This is rolling XOR obfuscation, with no compression/authentication. No additional
wrapper checksum is present in the cited format or observed samples. **Well-formed scalar/unknown
field corruption can go undetected.** Item serials have independent encoding/checksums and remain
untouched.

Google.Protobuf managed coded streams parse/rewrite wire records without reflection, generated
classes, protoc or Grpc.Tools. Embedded TSV resources contain factual names/numbers/types/cardinality
extracted from each game's three schemas and exact XP thresholds: BL3 80 messages / 529 fields;
Wonderlands 73 / 486. The schemas stay separate: game-state field 53 versus 19, bank field 137 versus 212.

Every field retains its complete original wire record. Editing rebuilds only changed values and
enclosing messages/lengths. Original order, unknown records, noncanonical varints and opaque bytes
survive. No-op serialization is byte-identical across the entire sample corpus. Unknown groups and
packed primitive data are preserved; known packed values are wire-validated. Repeated fragments of
a known singular message are rejected because protobuf merge semantics need additional work; none
occur in the samples.

Edits run on a cloned message tree; failure discards it, retaining original wire encodings and the
provenance of newly created records. Derived changes are reported for framework undo/redo, including
exact XP, skills/statistics and removal of newly added key records. Serialize parses its own complete
output wrapper, decodes/validates the protobuf and compares it with the intended wire message.

Bounds: 32 MiB input, 1,024 custom versions, 16 KiB header strings, 40 known-message nesting levels,
250,000 fields. Tables/raw pages have ≤100 records and large areas are lazy. Opening every sample is
tested below two seconds. Runtime I/O is limited to embedded resources; there is no filesystem/network
I/O or shared mutable document state.

## Sample evidence

Both titles contain three character/profile generations with identical primary/backup twins: 24
records total. Archive occurrence order does not establish chronology. Source level stats agree with
the following independently decoded values:

| Title / generation | XP | Level | Money | Eridium / moon orbs |
| --- | ---: | ---: | ---: | ---: |
| BL3 / 0 | 9,520,932 | 72 | 25,526,805 | 4,897 |
| BL3 / 1 | 9,520,932 | 72 | 13,296,047 | 2,592 |
| BL3 / 2 | 6,494 | 5 | 5,947 | 3 |
| Wonderlands / 0 | 70,754 | 12 | 36,748 | 0 |
| Wonderlands / 1 | 75,695 | 12 | 23,304 | 0 |
| Wonderlands / 2 | 70,565 | 12 | 30,754 | 0 |

Wonderlands moon-orb records omit the default-zero quantity. All profiles omit key-category records;
key creation is supported by the schema/hash and edit/reopen tests, with console behavior untested.
Samples/decoded payloads were inspected only in external scratch storage and are never committed.

## Validation

```powershell
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
dotnet test --project plugins/Gearbox/SaveToolkit.Plugins.Gearbox.Tests
dotnet test --project tests/SaveToolkit.Core.Tests
```

Validated: API build, zero warnings/errors; **47 Gearbox tests passed**; **42 framework tests passed**.
With samples unavailable: **24 passed, 23 skipped, zero failures**.

Tests include conformance with meaningful currency/key edits, every generation/twin, independent
feedback-window vectors, known values, money/level/key reopen, Wonderlands extra points, framework
undo/redo byte restoration, atomic rejection, preservation of every real-sample unknown record,
noncanonical encodings/nested lengths, UTF-16 GVAS strings, header/tail preservation, malformed
payload/framing rejection and invalid names. Sample-dependent tests use `SampleSaves` to skip.

## Unknowns and open work

- No edited file has been loaded on PS5. Only a human may promote Experimental to Stable.
- Some BL3 PS5 profile tags postdate the reference schema and remain opaque.
- Controlled game tests are needed for clamping, milestone challenges, low-level unlock dependencies
  and missing progression records. Existing mission/unlock data is preserved.
- Item/rank/reward/difficulty/mission editing and platform conversion are not implemented.
- Other regions/builds need samples. PPSA01494 is a separate BL4 format and never claimed.

## Sources and licences

Research date: 2026-10-28. Downloaded source was read as untrusted text, never executed. Implementation
code is independently written; reference resources contain schema/numeric facts. Notices are retained
under [Notices](Notices/).

- [apocalyptech/bl3-cli-saveedit, d459b28](https://github.com/apocalyptech/bl3-cli-saveedit/tree/d459b28a7e88f1a5ae0f16118272d8a5383b8b04):
  `bl3save.py`, `bl3profile.py`, `__init__.py`, `protobufs/Oak{Save,Profile,Shared}.proto`: framing,
  schema facts, currency hashes, XP thresholds, level/key semantics. Also [save documentation](https://github.com/apocalyptech/bl3-cli-saveedit/blob/d459b28a7e88f1a5ae0f16118272d8a5383b8b04/README-saves.md).
  Contrary to the task's GPLv3 note, this revision's COPYING/source headers use **zlib** (CJ Kucera);
  retained as `Notices/Bl3-COPYING.txt`. Protobuf research credits Rick/Gibbed.
- [apocalyptech/ttwl-cli-saveedit, 1fde90d](https://github.com/apocalyptech/ttwl-cli-saveedit/tree/1fde90dc43e7f7ee261413d85c366e0a7236dd9b):
  corrected repository name (`wl-cli-saveedit` returned 404); Wonderlands schemas/XP, dual classes,
  moon orbs/skeleton keys, Chaos mapping and skill-point exceptions. **zlib**, retained as
  `Notices/Wonderlands-COPYING.txt`.
- [FromDarkHell/BL3SaveEditor, c044e6d](https://github.com/FromDarkHell/BL3SaveEditor/tree/c044e6d6f1fd78b259f2147119fbe2cdbbc7a3ee/BL3Tools/Decryption):
  `SaveBogoCrypt.cs` / `ProfileBogoCrypt.cs`: PS4 mask facts/feedback behavior; `GameData/Experience.cs`
  cross-checks BL3 levels. Repository **GPL-3.0**, but these crypt files carry Rick/Gibbed's **zlib**
  notice, retained as `Notices/Gibbed-BogoCrypt.txt`. No GPL application/UI code was copied.
- [xcier/TTWLSaveEditor, 48984d8](https://github.com/xcier/TTWLSaveEditor/tree/48984d81a9a9f11951c7d3c4bca48f8cbc0abc11/BL3Tools):
  Wonderlands schema/XP comparison and matching PS4 masks; repository **GPL-3.0**, crypt file headers
  **zlib**. Reference only; no application code copied.
- [cfi2017/bl3-save, fdfc80b](https://github.com/cfi2017/bl3-save/tree/fdfc80b1781ad386e2182eb6ca4dad07a9be94c8):
  **MIT** web/CLI editor, reviewed for ecosystem/support scope, no implementation code used.
- [ZakisM/bl3_save_edit](https://github.com/ZakisM/bl3_save_edit): **GPL-3.0**; decrypted PS4 support
  documentation and attribution to HackerSmacker's PS4 mask research. Sample tests, not that support
  claim, establish PS5 compatibility here.
- [Protocol Buffers wire specification](https://protobuf.dev/programming-guides/encoding/): tags,
  wire types, packed fields and unknown-field behavior.
- [Google.Protobuf 3.36.2](https://www.nuget.org/packages/Google.Protobuf/3.36.2): managed C# runtime,
  **BSD-3-Clause** package dependency; no build-time compiler dependency.

## SDK requests

None. `BoundSaveDocument` is extended locally with clone-based transactions and complete derived-value
reporting for coupled progression edits.
