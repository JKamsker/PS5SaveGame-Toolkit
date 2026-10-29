# SDK-06: lossless JSON scalar patches

`SaveToolkit.Sdk.Json.LosslessJsonDocument` is the shared lexical substrate for JSON-backed plugins.
It parses one strict UTF-8 JSON value, retains the input byte array, and exposes a stable preorder
`LosslessJsonNode` tree. It deliberately does not perform object or array edits; GenericJson and
XorJson keep their structural identities, envelopes, and history rules in their own adapters.

```csharp
using SaveToolkit.Sdk.Json;

var document = LosslessJsonDocument.Parse(bytes, LosslessJsonLimits.Default);
var coins = document.Root.Children.Single(node => node.PropertyName == "coins");
var undo = document.CaptureSnapshot();
document.ReplaceScalar(coins, "9007199254740993"); // exact lexeme; never a double
var output = document.Serialize();
document.RestoreSnapshot(undo); // restores the prior replacement set
```

`Serialize()` returns an exact clone when there are no replacements. With replacements, it copies
all original bytes and substitutes only the selected scalar spans. BOM, leading/trailing whitespace,
line endings, escaped strings, duplicate member names, separators, and unknown members therefore
remain byte-for-byte unchanged. A node's `OriginalSpan` is its value token (quotes or container
braces included). `PropertyNameSpan` is the quoted member-name token; `PropertyNameLexeme` retains
its original escape spelling. `PropertyOccurrence` is zero-based among equal decoded names in one
object, so `{"x":1,"x":2}` has two distinct identities.

`MaxBytes` applies to both the imported input and every possible serialized output. A replacement
or snapshot restore computes the checked UTF-8 byte total before changing node state; an operation
that would exceed the budget throws `LosslessJsonException` and leaves the previous lexemes and
serialized bytes intact. `Serialize()` repeats the budget check before allocating its output buffer.
The strict UTF-8 byte count is obtained before scalar parsing allocates an encoded candidate buffer
or decoded string, so a rejected replacement cannot exceed the selected output budget temporarily.
The node overload of `ReplaceScalar` also verifies document ownership before looking up the stable
ID, so an equal-ID node from another document cannot patch this document.

Numbers are retained as raw syntax during parsing, including lexemes that exceed
`ExactJsonNumber`'s editing limits. A replacement number must pass `ExactJsonNumber.Validate`
using the document's `MaxNumberLength` and `MaxAbsoluteExponent`; callers can consequently expose
an oversized imported number as read-only without losing its original bytes. String replacements
must be one JSON string scalar and valid UTF-8; booleans and null remain distinct from a missing
member (a missing member has no node). Snapshots restore imported scalar metadata directly, so an
empty snapshot or an edit to a different field never reparses an oversized original number through
the stricter editable-number limits.

The default safety envelope matches the existing JSON plugins: 16 MiB input, 64 nesting levels and
250,000 value nodes. `LosslessJsonLimits` permits lower budgets and bounded overrides. The parser
accepts the optional UTF-8 BOM and JSON whitespace that existing parsers accept. Comments, trailing
commas, multiple roots, invalid UTF-8 and unpaired Unicode surrogates are rejected. A
`LosslessJsonException` reports a bounded parse/limit error and, when known, the byte offset.
For parser syntax errors, `ByteOffset` is an absolute byte coordinate in the original input,
including an optional BOM and multibyte UTF-8 preceding the error. Malformed scalar replacement
syntax is normalized to the same SDK exception before any document state changes.

The implementation uses .NET's `Utf8JsonReader` for grammar and escape validation and
`ExactJsonNumber` for replacement-number validation. See the [Utf8JsonReader documentation](https://learn.microsoft.com/dotnet/api/system.text.json.utf8jsonreader)
and [RFC 8259](https://www.rfc-editor.org/rfc/rfc8259) for the underlying JSON grammar and UTF-8
requirements.
