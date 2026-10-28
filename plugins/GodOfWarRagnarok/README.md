# God of War Ragnarök (`gow-ragnarok`)

**Maturity: ReadOnly.** Opens decrypted PS5 gameplay/history saves and the separate preferences record.
Provides metadata, an inferred hero wallet, resource ID occurrences, a partial record index, and lazy
raw word/byte views. Integrity dependencies and the complete serialization grammar remain unresolved.
No edits or actions are offered. An unsuccessful checksum search does not establish safe writing.

## Titles and file roles

Catalogued Ragnarök PS5 IDs: **PPSA08329, PPSA08330, PPSA08331, PPSA08332, PPSA08333, PPSA08334,
PPSA25043, PPSA25044**. Only **PPSA08330** has sample coverage. Other IDs require the same observed
header/layout; their catalogue identity does not establish save compatibility. No PC/PS4 header is
accepted. The source catalogue for all eight IDs is linked below.

God of War (2018) is excluded: its public PS4 editor uses a multi-slot `memory.dat` and fixed
slot-relative offsets. Shared resource IDs do not establish a shared container. No matching native
PS5 sample or title ID was established.

| File | Role | Observed coverage |
| --- | --- | --- |
| `GOWRSAVE0`–`GOWRSAVE65` | Gameplay/history slots; suffix alone does not establish chronology | 76 primary records, 833,714–3,217,731 bytes |
| `userpreferences` | Separate settings | 2 primary records, exactly 696 bytes |
| `sdimg_sce_bu_*` twins | Console backups, opened independently | All 78 pairs byte-identical |

All **156 archive records**, including duplicate paths and backups, open and serialize byte-identically.
There are 66 distinct gameplay file names. No sample files were copied into the repository.

## Established layout

Little-endian numbers, hexadecimal offsets. Structural constants have unknown semantics unless stated.
These are measured invariants of the supplied revision, not a complete game schema.

| Offset | Layout / interpretation |
| --- | --- |
| `00` | Timestamp-like u32; upper word at `04` is zero in samples. Unix-seconds meaning **Inferred**. |
| `08` | u32 `8` gameplay, `31` preferences. |
| `0C` | u32 `00513040`, supported revision word; exact meaning unknown. |
| `10`, `14` | Gameplay constants `000002B8`, `008C0400`; no size/dimension semantics asserted. |
| `18` | Variable u32, **Unknown**; not the file length. |
| `40`–`43F` | Gameplay metadata; timestamp-like prefix, `35` at `48`, same revision at `4C`. |
| `50`–`18F` | NUL-terminated UTF-8 location/quest text, **Verified** from readable sample content. |
| `190` | u32 play seconds, **Inferred** from the PC reader and coherent PS5 progression. |
| `1BC` | u32 save counter, **Inferred** from the PC reader and snapshot progression. |
| `440` | u16 `61C0`, observed directory marker; meaning unknown. |
| `442` | u32 ID count: 798 in all 76 primary gameplay records. |
| `446` | That many strictly increasing, unique u64 IDs; no name-hash algorithm established. |
| after IDs | Same number of u32 words, only `0`/`2` observed; correspondence to IDs **Inferred**. |
| after words | u32 count then u64 IDs: 10 in every record, each also in the sorted array. |
| variable | Exactly one u64 `00004895C643225F`, the PC reader's hero-wallet ID. |
| wallet `+08` | u32 row count, 0–211 in this corpus. |
| wallet `+0C`, `+10` | Unknown u32s, not treated as trusted lengths/counts. |
| wallet `+14` | Counted 16-byte rows: u64 ID, u32 inferred quantity, u32 unknown metadata. |
| EOF `-04` | Gameplay `FFFFFFFF`, observed sentinel, **not a checksum**. |

The partial index reports exact spans for these arrays. It does not invent parent/child links or
record lengths across unparsed gaps. The word preceding the wallet may be a size, but its coverage
is unproven and it is not used for traversal.

## Meaning and confidence

The embedded `Resources/ResourceNames.json` contains 18 Ragnarök resource names from exact eight-byte
search IDs in the community codes: Hacksilver, Rawhide, Chaos/Gale Flames, six dews and eight Valhalla
items. XP is an additional **inferred Ragnarök role** transferred from the 2018 editor's `EconomyXP`
ID. No network or filesystem access is performed by the plugin.

Ragnarök name-to-ID correspondences are substantiated by the source and exact sample matches where
present. Wallet ownership, quantities, time, counter and timestamp meaning stay **Inferred** because
they have not been compared with the PS5 UI. Unmapped IDs, words outside wallet rows, raw settings
and raw word interpretations are **Unknown**. All occurrences remain separate; they are never summed.
An absent ID does not imply zero inventory. Missing Valhalla IDs do not establish Valhalla support.

The PS4 difficulty offset `2F442` is not portable: 8/76 primary gameplay records contain values outside
its documented enum. No named difficulty field is published.

Preferences receive exact-length/header validation and byte pages covering all 696 bytes. Individual
audio, controller, language and accessibility meanings are not guessed. The two primary records
differ at 29 positions; settings ranges and integrity remain unknown.

## Integrity, compression and preservation

Readable metadata and matching resource records establish accessible plaintext structures, not the
absence of embedded compression/encryption. Reconnaissance found no recognized framed codec or
successful initial zlib candidate. No decryption key or compression transform is applied.

Seed-zero CRC32, Adler32, xxHash32/64 and MurmurHash3 candidates were checked against header/footer
words and selected directory/wallet spans: no match. Coverage/seed searches were finite. See the
[research log](RESEARCH.md) for methods and negative results.

The parser rejects unsupported headers, known malformed metadata, unsorted IDs, missing secondary
IDs, invalid array counts, missing/duplicate wallet markers and missing observed EOF sentinels.
**It cannot detect arbitrary corruption in opaque data or authenticate a save.**

`CanEdit` is false. Every field is read-only, edits are rejected atomically by `BoundSaveDocument`,
and unknown actions are rejected. `Serialize()` re-parses the original buffer and returns an isolated
byte-identical copy for the same path. Unknown bytes/padding/tails are preserved. Blob pages also
return copies. The normal UI disables export for read-only documents.

## Bounds and tests

Open reads one file, never all 66 slots or siblings. Files are capped at 32 MiB, array counts at
65,536, resource occurrences at 4,096. No large views are built during Open. Record tables use lazy
256-row pages. Raw words use 1 KiB pages under 64 KiB lazy groups, with final unaligned bytes
shown individually. Preferences use 256-byte pages. Hex reads are capped at 64 KiB.

```powershell
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
dotnet test --project plugins/GodOfWarRagnarok/SaveToolkit.Plugins.GodOfWarRagnarok.Tests
dotnet test --project tests/SaveToolkit.Core.Tests
```

**37 plugin tests passed; 43 Core tests passed; API build passed with zero warnings/errors.** Coverage:
read-only conformance, all 156 records, known values, all backup pairs, shrinking generations,
malformed counts/IDs/text, every synthetic-file truncation, bounded views, edit rejection and copies.
Each sampled Open is asserted under two seconds. Without samples: **29 pass, 8 skip**, zero failures.
The conformance edit test is inactive for ReadOnly maturity; no edit round-trip or console test is claimed.

Known generation-zero `GOWRSAVE0`: 3,215,266 bytes, play-time word 138,041, counter 1,797,
Hacksilver wallet word 288,638 at `1BC90`, Rawhide 52, Chaos Flame 0, XP-ID word 84,725.
Its next archive occurrence is 834,621 bytes, play-time word 568, counter 1 and an empty hero wallet.
These are byte-level regression facts, not proof of player-facing meanings.

## Open work

- Recover full nested serialization/length/type rules, including regions surrounding wallets.
- Determine all integrity dependencies, coverage and seeds before introducing a setter.
- Validate quantities, XP role and time units against PS5 UI or controlled saves.
- Locate difficulty through structure; map preferences with one-setting-at-a-time captures.
- Obtain other regional/revision and Valhalla samples; test in-game before any Stable designation.

## Sources

Research date: 2026-10-28. References were read as text; no downloaded editor/trainer was executed.
No third-party implementation was copied. The embedded dictionary is a small factual ID/name list;
the community repository does not establish a general code reuse licence.

| Source | Used for |
| --- | --- |
| [Local reconnaissance](../../docs/formats/PPSA08330.md), [method](../../docs/formats/README.md) | Corpus, headers, compression-search limits. |
| [SaveWizard codes, inspected revision](https://github.com/sterben-Dev10/SaveWizard-Resources/blob/4c32596efde54af7bec390b298265f09c929d94d/SaveWizard-Codes/GOW%20Ragnarok%20%5BSaveWizard%5D.txt) | Resource IDs, chest anchor, PS4 difficulty lead; whole cheats not copied. |
| [Apollo SaveWizard reference](https://bucanero.github.io/apollo-lib/savewizard.html) | Types 8 search, 2 pointer-relative u32 write, 9 pointer and A literal write. |
| [Apollo CUSA34384 patch](https://github.com/bucanero/apollo-patches/blob/main/PS4/CUSA34384.savepatch) | Independent code/difficulty context; no integrity algorithm supplied. |
| [Continue? PC reader, inspected revision](https://github.com/Alexsander720/continue/blob/51e19b2a292c60f9acdf3c13c561809657104ea2/plugin/py_modules/ddsaves/god_of_war_ragnarok.py) | Wallet ID, counted rows, metadata/time/counter. PC revision is `00660404`; transferred ideas checked on all PS5 samples. |
| [2018 layout data](https://github.com/xcier/GoW-2018-PS4-editor/blob/main/app/core/gow2018_data.py), [item data](https://github.com/xcier/GoW-2018-PS4-editor/blob/main/app/resources/database/gow2018_items.json) | XP/EconomyXP ID and reason to exclude the 2018 container. |
| [Codes Prince console editor](https://codesprince.com/2022/12/06/ps4-ps5-save-editor-for-gow-ragnarok/), [PC editor](https://codesprince.com/2024/09/24/pc-editor-for-gow-ragnarok/) | Separate platform/version support; no public parser/integrity algorithm obtained. |
| [GOWTool](https://github.com/kainotoa/GOWTool), [smpack](https://github.com/iArtorias/smpack) | Asset/WAD leads; no proof that asset hashing or WTOC/LZ4 applies to saves. |
| [Regional catalogue](https://sharpemu.app/game/PPSA08329/), [PPSA08329](https://prosperopatches.com/PPSA08329), [PPSA08330](https://prosperopatches.com/PPSA08330), [PPSA08331](https://prosperopatches.com/PPSA08331), [PPSA08332](https://prosperopatches.com/PPSA08332), [PPSA08334](https://prosperopatches.com/PPSA08334) | Title identity/IDs; not save compatibility. |

## SDK requests

None blocking. Existing lazy sections, tables, confidence and hex pages support this explorer.
Full grammar/integrity research is the blocker to editing.
