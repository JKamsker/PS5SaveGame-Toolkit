# LSPK v18 digest investigation (Round 2)

## Result on PPSA18463

The supplied PS5 package has digest `6D98C6D4C2FF81E7C7F43AAA881D66DA`.
It equals **MD5 of concatenated expanded entries in increasing payload-offset
order, with each final digest byte incremented by one modulo 256**. This is the
LSLib content-hash algorithm; the missing piece in Round 1 was the input order.
The increment applies to the final digest, not to the payload bytes.

Physical order:

1. `AutoSave_0.WebP`
2. `Globals.lsf`
3. `LevelCache/SYS_CC_I.lsf`
4. `LevelCache/TUT_Avernus_C.lsf`
5. `meta.lsf`
6. `SaveInfo.json`
7. `StorySave.bin`

The file list uses a different order (see README). Ordinal filename sorting puts
uppercase names before lowercase names and also fails. Ordinal-ignore-case sorting
**happens to equal physical order in this fixture**, so the one independent sample
cannot distinguish those two hypotheses. The writer preserves physical and table
orders independently; it does not rename, insert or remove entries. Thus both
matching hypotheses remain satisfied after every supported edit. No claim is made
about differently ordered packages from other versions/platforms. A v18 package
whose digest fails the physical-order calculation is rejected, including zeroed
digests; there is no fallback to silently exporting a stale checksum.

## Reproducible experiments

`DigestResearchTests.SampleDigestHypothesesAreReproducible` runs 844 named
candidate calculations against the primary package. The console backup is byte
identical; it is not an independent hash vector. The archive contains generation
0 only, so there are no additional snapshots to compare.

The matrix covers stored versus expanded entry data; file-list, payload-offset,
ordinal filename and case-insensitive filename orders, each forward and reversed;
unmodified input versus input bytes incremented by one; UTF-8 filename prefixes,
NUL-terminated, lowercase and backslash variants; binary and lowercase-hex
per-entry MD5 concatenations rehashed with MD5, including incremented per-entry
digests; and every nonempty subset of the seven entries in file-list order.
Separate inputs cover the file list with preamble, compressed table, expanded
table, physical payload area including padding, zeroed-digest header, whole file
with zeroed digest, whole file with digest removed, and original whole file.
Every resulting digest is compared both unchanged and bytewise incremented.

Exactly two candidates match:

| Candidate | Match |
| --- | --- |
| Expanded / physical offset / forward / final digest +1 | Yes |
| Expanded / case-insensitive filename / forward / final digest +1 | Yes (same order) |
| All other 842 candidates | No |

Regression tests also use independent MD5 vectors (empty input and `abc`),
synthetic packages with deliberately different table, physical and filename
orders, and single-byte payload/digest corruption. Rebuilt packages validate
their digest before resource parsing. MD5 does not authenticate the archive and
does not cover filenames, flags, padding or other container metadata.

## Upstream evidence and game validation

Research revisited LSLib issues and `PackageWriter.cs` history, not just community
editor claims. No downloaded executable or upstream script was run.

- [LSLib PackageWriter.cs](https://github.com/Norbyte/lslib/blob/6f5f6987d10d8485876b8368431c948e4dea4f83/LSLib/LS/PackageWriter.cs):
  `ComputeArchiveHash` hashes expanded contents, then increments digest bytes.
  Modern versions use build-input order; only pre-v15 sorts ordinally. Build-input
  order is also the writer's physical payload order. Its comment about sorting
  does not describe the modern branch.
- [Original archive-hash change](https://github.com/Norbyte/lslib/commit/da528ba10e5a748a3a48ea37443e8bf5fa99d8d2):
  introduced this algorithm to fix save corruption warnings. This older revision
  used culture-sensitive name sorting; it is historical context, not the current
  ordering rule.
- [LSLib issue #74](https://github.com/Norbyte/lslib/issues/74): Norbyte requested an
  original save and reported a fix in v1.5.10 (release link says 1.15.10); the
  reporter confirmed loading after the update. This was BG3 Early Access in 2021,
  not a PS5 round-trip test and not evidence that checksums can be skipped.
- [Wahkah/BG3SE write investigation](https://github.com/Wahkah/BG3SE/blob/main/README.md):
  reports warnings after non-ECS edits and load failure after ECS edits on PC.
  Its unsuccessful hash search is superseded **for this PS5 fixture** by the
  reproducible match above. Its proposed LSMF hash interpretation is not adopted.
- [Serofix editor README](https://github.com/Serofix-code/bg3-save-editor/blob/main/README.md):
  uses Divine for repacking and warns that repacked saves can trigger tamper
  warnings. It supplies no evidence that a zero or stale digest is safe on PS5.

The digest is now reproduced rather than assumed unnecessary. No in-game or
console test has been performed. Experimental maturity is based on conformance,
byte preservation, checksum validation and edit/reopen tests only. Opaque LSMF
and story data are never edited; their internal integrity remains outside scope.
