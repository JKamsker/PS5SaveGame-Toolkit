# UTF-8 text capacity and planned actions

`TextSpec.MaxUtf8Bytes` is an optional inclusive limit on the **logical value** encoded
as strict UTF-8. The host enforces it for field edits and action parameters before
calling the plugin. The browser shows used/capacity/remaining bytes while typing,
including in action forms, multiline fields, and compact table controls. Read-only
text retains its capacity hint. The server remains authoritative.

```csharp
var captionSpec = new TextSpec
{
    MaxLength = 80,    // Existing UTF-16 code-unit bound, independently enforced.
    MaxUtf8Bytes = 63, // Example only: publish a proven format-specific capacity.
};
```

The corresponding wire spec is additive:

```json
{ "kind": "text", "maxLength": 80, "maxUtf8Bytes": 63, "multiline": false, "readOnly": false }
```

- Missing/null `MaxUtf8Bytes` retains legacy text validation and has no byte counter.
- The valid capacity range is `TextSpec.MinimumUtf8ByteCapacity` (zero) through
  `TextSpec.MaximumUtf8ByteCapacity` (`int.MaxValue`). Zero accepts only empty text.
  Negative capacity metadata produces a controlled field-validation error.
- `MaxLength` continues to measure UTF-16 code units. An emoji such as `🎮` uses two
  UTF-16 code units and four UTF-8 bytes. Both limits apply when both are present.
- Storage NUL terminators and encoding preambles are excluded. A literal U+FEFF
  **inside the value** counts as three bytes, and a literal NUL counts as one byte.
  A format that forbids embedded NUL still needs its own validation.
- Invalid surrogate sequences are rejected for hinted edits. Neither host nor UI
  normalizes, truncates, or replaces user text to make it fit. Combining sequences
  count as supplied (`é` is two bytes, `e` + U+0301 is three).
- The existing `Multiline` policy independently controls carriage returns/line feeds.
- The property survives polymorphic/nested specs, field metadata snapshots and
  agreement checks, field/action wire serialization, and generated OpenAPI/docs.

For a proven NUL-terminated UTF-8 field with a storage capacity of `entryCapacity`
and a **terminator-inclusive** global limit (such as the strict SFO helper's
`MaxEncodedValueBytes`), publish `Math.Min(entryCapacity, inclusiveGlobalLimit) - 1`
after validating that both native bounds allow the terminator. The generic
`MaxUtf8Bytes` itself always excludes the terminator. This hint establishes no
ownership of system files, parser correctness, console compatibility, or automatic
caption synchronization.
Native writes must retain unknown bytes and integrity rules. A caption writer
that overwrites reserved bytes needs whole-byte history/mementos to restore them;
old/new caption strings alone cannot reconstruct those bytes. A plugin's
`Serialize()` must still return its complete owned outputs after a no-op. The
session's clean-document override list may legitimately be empty.

Planned-action dialogs announce **No changes. No undo step will be created.** for
a successful empty plan and disable Apply. Warnings/skipped rows remain visible.
Cancel and native Escape close without applying and return focus to the trigger.
A typed stale apply response or an apply failure with HTTP 409 and `code: "stale"` refreshes the document snapshot
before keeping the dialog and parameter values, focusing the reason, and offering
**Preview again** with the refreshed revision. Other HTTP 409 failures retain their
ordinary error message without being labeled stale. Recovery does not reuse the
rejected token or create local history. The focused dialog alert owns apply failures, avoiding a duplicate
background error toast; ordinary field edits retain their existing toast feedback.
If the snapshot read fails, the dialog retains the draft and offers **Retry document
refresh**. Preview remains blocked until a successful snapshot is installed; repeated
read failures stay recoverable inside the dialog. A cancelled read that only restores
cached data does not count as a fresh snapshot. Closing the dialog keeps the existing
background document refresh control available. Retrying a read never applies an action
or creates history. The dialog owns read-error announcements while open; closing it
returns that responsibility to the background document error.
Refresh-required status starts before awaiting the stale snapshot read and belongs to
the document lifetime. Escape and reopening while that read is pending retain the
draft and show a disabled **Refreshing document…** control. A successful installation
enables a fresh preview; failed or cancelled reads retain the reachable retry path.
Read-only documents can preview but cannot apply.

An unspecified or explicit-null capacity is omitted when writing SDK JSON, including
with the default serializer outside the HTTP host. This preserves existing descriptor
JSON and historical view fingerprints. Supplied capacities, including zero, are
serialized; explicit-null input remains accepted as the legacy unconstrained state.

The initial delivery changes no plugin. Consumer adoption must publish a proven
UTF-8 capacity and retain format-specific checks. SDK-10's metadata ownership and
caption writer remain separate work.

Implementation: [TextSpec](../src/SaveToolkit.Sdk/Values/FieldSpec.cs),
[field validation](../src/SaveToolkit.Core/Editing/FieldValueCoercer.cs),
[byte counting](../web/src/lib/textCapacity.ts), and
[planned actions](../web/src/components/document/review/usePlannedAction.ts).

Primary references: [.NET optional property omission](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/ignore-properties#ignore-individual-properties),
[.NET UTF8Encoding constructor and strict fallback](https://learn.microsoft.com/dotnet/api/system.text.utf8encoding.-ctor),
[WHATWG TextEncoder algorithm](https://encoding.spec.whatwg.org/#interface-textencoder),
and [Web IDL USVString conversion](https://webidl.spec.whatwg.org/#es-USVString).
TextEncoder uses UTF-8 but converts lone surrogates to replacement characters;
the UI detects invalid scalars before counting to avoid reporting replacement bytes.
