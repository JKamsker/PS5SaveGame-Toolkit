# ASTRO's PLAYROOM (`astro-playroom`)

Title: **PPSA01325**. Maturity: **Experimental**, with edits passing conformance and format tests;
no edited save has been loaded on a console. Only a human should promote this to Stable.

## Files and supported layout

The game document is `memory.dat` in `sce_sdmemory` (archive directory `sdimg_sce_sdmemory`),
with an identical console backup under `sdimg_sce_bu_sdmemory` in each supplied generation.
The plugin returns only its own file. The framework's default export mirrors matching primary
files to their same-generation backups; it does not require plugin-specific backup writes.
System files are untouched. The plugin has no filesystem/network access and depends only on the SDK.

| Range | Observed role |
| --- | --- |
| `0x000–0x3FF` | 1,024 zero bytes; nonzero variants are rejected |
| `0x400–0x401` | `0D 0A` (CRLF), part of the text, **not a version/length word** |
| `0x402…` | UTF-8 XML-like `<PreInstall>` tree; numeric tag names are legal |
| After `</PreInstall>` | Opaque suffix, if any; preserved without interpretation |
| First NUL from `0x400` | Allocation text terminator |
| Terminator through `0xFFFFF` | All zero in both snapshots; nonzero padding variants are rejected |

Exactly **1,048,576 bytes** are required. There is no game-layer encryption or compression in this
observed allocation. Platform save decryption/encryption is outside this plugin.

The tokenizer records source byte spans for elements, quoted attributes and non-whitespace text
segments. It accepts numeric names, repeated siblings, comments, self-closing elements, mixed
content, UTF-8 values and single/double-quoted attributes. It does not expand entities or use XML
libraries. Entity spellings are shown literally and preserved. DTDs, processing instructions and
CDATA are unsupported and rejected inside the root rather than rewritten incorrectly.

Raw sections load lazily and page at 64 immediate entries plus a continuation. Parsing is bounded
to 64 levels, 4,096 elements and 4,096 values; each curated table stays below 5,000 rows. Value IDs
use tag names and sibling occurrence numbers, never byte offsets, so length-changing edits retain
their IDs. Empty elements remain visible as tree sections without inventing a text value.

## Editing and confidence

- **Verified name matches:** `Common/NumCoin` (coins) and `Common/PlayCount` (play count).
  Observed values are 0/1 in generation 0 and 1749/2 in generation 1 respectively. What event
  increments PlayCount, and the game's actual coin limit, have not been established. The editable
  range 0…2,147,483,647 is a conservative signed-storage bound, not a verified gameplay limit.
- **Inferred effects, literal saved names:** boolean fields in `Progress` and `World`, including boss,
  visited, clear and map-notification flags. `Artifact`, `Puzzle` and `Prop` expose `IsGot` and
  `IsCinematicsPlayed` in tables. Their saved identifiers are displayed without guessing item names.
  These names strongly suggest collection/progression semantics, but linked counters, notification
  behavior, cinematics and trophy effects have not been tested in-game.
- **Unknown:** other primitives, mixed text and all attributes are read-only in Raw. Leaderboard
  numeric IDs, scores/units, prompts and `UdsWorkaround` counters are preserved, not remapped.
- **Special bots:** no `Armadillo` or dedicated collected-bot fields occur in either supplied
  generation. The UI reports this absence. Newer community unlock flags are not inserted into an
  older save. Trophy-action names containing `BOT` are not treated as collected-bot flags.

Only existing supported integer/boolean text spans are writable. Every other byte, including
indentation, quotes, comments, unknown values and the opaque suffix, is copied verbatim. If the
replacement length changes, everything through the original NUL shifts together; remaining capacity
is zero-filled. There is no identified length field to update. Batched edits validate total capacity
including the NUL before any state change. Serialization reparses its output and compares every
original primitive's value. Undo back to an original value restores its exact original spelling
(including leading zeros). No-op output is byte-identical on all four records.

## Snapshot and integrity evidence

Research date: **2026-10-28**. Source: main-repository `PPSA01325_saves_dec.zip`; duplicate ZIP records
remain distinct generations, not four independent saves. Only `memory.dat` was examined; no account
metadata or credentials were read. Scratch extraction was outside the repository.

Both primary records and both backup twins have **785 elements / 508 primitive text values**,
with no attributes. Each primary/backup pair is byte-identical. The two primary generations differ
at 27,999 common offsets, first `0x428`, last `0x7773`.

| Observation | Generation 0 | Generation 1 |
| --- | ---: | ---: |
| Root end, exclusive | `0x7776` | `0x7729` |
| Text NUL offset | `0x7776` | `0x7776` |
| Opaque suffix length | 0 | 77 bytes |
| `NumCoin` / `PlayCount` | 0 / 1 | 1749 / 2 |
| True `Artifact/*/IsGot` | 0 of 50 | 3 of 50 |
| True `Puzzle/*/IsGot` | 0 of 96 | 9 of 96 |
| True `Prop/*/IsGot` | 0 of 18 | 1 of 18 |
| CRC32 over `[0x400, NUL)` | `D15E8648` | `910A25C2` |
| Adler-32 over `[0x400, NUL)` | `ACF2293B` | `C4242ACD` |
| Byte sum over `[0x400, NUL)` | `0023272D` | `002328BF` |

The second suffix consists of fragments such as `ll`, `all`, `nstall` and `>`; **stale overwrite
residue is inferred**, not a defined format feature. The root became 77 bytes shorter but the first
NUL did not move. Tokenization therefore stops at the balanced root, not at that NUL; suffix bytes
remain opaque. Their byte preservation is tested through shorter/longer edits.

No integrity field was identified. Every byte before `0x400` is zero in both generations, and the
following two bytes are CRLF while the text changes. The research tests check all unaligned 32-bit
header words in both endiannesses against CRC32/ISO-HDLC, Adler-32, byte sum, text lengths and end
offsets; lengths are also checked as 16-bit words. Candidate ranges start at `0x400` or `0x402` and
end at the root end, the first NUL, or immediately after the NUL. None match. There are no named
checksum/hash/length/version fields in the observed tree. Hash-vector tests verify the candidate
algorithms independently; they are test helpers, not production save checksums.

**Inference:** the observed game payload has no integrity/length metadata to repair. This plus
community reports of direct text editing supports Experimental writing for this layout. It is not
proof against an undocumented algorithm, a relationship among gameplay flags, or integrity changes
in newer versions. Syntactically valid value corruption cannot be detected without a known checksum.

## Validation

```powershell
dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
dotnet test --project plugins/AstroPlayroom/SaveToolkit.Plugins.AstroPlayroom.Tests
dotnet test --project tests/SaveToolkit.Core.Tests
```

The plugin suite covers all four sample records and both distinct generations, no-op identity,
known values, meaningful coin edits, boolean and integer length changes, exact byte preservation,
opaque suffixes, undo, multiple edits, session isolation, exact capacity and atomic overflow rejection,
numeric tags, attributes, comments, UTF-8, duplicate sibling IDs, lazy paging, malformed tags/encoding,
missing terminators, unsupported padding, oversize files and parser limits. Sample tests use
`SampleSaves` and skip when the archive is absent. Open/main-view timings in the initial test run
were 1.4–20.4 ms per sample; these are local observations, not a benchmark guarantee.

## Sources

- [Repository format reconnaissance](../../docs/formats/PPSA01325.md): initial layout, sample counts,
  numeric-tag warning and starting community reference; all byte claims rechecked against the archive.
- [PROSPEROPatches title record](https://prosperopatches.com/PPSA01325): title-ID/name correspondence.
- [Original PS5_Jailbreak editing thread](https://www.reddit.com/r/PS5_Jailbreak/comments/1wemi58/astros_playroom_special_bots_dont_show_up/):
  first-person reports of editing newer `memory.dat` Common/Armadillo flags with a text editor,
  updating the backup, and successfully reloading. Anecdotal supporting evidence only; none of
  those flags or proposed values were copied into these older samples.
- [zlib manual, checksum functions](https://zlib.net/manual.html#Checksum): candidate CRC32 and
  Adler-32 conventions for the integrity investigation; also cross-checked with Python standard
  library `zlib` in scratch analysis. No zlib package is needed by the plugin.
- [RFC 1952](https://www.rfc-editor.org/rfc/rfc1952): CRC32 polynomial specification used for the
  independent, bitwise test helper. No substantial external code was copied or executed.

Searches included ASTRO's PLAYROOM save editors, GitHub parsers/templates, `memory.dat` header,
`PreInstall`, `NumCoin`, coins/collectibles, special bots, CRC/Adler/checksums and PS5_Jailbreak
threads. No reusable game-specific parser or documented integrity algorithm was established.
Search hits for Astroneer parsers and other games' `memory.dat` formats were excluded.

## Open questions

Console validation remains required, especially coin limits and consistency between collection,
cinematic, notification and `UdsWorkaround` flags. Newer special-bot schemas need actual samples
and a fresh integrity investigation before curated support. The opaque suffix's purpose is unknown.

## SDK requests

None. Existing lazy sections, bindings, byte outputs and framework backup mirroring are sufficient.
