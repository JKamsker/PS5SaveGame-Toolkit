---
title: Project one canonical field into several views
description: Reuse one binding across projections while retaining metadata, confidence and safe unavailable states.
---

# Project one canonical field into several views

`FieldReferenceNode` is a presentation instruction: it names one canonical `valueId` and never
creates another binding. The host resolves the target from a loaded field or from targeted
`IFieldMetadataDocument` discovery. An available reference becomes an ordinary `FieldNode`; an
unresolved target remains an unavailable reference. This keeps a summary, table cell and review
row on one live value and one write policy.

The contract is shipped in `SaveToolkit.Sdk`, and the production UnrealGvas consumer is now on
`main`. Non-generic Jedi/Sackboy raw, named and eager-search scalar projections emit these
references; generic raw summaries, wide strings, actor observations and opaque values retain their
bounded/read-only fallbacks. The framework and consumer tests are the runnable examples for this
page:

- [`FieldReferenceContractTests.cs`](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/tests/SaveToolkit.Core.Tests/FieldReferenceContractTests.cs)
- [`FieldReferencePresentationTests.cs`](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/tests/SaveToolkit.Core.Tests/FieldReferencePresentationTests.cs)
- [`GvasFieldReferenceTests.cs`](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/plugins/UnrealGvas/SaveToolkit.Plugins.UnrealGvas.Tests/GvasFieldReferenceTests.cs)

## Emit a reference, never a second field

Only the target ID and optional contextual presentation strings cross the wire. The reference
cannot carry a spec, value, `valueId`, `readOnly` flag or another reference target.

```csharp
ViewNode node = new FieldReferenceNode("raw/opaque~雪")
{
    Label = "Named field",
    Description = "Context",
};

var json = JsonSerializer.Serialize(node, ToolkitJson.Options);
Assert.Equal(
    "{\"type\":\"fieldReference\",\"targetValueId\":\"raw/opaque~\\u96EA\",\"label\":\"Named field\",\"description\":\"Context\"}",
    json);
```

`TargetValueId` is 1–4096 characters. `Label` and `Description` are each at most 4096
characters; a supplied label cannot be blank. Unknown properties and permission-bearing properties
are rejected. IDs are opaque: do not synthesize a path from an ID prefix or infer a writable field
from a reference's label.

## Publish canonical metadata once

Implement `IFieldMetadataDocument` when a target may be unloaded. Resolve only requested IDs, in
batches of at most `FieldMetadataLimits.MaxBatchSize` (200 including duplicates), and re-resolve
after structural changes, restores and group publication. A `FieldMetadata` record owns the
canonical `Label`, `Spec`, `BindingKind`, optional real `Path`, and optional `FieldPresentation`.

```csharp
RegisterField(new FieldMetadata(
    "item/stable-key/quantity", "Stored quantity", new IntegerSpec(0, int.MaxValue)),
    () => FieldValue.Of(item.Quantity),
    value => item.Quantity = value.AsInt32(),
    ValidateQuantity);

RegisterField(new FieldMetadata(
    "inventory/total", "Total quantity", IntegerSpec.Int32 with { ReadOnly = true })
    { BindingKind = FieldBindingKind.Derived },
    () => FieldValue.Of(items.Sum(item => item.Quantity)));
```

This registration pattern is copied from the authoritative SDK-02 example in
[`PLUGIN_GUIDE.md`](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/PLUGIN_GUIDE.md#field-metadata-without-rendering-a-page-sdk-02).
Derived fields must be read-only and have no independent setter. A primary setter is the only
write path; derived values are reported as consequences and recomputed atomically.

`FieldPresentation` carries exactly the canonical `Confidence`, `Warning` and `Description`.
`Presentation = null` means “unspecified”: an unloaded reference must wait for a loaded canonical
field before exposing an editor. `new FieldPresentation()` means “explicitly no annotations”. A
projection may append its contextual description, but it cannot replace canonical warning,
confidence or description, promote confidence, or make a read-only derived field writable.

## Render the four target states

The renderer and host preserve a bounded state machine:

| Target state | User-facing result | Write rule |
| --- | --- | --- |
| Loaded and available | Ordinary `FieldNode` with canonical spec/value and optional projection label | Commit only through the canonical primary binding |
| Unloaded but metadata-resolvable | Ordinary `FieldNode` after targeted discovery; keep canonical warning and evidence | Same primary binding; no view expansion is required |
| Missing, deleted or unavailable | Unresolved `FieldReferenceNode` and a clear unavailable message | No edit, revert or guessed placeholder |
| Read-only or derived | Value with explicit read-only reason; derived explanation may say it follows the primary | No direct write; update the primary and recompute |

Loading and errors are host states, not fake values: keep the row stable, announce “Loading value”
or the error once, and offer refresh/retry. Do not render `0`, an empty string or a disabled editor
as a substitute for missing data.

## Keep normalized output bounded

References in views, lazy expansions and collection pages are normalized to ordinary fields before
HTTP delivery. The complete normalized value, annotations and combined projections are bounded to
2 MiB; an oversized root or page returns 413 without partial output. Collections still render only
their requested bounded page. A plugin or other consumer must choose a bounded text/detail fallback
for very long scalars **before** emitting references. The host then normalizes the complete
canonical values, specs and annotations, measures the combined 2 MiB budget, and returns 413 without
partial output when it is exceeded. It never clips a canonical `FieldValue` or automatically
replaces an oversized reference. The consumer's fallback must preserve identity, provenance,
search/filter/sort behavior and genuine canonical values. See the authoritative
[`FIELD_REFERENCES.md` budget guidance](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/FIELD_REFERENCES.md#host-lifecycle-and-wire-contract).

Changing a projection label, selecting a row, opening a details panel or navigating to a field must
not mutate the canonical value. The existing field control's commit, or an explicitly reviewed
action, is the only write. Group adapters prefix the canonical target ID for the owning member;
they do not create a second binding.

## Adoption status

SDK-08 is **resolved** in the central ledger for the shipped UnrealGvas consumer. Its tests cover
genuine projections, unavailable structural targets, canonical annotations, shared live edits and
the bounded wide-string fallback. This does not establish gameplay semantics for other titles or
close the separate SDK-06, SDK-07 or SDK-10 adoption/evidence gates.

Continue with [working with saves](../guides/working-with-saves.md) to review and revert changes,
or return to [the first-plugin walkthrough](first-plugin.md).
