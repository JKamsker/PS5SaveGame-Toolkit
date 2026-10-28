# Sucker Punch save plugin (`suckerpunch`)

Titles: **PPSA02225** (Ghost of Tsushima, Director's Cut) and **PPSA26344** (Ghost of Yōtei).

The plugin is deliberately **ReadOnly**. It preserves and displays the complete decrypted file,
but does not publish editable game fields because no field-level Yōtei mapping or PS5 in-game write
test is available. `Serialize()` still returns an exact copy of the opened file for tooling that
needs to inspect it.

## Files and observed samples

The title directories contain fixed-size `dis.dat` slot files and `memory.dat` state files. The
sample archive has the following sizes:

| Title | Files | Size |
| --- | --- | ---: |
| PPSA02225 | `dis.dat`, `memory.dat` | 555,008 bytes |
| PPSA26344 | `dis.dat`, `memory.dat` | 616,448 bytes |

PPSA02225 samples have non-zero data in both file roles. PPSA26344's `dis.dat` files include
all-zero placeholders; the non-zero `memory.dat` and one `dis.dat` use the newer header described
below.

## Verified Tsushima container (PPSA02225)

Every Tsushima sample uses little-endian words at offsets 0x00–0x0F:

| Offset | Size | Meaning | Confidence |
| ---: | ---: | --- | --- |
| 0x00 | 4 | Magic `0x0000014E` (334) | Verified in all supplied samples and the converter research |
| 0x04 | 4 | Content size; the remainder of the fixed 555,008-byte file is zero padding | Verified in all supplied samples |
| 0x08 | 4 | Additive checksum | Verified in all supplied samples |
| 0x0C | 4 | Format version (`48` or `49` in the supplied files) | Verified in all supplied samples |

The checksum is the unsigned 32-bit sum of every byte in `data[0x10..contentSize)`, plus the
stored content size, magic, and version:

```text
checksum = (sum(data[0x10:contentSize]) + contentSize + magic + version) mod 2^32
```

The implementation rejects a bad magic, out-of-range content size, or checksum mismatch. It does
not rewrite or reinterpret the remaining game data.

## Yōtei observations (PPSA26344)

Non-zero samples begin with magic `0x00000150`, followed by three values that do not behave like
the Tsushima content-size/checksum/version tuple. For example, the first words of `memory.dat`
are `0x150, 0x0D, 0x647E3, 0x1C84573`; the non-zero `dis.dat` is `0x150, 0x10, 0x64705,
0x1CC2A56`. Applying the Tsushima checksum to these files fails, so these words are exposed only
as unknown raw header values. Empty `dis.dat` placeholders are accepted as all-zero files.

No compression or encryption layer was identified in the supplied decrypted samples. No game
resource, play-time, difficulty, chapter, inventory, or technique offset is claimed by this
plugin. `param.sfo` subtitles are shown as correlation text only; they are not treated as binary
fields.

## Research log and sources

* [KOVRlN/ghostoftsushima-save-converter technical notes](https://github.com/KOVRlN/ghostoftsushima-save-converter/blob/main/TECHNICAL.md) documents the 16-byte Tsushima container, fixed PS4 size, platform block, and additive checksum. The checksum formula was independently checked against every supplied PPSA02225 file.
* [KOVRlN converter source](https://github.com/KOVRlN/ghostoftsushima-save-converter) was read as an algorithm reference; no code was executed or copied.
* [SaveWizard Ghost of Tsushima code archive](https://github.com/sterben-Dev10/SaveWizard-Resources/blob/main/SaveWizard-Codes/Ghost%20of%20Tsushima%20Codes%20%5BSaveWizard%5D.txt) is a lead for future field-offset research. Its cheat addresses are not used because they target other builds/platforms and are not verified against these PS5 dumps.

## Open questions

* Identify the PPSA26344 header/checksum and determine whether its large fourth word is a build,
  timestamp, or another integrity value.
* Correlate `param.sfo SUBTITLE` play time/difficulty/chapter with controlled save changes to
  locate real field offsets.
* Find a documented PS5 editor or in-game round-trip procedure before enabling writes.

## SDK requests

None. The existing `HexNode`, `ReadBlob`, confidence labels, and read-only maturity are sufficient
for the current evidence level.
