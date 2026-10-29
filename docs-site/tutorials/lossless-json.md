---
title: Preserve JSON while editing scalar values
description: Parse bounded UTF-8 JSON, retain lexical trivia and duplicates, and patch only reviewed scalar spans.
---

# Preserve JSON while editing scalar values

`LosslessJsonDocument` is a small SDK substrate for plugins that must change a scalar without
normalizing the surrounding document. It retains the original UTF-8 bytes, BOM, whitespace,
duplicate property occurrences and numeric lexemes. Containers and property names are structural
read-only data; a plugin can replace a scalar token only after it has identified the node it owns.

This page teaches the merged SDK contract. It does not claim that GenericJson or XorJson has
migrated to it. Their structural IDs and XOR/envelope rules remain plugin-owned. The shared API
is useful at that boundary because it does not invent those identities or rewrite an envelope.

The runnable excerpts below are taken from
[`LosslessJsonDocumentTests.cs`](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/tests/SaveToolkit.Core.Tests/LosslessJsonDocumentTests.cs).
Run the focused tests after changing an adapter:

```powershell
dotnet test --project tests/SaveToolkit.Core.Tests --filter-method '*LosslessJsonDocumentTests*'
```

## Parse strict UTF-8 with finite limits

Pass bytes, not a decoded `string`. Parsing validates strict UTF-8 before JSON diagnostics and
rejects concatenated values, malformed escapes, excessive depth/tokens and input larger than the
selected `LosslessJsonLimits`. Defaults are 16 MiB, depth 64 and 250,000 value nodes; the hard
envelope is 64 MiB, depth 256 and 1,000,000 nodes. Replacement numbers also have bounded lexeme
length and absolute exponent.

```csharp
var input = Encoding.UTF8.GetBytes("\uFEFF \r\n{\"x\":1.2300e+004,\"x\":\"\\u0041\"} \t\r\n");
var document = LosslessJsonDocument.Parse(input);

Assert.Equal(input, document.Serialize());
Assert.Equal("\uFEFF \r\n", Encoding.UTF8.GetString(document.Preamble.Span));
Assert.Equal(" \t\r\n", Encoding.UTF8.GetString(document.Postamble.Span));
Assert.Equal("1.2300e+004", document.Root.Children[0].OriginalLexeme);
Assert.Equal(1, document.Root.Children[1].PropertyOccurrence);
```

`Preamble` and `Postamble` include bytes outside the root value. A duplicate name is not merged:
use `PropertyOccurrence` and the node's stable preorder `Id` when a plugin's structural identity
needs to distinguish occurrences. `LosslessJsonException.ByteOffset` is an original byte offset
when the parser can calculate one.

## Replace one scalar, and only that span

`ReplaceScalar` accepts exactly one JSON scalar lexeme: a string including quotes, a number,
`true`, `false`, or `null`. Leading/trailing trivia, objects and arrays are rejected. The method
returns `false` for an exact no-op. The serialized length is checked against `MaxBytes` before the
document state changes, and invalid UTF-8 or an unpaired surrogate leaves the document unchanged.

```csharp
const string source = "{ \"before\" : 123456, \"value\" : 1.2300e+004, \"after\" : \"keep\" }";
var document = LosslessJsonDocument.Parse(Encoding.UTF8.GetBytes(source));
var value = document.Root.Children[1];
var after = document.Root.Children[2];

Assert.True(document.ReplaceScalar(value, "null"));
Assert.True(document.ReplaceScalar(after, "\"changed\""));
Assert.Equal("{ \"before\" : 123456, \"value\" : null, \"after\" : \"changed\" }",
    Encoding.UTF8.GetString(document.Serialize()));
Assert.Equal("1.2300e+004", value.OriginalLexeme);
Assert.Equal("null", value.CurrentLexeme);
```

An imported number can be longer or have a larger exponent than a replacement is allowed to use;
the original lexeme remains readable and round-trippable. This is why a plugin should not parse
every number through `double`, `decimal` or JavaScript `Number` before deciding what to write.

## Capture and restore bounded history

Snapshots contain only replacement lexemes keyed by parse-time node IDs and report an approximate
`SizeBytes`. They are owned by their document. Restoring a snapshot returns the prior snapshot for
redo, rejects a snapshot from another document, and preserves untouched bytes.

```csharp
var document = LosslessJsonDocument.Parse(Encoding.UTF8.GetBytes("[1,2,3]"));
var original = document.CaptureSnapshot();

document.ReplaceScalar(document.Root.Children[0], "1000000000000000000000000000000000000000001");
var changed = document.CaptureSnapshot();
document.ReplaceScalar(document.Root.Children[1], "false");

var redo = document.RestoreSnapshot(changed);
Assert.Equal("[1000000000000000000000000000000000000000001,2,3]",
    Encoding.UTF8.GetString(document.Serialize()));
document.RestoreSnapshot(original);
Assert.Equal("[1,2,3]", Encoding.UTF8.GetString(document.Serialize()));
document.RestoreSnapshot(redo);
Assert.Equal("[1000000000000000000000000000000000000000001,false,3]",
    Encoding.UTF8.GetString(document.Serialize()));
```

Use the host's normal document history for user-visible undo. A plugin may keep this snapshot as
part of its own memento, but it must account for the reported size and preserve any structural
identity/envelope snapshot it owns. `LosslessJsonSnapshot` is not a cross-document patch token.

## Boundaries for a plugin adapter

The shared tree deliberately stops at scalar spans:

- Keep object/array identities, duplicate-key policy, schema checks and structural add/remove
  semantics in the plugin.
- Keep XOR wrappers, checksums, compression and encrypted/enveloped bytes in the plugin. Parse the
  decoded JSON only after the envelope has been verified, then write it back through that same
  plugin-owned boundary.
- Treat `LosslessJsonDocument.Serialize()` as a candidate byte sequence. The plugin still owns
  complete output verification, no-op equality, integrity repair and reopening the output.
- Bound every input, replacement and retained snapshot. Do not turn a malformed or over-budget
  document into a normalized “repair” export.

The shared tests cover BOM, CRLF/trailing trivia, duplicate names, exact numbers, type-changing
scalar patches, malformed input, strict UTF-8, cumulative byte budgets and typed undo/redo. Add a
format-specific characterization test before an adopter makes a writer claim.

Continue with [canonical field references](field-references.md), or return to
[create your first plugin](first-plugin.md) and [query a bounded collection](collections.md).
