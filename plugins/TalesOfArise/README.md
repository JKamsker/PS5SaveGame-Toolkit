# Tales of Arise (`tales-of-arise`)

Supports **PPSA02745 (EU), base-game gameplay saves** with the measured 760,856-byte,
30-section PS5 schema. Maturity: **Experimental**. Encryption, integrity and editing
conformance pass against the supplied saves; no edited output has been loaded in-game.
Other regions, PC/PS4 input, Beyond the Dawn and different section versions are not claimed.

## What works

- Opens `SaveData_NNN.sav` by title, filename, length, magic and PS5 mode. Tests cover
  slots 000–002 and all three console backup twins.
- Verifies SHA1 before decryption; follows and validates the shuffled section chain;
  rejects bad alignment, bounds, overlap, cycles, counts, IDs, sizes and versions.
- Curated gald, six character levels/current HP/SP, existing recognised consumable
  quantities, and read-only play time, experience and base HP. All gameplay meanings
  carry **Inferred** confidence for the PS5 mapping.
- Party stats and character table, lazy inventory table, lazy read-only explorer of
  all 30 sections / 138,832 primitive uint32 words, and paged plaintext/outer hex views.
  Primitive pages contain at most 256 rows. Opening and building the initial view
  stays below two seconds in the sample tests.
- Serializes fixed-size patches, re-encrypts in chain order, recomputes SHA1, then
  decrypts and validates the output and compares every plaintext byte after the header.
  Even no-op serialization runs the actual cipher and is byte-identical on all six files.
- Preserves the entire outer region `0..0x32057`, including screenshot/header bytes.
  Unknown fields, record flags, IDs, padding and unvisited bytes remain unchanged in plaintext.

`SystemSaveData.sav` is a separate **4,096-byte opaque system blob**. It is not claimed,
decrypted or rewritten; the framework's raw viewer remains available. Each document
only returns its own file, without silently changing the console backup twin.

## Container layout and reconciled offsets

All integers are little-endian. File offsets below are absolute.

| Offset | Length | Meaning / validation |
| --- | ---: | --- |
| `0` | `0x32058` | Opaque outer header and screenshot, preserved verbatim |
| `0x32058` | 4 | Magic `30 16 05 10` |
| `0x3205C` | 4 | Gameplay block length `0x87BC0`, measured **from magic to EOF** |
| `0x32060` | 20 | SHA1 of the final ciphertext at `0x32078..EOF` |
| `0x32074` | 1 | Platform/XOR mode `03` (PS5) |
| `0x32075` | 1 | Regular algorithm selector `01`; alternatives rejected |
| `0x32076` | 2 | Preserved alignment bytes |
| `0x32078` | `0x40` | First encrypted block: 16 relative uint32 offsets |

EOF is `0xB9C18`. Actual ciphertext length is `0x87BA0`, not the length word.
The upstream prose offset `0x32068` for the mode overlaps its own SHA1 range and is
incorrect for these saves. The converter's computed constants correctly place it at
`0x32074`. Another prose arithmetic typo, `0x32078 + 0x18 = 0x32000`, should be
`0x32090`. The PS5 directory pointer is at byte `0x18` within the first decrypted
block, i.e. file offset `0x32090`. All pointer targets are relative to `0x32058`.

That pointer locates a 64-byte directory with marker 100 at `+0`, count 30 at `+4`,
and first section pointer at `+8`. Each section has a separately encrypted 16-byte
header: version at `+0`, identifier at `+4`, padded body size at `+8`, next header
pointer at `+0xC`; its encrypted body follows immediately. The final next pointer is zero.
The section names/order follow upstream research; embedded `Resources/Sections.csv`
records the IDs, versions and sizes confirmed across this corpus. IDs stay constant
while bodies change, so they are treated as schema identifiers, **not body checksums**.
Their name-hash algorithm is unknown.

Physical directory offsets relative to the block are `0x83F50`, `0x5BD70`, and
`0x7E770` for slots 000, 001, and 002 respectively. No gameplay field uses a fixed
absolute file address. Unsupported schema changes fail closed instead of guessing.

## Encryption and integrity

An independently written C# implementation uses .NET AES-256-ECB, no padding,
and the public PS5 64-byte key material and XOR table documented by save-converters.
Only the first 32 key bytes configure AES. The full 64-byte key evolves as follows:

1. Process the first 64-byte offset block, then the pointed-to 64-byte directory,
   then each 16-byte section header and its complete body, in linked order.
2. Within each such region, process chunks of up to 64 bytes. Before AES encryption
   XOR plaintext with feedback, swapping adjacent feedback bytes (`i ^ 1`) for PS5.
   Decryption applies AES first, then that same XOR.
3. Replace the feedback prefix with the chunk's ciphertext, preserving unused feedback
   bytes for a short chunk. Feedback persists across headers and bodies.
4. After each complete region, SHA1 its **plaintext** and XOR the repeating 20-byte
   digest into all 64 key bytes. A zero XOR result retains the old nonzero byte;
   if the old byte was zero too, use `0x20`.
5. After all encryption, SHA1 the **entire physical ciphertext**, from `0x32078` to
   EOF, in file order. Write its raw 20-byte digest at `0x32060`.

Thus the internal key schedule hashes plaintext in traversal order, whereas the
stored integrity digest hashes ciphertext in physical order. Both distinctions are
verified by exact re-encryption of every supplied gameplay file. The stored digest
matches ciphertext on 6/6 records and does not match the corresponding plaintext.
No compression or additional integrity field is identified in this schema.
SHA1 is retained solely for compatibility with the game's format.

## Curated fields: evidence and limits

Offsets here are relative to each **section body**, after its 16-byte header.
The PS4 patch codes locate the section ID at header `+4`, so their relative offsets
must be reduced by `0xC` to address the body. This is separate from the encryption
offset discrepancy above.

| Section | Offset | Field | Access / evidence |
| --- | --- | --- | --- |
| PartyProfile | `0x18` | Gald, uint32 | Editable 0–999,999,999; source ID +`0x24`; zero in all supplied slots |
| PartyProfile | `0xAF8` | Play seconds, uint32 | Read-only; equals outer header `+8` in every slot |
| PartyProfile | `0x2000` | Play ticks, uint32 | Read-only; `/60` floors to seconds; duplicate at `0x2008` preserved |
| ArisePCStatus_000..005 | `0x04` | Level, uint32 | Editable 1–100; PS4 source ID +`0x10`, all six are level 5 |
| same | `0x08` | Current HP, uint32 | Editable 0–99,999; PC table layout minus `0x40` fits PS5; inferred |
| same | `0x10` | Experience, uint32 | Read-only; PS4 source ID +`0x1C` |
| same | `0x30` | SP, uint32 | Editable 0–999,999; PS4 source ID +`0x3C`; consistent PC layout |
| same | `0x50` | Base HP, uint32 | Read-only; PS4 source ID +`0x5C` |
| SaveData_ItemManager | `i * 16` | Stack quantity byte | Editable only for existing IDs 1–13, 0–15 |
| same | `i * 16 + 1` | Flags byte | Raw, preserved |
| same | `i * 16 + 2` | Item ID, uint16 | Read-only; 934-record scan from cited PS4 patch |

Character names and the 13 consumable IDs are embedded reference tables; unknown
item IDs retain numeric labels. Empty record IDs are omitted; there is no item
insertion, flag editing, accessory editing or bulk grant action. Existing values
outside the supported editing guardrails remain visible but read-only. IDs 6/7 of the
character sections are unmapped and read-only, not assumed to be active party members.

**Verified** means the byte-level observations, cipher, integrity coverage and
round-trip preservation described above. Curated PS5 **meanings remain Inferred**:
the cited PS4/PC offsets and sample consistency support them, but no controlled
in-game PS5 edit has confirmed them. Explorer words have **Unknown** semantics.
Numeric maxima are editing guardrails, not claims of validated PS5 game limits.

Level edits do not synthesize experience, recalculate stats, or change the outer
load-screen preview. Current HP need not equal base HP once equipment/bonuses are
involved. Play time is read-only because it has duplicate counters and an outer
copy which this plugin deliberately preserves. Bulk inventory codes in the source
have reports of broken accessory/key-item acquisition; this plugin does not implement them.

### Measured samples

Primary and backup are identical for each slot; these are three different saves,
not six independent play sessions. No repeated ZIP path generations exist here.

| Slot | Seconds / ticks | Experience (six main characters) | Inventory records |
| --- | --- | --- | ---: |
| 000 | 1,890 / 113,431 | 0 | 24 |
| 001 | 2,770 / 166,253 | 33 | 24 |
| 002 | 3,166 / 189,968 | 33 | 25 |

Base/current HP are 321, 282, 255, 291, 339, 312 for character indices 0–5 in all
three slots. Slot 002 adds an Apple Gel stack of one at inventory record 24.

## Validation

```powershell
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
dotnet test --project plugins/TalesOfArise/SaveToolkit.Plugins.TalesOfArise.Tests
dotnet test --project tests/SaveToolkit.Core.Tests
```

Plugin suite: **39 passed**, including the six inherited conformance tests.
Core suite: **43 passed**. API build: **zero warnings/errors**.
Tests cover independent cipher vectors with short feedback updates and key evolution,
SHA1 `abc`, independently calculated whole-plaintext SHA256 digests, all six real
gameplay records, known values, no-op, edits/reopen/undo, exact preservation of every
other plaintext byte and outer bytes, atomic rejection, corruption, valid-SHA1
malformed encrypted structures, unsupported modes/schema, and every explorer page.
Sample tests use `SampleSaves` and skip when the archive is unavailable.
An explicit missing-archive run passed 9 independent tests and skipped 30 sample tests.
No sample data or downloaded reference implementation is embedded in this plugin.

## Open questions

- In-game loading and gameplay behavior after PS5 edits; this blocks Stable maturity.
- CP, grade/score, remaining field meanings, equipment, accessories and flags.
- Meaning of duplicated play counters, additional party members, section ID hashing,
  and unknown outer metadata/integrity dependencies. The outer preview is not refreshed.
- Nonzero gald evidence and controlled PS5 inventory/HP/SP changes; supplied saves
  are early-game and do not validate late-game/DLC schemas or upper ranges.
- Alternate platform/algorithm selectors, other save versions and Beyond the Dawn.

## Sources and attribution

Research performed 2026-10-28. Downloaded implementations were read as untrusted
text; none was executed. The algorithm and public constants were independently
implemented in C#; no substantial upstream code was copied. Runtime dependencies
are the SDK and .NET cryptography only, with no added NuGet packages.

- [ItsAllAboutTheCode/save-converters notes](https://github.com/ItsAllAboutTheCode/save-converters/blob/main/save_convert/tales_of/arise/save_NOTES.md):
  PS5 AES/XOR material, section names/order, pointer base, platform differences.
- [Converter implementation, inspected revision 6632390](https://github.com/ItsAllAboutTheCode/save-converters/blob/663239063e7c1cc8550f4248f772ee9dd47ed5b0/save_convert/tales_of/arise/tales_of_arise_save_converter.py):
  authoritative computed offsets, feedback byte swapping/partial updates, plaintext
  key evolution and final ciphertext SHA1. Upstream is
  [GPL-3.0](https://github.com/ItsAllAboutTheCode/save-converters/blob/main/LICENSE);
  attribution is to the published research, not a claim that copied GPL code is relicensed.
- [PS4 research thread](https://web.save-editor.com/bbs/patchcode/ps4/bbs.cgi?list=pickup&num=30070),
  especially [ロード's 2022-04-03 patch/reference attachment](https://web.save-editor.com/bbs/patchcode/ps4/dl.cgi?file=31135.txt):
  gald and relative character offsets, 934 inventory records, item acquisition warnings.
- [PS4 discussion](https://web.save-editor.com/bbs/patchcode/ps4/bbs.cgi?list=pickup&num=30162),
  posts 30214, 30222, 30223, 30245 and 30246: character IDs, quantity/flags/item ID
  layout and Japanese consumable names (English labels supplied here).
- [Sora3100 Arise.CT](https://github.com/Sora3100/Tales_of_Cheat_Tables/blob/master/Arise.CT):
  character index/name ordering and PC level/HP/experience/SP/base-HP layout.
  PC runtime addresses are not used as save-file addresses.
- [Gald Editor author page](https://www.nexusmods.com/talesofarise/mods/407):
  consulted for existing tools; this is a PC memory table, not PS5 save-layout evidence.
- [Local reconnaissance](../../docs/formats/PPSA02745.md) and
  [method](../../docs/formats/README.md): initial archive measurements and offset discrepancy.

## SDK requests

None. The existing bindings, lazy sections, tables and paged blobs support this plugin.
