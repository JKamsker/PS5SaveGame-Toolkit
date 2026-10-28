# Obfuscated JSON (`format.json-xor`)

**Experimental** format-preserving token editor. PPSA16064 is identified as
**Minecraft Dungeons II** by the title-id source below. The round-one XOR-1
hypothesis is disproved for the supplied global settings: the actual encoding
is **subtract one from every UTF-8 byte modulo 256**. Decoding adds one; encoding
subtracts one. Synthetic tests additionally cover genuine XOR-1 JSON. The requested
plugin folder/id are retained, while its display name is “Obfuscated JSON”.

## Coverage and evidence

| File role | Observation / support |
| --- | --- |
| GlobalSaveDataDefault/ue4savegame.dpx.sav | 3,978 bytes; byte-plus-one decoding produces a complete valid JSON object with `blobs`; `masterVolume=50`. Claimed and editable. XOR-1 produces invalid JSON. |
| Character*/ue4savegame.dpx.sav | 40,062-byte plain UTF-8 JSON with `SerializeMeta` and `CharacterSaveV1`. Declined here; the existing GenericJson plugin handles plain JSON. |

These are the two game-file entries in the supplied archive; there are no
additional settings snapshots/backup twins to establish wider release coverage.
The byte transform and full JSON grammar are **verified**. Game-specific meaning,
ranges, dependencies and console acceptance remain unknown. Synthetic XOR-1
support does not establish that any supplied game uses XOR-1.

## Detection, editing and preservation

Probe checks a bounded prefix, then fully parses each plausible decoding. It
returns Format (20) only for an unambiguous valid UTF-8 object/array. Plain JSON,
binary lookalikes, malformed encodings and ambiguous candidates (for example an
encoded empty array) are declined. Detection does not depend on title/filename.

The local token parser records original byte spans and ordered node indices.
It is copied from this repository's GenericJson implementation, with a local
namespace and encoding-aware serialization; there is **no project reference**
to GenericJson. Only SDK and runtime dependencies are used. Plugin instances are
stateless; documents perform no I/O.

Only changed primitive value tokens are replaced in the decoded byte stream.
Unchanged escapes, numeric spellings, duplicate property names, key order, BOM,
whitespace and line endings are retained. Re-encoding uses the detected scheme.
No-op and undo restore the original encoded bytes exactly. Serialize decodes and
parses its own encoded output, checking encoding, every node's type/name/value,
and node count before returning it.

Strings, booleans, Int128 integers and finite doubles are editable; null and
numbers outside the SDK range remain unchanged/read-only. Double edits carry a
precision warning. Invalid surrogate edits, type changes and non-finite floats
are rejected atomically. Values use Confidence.Unknown because JSON storage names
alone do not establish game meaning or safe combinations. No presets are offered.

Overview is bounded; JSON objects/arrays expand lazily in at most 128-node pages,
including hierarchical groups for wide containers. Limits: 16 MiB, depth 64,
250,000 nodes. JSON and these transforms have no intrinsic checksum/compression;
none was found in the supplied settings. External game dependencies remain unknown.

## Validation and open problems

17 tests pass with samples: conformance editing `masterVolume`, known sample
values and transform discrimination, plain Character rejection, byte-identical
no-op/undo, Unicode length-changing edits in both encodings, BOM/formatting,
duplicate names, malformed grammar/UTF-8, numeric limits, atomic failures,
ambiguity rejection, size/depth limits and a lazy 20,000-value tree.
Sample-dependent tests skip through SampleSaves when absent.
Verified with an empty sample directory: 12 pass, 5 skip, none fail.

Experimental is warranted by meaningful settings edits surviving serialization
and reopen. No in-game reload is verified; Stable requires a human console test.
Character schemas, safe game-specific ranges and support for other obfuscation
keys/transforms remain open. Plain Character editing is outside this plugin.

## Sources

* https://prosperopatches.com/PPSA16064 — title-id mapping to Minecraft Dungeons II.
* https://github.com/boykopovar/AnyPS5/discussions/8868 — independent first-party community title report corroborating PPSA16064; no save-format conclusions taken from the report.
* https://www.rfc-editor.org/rfc/rfc8259 — JSON grammar and UTF-8 requirements.
* https://learn.microsoft.com/dotnet/api/system.text.json.utf8jsonreader — token offsets and parsing API.
* `plugins/GenericJson/SaveToolkit.Plugins.GenericJson/{JsonTokenTree,JsonValueToken,JsonSaveDocument}.cs` — same-repository token-preserving editing approach/local source copies; no external third-party code copied or packages added.

The byte-minus-one transform is independently established against **the entire
sample**, not taken from an external format specification. No downloaded code
was executed and no sample data is committed.

## SDK requests

None for this implementation. Int128 and lazy sections cover the required
primitive values and bounded tree views.
