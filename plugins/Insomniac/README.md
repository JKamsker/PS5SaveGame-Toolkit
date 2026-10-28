# Ratchet & Clank: Rift Apart (`insomniac`)

Version **0.2.0**, **ReadOnly**, for decrypted **PPSA01474** saves. Round two adds named
gameplay fields to the lossless DAT1 explorer. Writing remains disabled: no public PS5
patch payload was found to establish plain writes, and the checksum investigation has
not established complete integrity coverage. No edited save has been tested in-game.

## Files and views

* `i29-s.save`: gameplay. A **Player** tab shows career/spent bolts, calculated available
  bolts, career XP, aggregate weapon XP, career raritanium and spending, play time,
  difficulty code, campaign/challenge flags, gold bolt count, RYNO-plan mask and inferred
  spybot count, seven gold-bolt options, and per-location counters.
* `i29-userprefs.save`: preferences; retained as the original read-only DAT1 explorer.
* Overview, section descriptors, lazy section hex/ASCII explorers and paged whole-file
  hex remain available. Campaign, gold bolt and speedrun sections additionally expose
  bounded, named root DDL fields. Nested objects remain opaque.
* The corpus has 36 primary gameplay records and two primary preference records, plus
  38 byte-identical backup twins. Repeated zip paths are distinct generations, not
  replacements. All 76 records retain byte-identical no-op output.

The plugin depends only on the SDK and performs no filesystem/network I/O. Research
scripts are separate, explicitly invoked offline tools; they are not loaded by the app.

## DAT1 and DDL layout

The DAT1 envelope is unchanged from round one:

| Offset | Encoding | Meaning |
| ---: | --- | --- |
| `0x00` | u32 LE | `0x44415431` (`1TAD`) |
| `0x04` | u32 LE | Header word `8`; semantic name unknown |
| `0x08` | u32 LE | Total file size, equal to EOF |
| `0x0C` | u16 LE | Descriptor count: 19 gameplay / 1 preferences in this corpus |
| `0x0E` | u16 LE | Auxiliary-record count; zero in samples |
| `0x10` | repeated u32 triples | Section tag, absolute file offset, byte size |
| after descriptors | auxiliary records, strings, padding | Preserved verbatim |

Framing validation rejects size/count overflow, invalid magic/version, truncated tables,
out-of-range/overlapping sections. Descriptor order and all original bytes are retained.

Rivet's save reader identifies the campaign section as `Checkpoint`; the engine name CRC
of that string is **`0xD436A987`**. Gold-bolt data is **`0x77F00BE8`**, and the speedrun
timer is **`0xA787E610`**. The runtime parses these three roots only. See the pinned
[save reader](https://github.com/neptuwunium/rivet/blob/a631c96ed6455f9873a646825d37883f99defa6e/Rivet/Data/SaveData.cs)
for section identification and the still-unresolved `INVT` block.

Each DDL object starts with four LE u32 words: zero, `0x03150044`, field count, body
size. Its body starts with `(name hash, metadata)` pairs, then absolute name-string
offsets, then consecutive field values. Metadata's high byte selects the stored type;
bits 4–23 encode the count and bits 0–3 the array kind. Names must point into the DAT1
string region and match the engine CRC: reflected polynomial **`0xEDB88320`**, seed
**`0xEDB88320`**, no final inversion. This is a **name identifier**, not a save-body
checksum. These structures are documented by Rivet's
[field header](https://github.com/neptuwunium/rivet/blob/a631c96ed6455f9873a646825d37883f99defa6e/Rivet.DDL/Models/DDLFieldHeader.cs)
and [DAT1 format notes](https://github.com/neptuwunium/rivet/blob/a631c96ed6455f9873a646825d37883f99defa6e/docs/format/dat1.txt).

Sample-confirmed details matter more than the PC class's declared widths:

* Integers narrow to u8/u16/u32/u64 as needed, and fields reorder between saves.
  Career raritanium is u8 in early saves and float32 in late saves. `Playtime` is float32
  here although the published PC schema declares double. There are no fixed absolute
  gameplay offsets.
* Strings contain u32 byte length, two opaque hash words (u32/u64), UTF-8 text and a NUL.
  Their text-plus-NUL storage rounds up to four bytes **relative to the string**, even
  when its header is unaligned. For example the late autosave's `Version` begins at
  `0xB1BE` and the next field begins at `0xB1D2`, not `0xB1D0`.
* Empty `CollectedBoltHashes` is type 19, count zero, one stored zero byte. It means
  **zero** gold bolts, not a one-element null array.
* Values and child-object bounds, UTF-8/NULs, booleans, finite numbers, duplicate field
  names and root padding are checked. Root views are bounded to 1,024 fields and 4,096
  total values. Nested object contents and opaque sections are not semantically validated.

No extra compression or encryption is needed to read these decrypted containers. Most
payloads are plaintext DDL or custom binary structures; other sections remain opaque,
so this does not establish that every section is uncompressed or checksum-free.

## Meaning and confidence

**Verified** means the stored name/hash matches and is corroborated by the published
[campaign schema](https://github.com/neptuwunium/rivet/blob/a631c96ed6455f9873a646825d37883f99defa6e/Rivet.DDL.RCRA/Types/GameCampaignSave.cs)
or [gold-bolt schema](https://github.com/neptuwunium/rivet/blob/a631c96ed6455f9873a646825d37883f99defa6e/Rivet.DDL.RCRA/Types/GoldBoltSaveData.cs).
It does not imply an in-game write test.

| Fields | Status / limitations |
| --- | --- |
| `CareerBolts`, `SpentBolts`, career XP / weapon XP | Stored counters verified; weapon XP is aggregate |
| `CareerRaritanium`, `CareerRaritaniumSpend` | Stored counters verified; available raritanium unknown. Late spending 584 exceeds career value 531.199646; both are shown with warnings |
| `Playtime` | Stored name/value verified; seconds inferred from scale and progression |
| `Difficulty` | Stored code verified; enum labels unknown (samples contain 0 and 1) |
| `CurrentLevel` | Location code, **not player level**; planet-name mapping unknown |
| Challenge flags and seven gold-bolt option booleans | Stored names/types verified; not editable |
| Gold bolts | Verified entry count of `CollectedBoltHashes`: 0 early, 7 late; matches the sum of per-location gold counters in every primary record |
| `RYNOPlansCollected` | Verified stored bit mask, not a count: e.g. 553 = `0x229` |
| Available bolts | **Inferred**: career minus spent; no in-game display comparison |
| Spybot count | **Inferred**: population count of the RYNO-plan mask; locations unknown |
| Health/armor upgrades, weapon ammo/individual levels | **Unknown**; not exposed as invented scalars |

Field labels live in an embedded resource. The raw DDL view labels semantics Unknown
even though its field-name hashes are checked. See [RESEARCH.md](RESEARCH.md) for the
36-record evidence, alternate branch, exact offsets and integrity results.

## Integrity and writing decision

The public NinjaSaves page describes options but supplies **no code payload**, offsets,
search anchors, checksum-fix command or algorithm. Searches of the specified SaveWizard
archive and Apollo database found no Rift Apart/PPSA01474 save patch. Thus the code
format/target and presence of checksum-fix commands **cannot be determined**; absence
of a published code is not evidence of plain writes.

The reproducible scan computed **41,652 digest candidates** over all 19 sections in all
36 primary gameplay records: CRC32 variants, CRC64 ECMA/XZ/GO-ISO, FNV-1/FNV-1a 32/64,
and Murmur3 x86_32. Each section was checked whole and with common 4/8-byte prefix or
suffix exclusions. Both byte orders were searched throughout the file outside the
hashed range. **No nondegenerate matches** were found; 161 zero/all-one candidates were
excluded. Small timer diffs change only the double, but this does not rule out integrity
stored elsewhere. The eight-byte `0xBC60B495` section equals career XP in all 36 records.

This rules out only the tested hypotheses. Custom seeds, other coverage, zeroed checksum
fields, larger hashes, cross-section dependencies and signatures remain open. Without
plain-write evidence or complete integrity knowledge, maturity stays **ReadOnly**.
`Serialize()` reparses both the DAT1 envelope and supported DDL roots before returning
an isolated copy of the original bytes.

## Validation and research commands

```powershell
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
dotnet test --project plugins/Insomniac/SaveToolkit.Plugins.Insomniac.Tests
dotnet test --project tests/SaveToolkit.Core.Tests
python -B plugins/Insomniac/research/hash_algorithms.py
python -B plugins/Insomniac/research/inspect_integrity.py <absolute-path-to-PPSA01474_saves_dec.zip>
```

Plugin tests cover the conformance suite over every distinct sample, all 76 byte-identical
round trips, five specific gameplay records, both campaigns, the alternate early branch,
name-hash vectors, changing numeric widths, empty arrays, confidence labels, atomic
read-only rejection and corrupt DDL/header inputs. Sample-dependent tests skip when
samples are absent. Research scripts read the zip without extracting or writing data.

Validated: API build with zero warnings/errors; plugin tests **40/40 passed**; framework
tests **43/43 passed**. With an empty artifacts directory, **14 passed / 26 skipped**.

On the development machine, opening and building the initial view for all 36 primary
gameplay records measured 5.54 ms median and 49.65 ms maximum (Debug build, including
the first JIT invocation; archive import excluded).

## Sources and licensing

Research checked 2026-10-28. No downloaded tool was executed, no substantial upstream
code was copied, and no new runtime package was added. Format facts and hash algorithms
were implemented independently; attribution is retained here.

* **Neptuwunium / Rivet**, pinned to `a631c96ed6455f9873a646825d37883f99defa6e`, EUPL-1.2:
  the links above establish section identities, DDL metadata and gameplay field names.
  The [DDL reader](https://github.com/neptuwunium/rivet/blob/a631c96ed6455f9873a646825d37883f99defa6e/Rivet.DDL/DDLSerializer.cs)
  informed format research; PS5 widths/padding were independently checked, not assumed
  identical to that reader.
* **Tkachov / ALERT**, GPL-3.0:
  [DAT1 reader](https://github.com/Tkachov/ALERT/blob/main/dat1lib/types/dat1.py),
  [serialized sections](https://github.com/Tkachov/ALERT/blob/main/dat1lib/types/sections/__init__.py),
  [format wiki](https://github.com/Tkachov/ALERT/wiki/Formats). Framing, string region,
  DDL structures and integer narrowing; reference only.
* **VELD-Dev / RiftRipper**, GPL-3.0:
  [repository](https://github.com/VELD-Dev/riftripper). Round-one asset-format lead;
  PS5 support unconfirmed and no save-integrity algorithm located.
* **ROStudios25 / RCRA-Forge**:
  [repository credits](https://github.com/ROStudios25/RCRA-Forge). Discovery lead
  pointing to Neptuwunium's Rivet DDL research; no code or assets used.
* **NinjaSaves**:
  [PPSA01474 option listing](https://ninjasaves.com/cheats/ratchet-clank-rift-apart-ppsa01474).
  Candidate concepts only; no binary patch instructions were available.
* **sterben-Dev10 / SaveWizard-Resources**, GPL-3.0:
  [tree at `4c32596`](https://github.com/sterben-Dev10/SaveWizard-Resources/tree/4c32596efde54af7bec390b298265f09c929d94d).
  Searched code/patch paths and text for title/name; no Rift Apart save code located.
* **bucanero / Apollo patches**, GPL-3.0:
  [tree at `273f032`](https://github.com/bucanero/apollo-patches/tree/273f032b4c31c017fed5c1588f209b8faf87ba6c).
  Searched patch database; no PS5 Rift Apart entry located. PS3/PS4 Ratchet patches do
  not establish a PS5 format.
* **CRC RevEng**:
  [catalogue](https://reveng.sourceforge.io/crc-catalogue/17plus.htm), CRC parameters
  and `123456789` check vectors used by the offline scan.
* **Fowler, Noll, Vo et al.**:
  [FNV specification, draft 25](https://www.ietf.org/archive/id/draft-eastlake-fnv-25.html),
  algorithm definitions/constants; independently implemented, no source copied.
* **Austin Appleby / SMHasher**, public domain:
  [MurmurHash3 reference](https://github.com/aappleby/smhasher/blob/master/src/MurmurHash3.cpp),
  x86_32 algorithm, tail and avalanche used by the offline scan.

## Open work

Obtain a documented plain-write PS5 patch or controlled pre/post edit pair; identify
all integrity dependencies; decode the `INVT` inventory structure and health/weapon
progression; resolve raritanium accounting, difficulty labels and collectible locations;
validate a fixed-size edit in-game before considering promotion beyond Experimental.

## SDK requests

None. Existing bindings, warnings, confidence labels and lazy sections support this stage.
