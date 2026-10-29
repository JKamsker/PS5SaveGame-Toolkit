# SDK-10 lossless SFO patch substrate

`SaveToolkit.Sdk.Saves.ParamSfoDocument` is an additive, pure helper for a narrow
operation: replacing the text of one existing, unique UTF-8 SFO entry without
rewriting the file. It is deliberately separate from the compatibility projection
in `ParamSfo.TryParse`, which may continue to use its existing display fallback for
inspection.

## Scope

The helper parses a bounded PSF/SFO byte buffer and keeps the original bytes,
index order, key bytes, format code, logical length, reserved length, data offset,
raw index entry, reserved value slot, and trailing bytes. It never performs file
or network I/O and does not know game-specific key names. A patch plan can target
only an existing unique entry with format `0x0204` (UTF-8) or `0x0004`
(UTF-8-special).

```csharp
using SaveToolkit.Sdk.Saves;

var document = ParamSfoDocument.Parse(bytes, ParamSfoPatchLimits.Default);
var plan = document.PlanExistingUtf8("SUBTITLE", "User-authored caption");
ReadOnlyMemory<byte> patched = document.Apply(plan);
```

`ParamSfoPatchLimits` requires finite positive limits for the complete file,
reserved slot, key bytes, and replacement encoded bytes. The default is 1 MiB per
file, 64 KiB per reserved slot, 1 KiB per key, and 4 KiB per replacement value
including its required NUL terminator. `MaxEncodedValueBytes` is therefore a
terminator-inclusive helper limit. It is distinct from a generic
`TextSpec.MaxUtf8Bytes` logical payload hint: when a consumer uses this helper,
the logical hint must fit `min(entry capacity, MaxEncodedValueBytes) - 1` because
one byte is reserved for the terminator.
The replacement must be strict UTF-8, contain no embedded NUL, include its NUL
terminator within the entry's reserved capacity, and remain within the configured
encoded-byte limit. Empty text is represented by a one-byte terminator.

`ParamSfoPatchPlan` records the key, format, index position, data offset, original
and reserved lengths, original/new text, the source SHA-256, and an entry SHA-256.
`Apply` checks every identity field and both fingerprints before producing bytes.
Changing the source, forging the entry identity, targeting an unsupported format,
or supplying malformed structure raises `ParamSfoPatchException` before output
publication.

## Preservation rules

The output has exactly the original length. The header, version bytes, index order,
key table and alignment, unknown index/value formats, untouched entries, reserved
bytes, and trailing data are copied byte-for-byte. A no-op plan returns a byte-
identical copy. A successful patch changes only the selected index entry's
`param_len` and the bytes occupied by the new value plus terminator. Bytes in the
selected reserved slot after the new logical value are intentionally left as they
were; consumers must use the new `param_len` and must not interpret that preserved
suffix as part of the value.

The strict writer rejects duplicate keys, missing key terminators, table/index
overlap, out-of-bounds or overflowing offsets, overlapping reserved data slots,
`param_len > param_max_len`, invalid target UTF-8, and values that exceed either
capacity or the named global limit. Unsupported entries remain readable and
preserved but cannot be targeted.

This substrate does not authorize `sce_sys/*` ownership, change export/history,
or synchronize a caption with timer, difficulty, locale, or any other gameplay
field. Those concerns belong to the separate host-ownership and consumer PRs.

## Format references and limits of the evidence

The [PS4 Developer Wiki PARAM.SFO reference](https://www.psdevwiki.com/ps4/PARAM.SFO#Internal_Structure)
was used for public format facts such as the `\0PSF` header, little-endian tables,
16-byte index records, logical/reserved lengths, offsets, and NUL-terminated keys.
The page documents a cross-platform SFO shape; it does not establish any PS5 title's
metadata ownership or console acceptance.

[RPCS3's PSF loader](https://github.com/RPCS3/rpcs3/blob/master/rpcs3/Loader/PSF.cpp)
and [OrbisPkgTool's SFO model](https://github.com/pearlxcore/OrbisPkgTool/blob/master/OrbisPkgTool/Sfo/ParamSfo.cs)
were consulted as independent format comparisons only. No downloaded code was
copied or ported, and RPCS3 remains under its [GPL-2.0-or-later license](https://github.com/RPCS3/rpcs3/blob/master/LICENSE).
