# Canonical field presentations (SDK-08)

Use `FieldReferenceNode` when the same saved scalar belongs in several views. It presents the
canonical editable field; it does not register another binding, publish another spec, or select a
record from a reference catalog. A catalog-valued field still uses `ReferenceSpec` and its existing picker.

## Register once, present in several places

The canonical binding owns validation, read-only policy, evidence and warnings. A reference supplies
only the direct canonical value ID and optional contextual label/description:

```csharp
RegisterField(new FieldMetadata("settings/hdr", "HDR brightness", new FloatSpec { Min = 0, Max = 2 })
{
    Presentation = new FieldPresentation
    {
        Confidence = Confidence.Inferred,
        Warning = "Experimental setting; gameplay effect is unverified.",
        Description = "Canonical storage interpretation and attribution.",
    },
}, () => FieldValue.Of(model.Brightness), value => model.Brightness = value.AsDouble());

ViewNode projection = new FieldReferenceNode("settings/hdr")
{
    Label = "Display brightness",
    Description = "Context for this settings table.",
};
```

The owning document explicitly implements `IFieldMetadataDocument`. Its targeted resolver must
register **current-model** getters/setters after structural restoration and return only requested
descriptors. `BoundSaveDocument.ResolveFieldMetadata` can then return those registrations. Merely
inheriting `BoundSaveDocument` does not opt in. Forward this capability through wrappers/groups;
an inner document implementing it does not automatically opt its wrapper in.

`FieldMetadata.Presentation` declares the **complete canonical annotations**, including confidence,
warnings and attribution. `new FieldPresentation()` explicitly means there are no annotations;
`null` means they are unspecified. For an unloaded target, unspecified annotations leave the
reference unavailable rather than constructing an editable field that could omit a critical warning.
When a canonical `FieldNode` has already been loaded, its annotations can supply the fallback.
Explicit presentation metadata is authoritative; the host rejects conflicts with learned canonical
annotations rather than silently dropping a warning or changing confidence.

Existing plugins and `FieldNode` callers keep their behavior. A partially metadata-aware document
can also reference a loaded, view-only field. A previously registered target cannot fall back to a
stale view when its descriptor disappears.

## Host lifecycle and wire contract

[FieldReferenceNode](../src/SaveToolkit.Sdk/Views/FieldReferenceNode.cs) is registered under the
`fieldReference` discriminator. Plugin-produced JSON contains only:

```json
{"type":"fieldReference","targetValueId":"settings/hdr","label":"Display brightness","description":"Settings context"}
```

There is no projection binding ID, value, spec, read-only flag, confidence override, or target node.
IDs must be nonblank and at most 4,096 characters. Labels, when supplied, must be nonblank and at
most 4,096 characters; descriptions are limited to 4,096 characters. Deserialization and indexing
reject invalid references. Unknown properties, including attempted spec/value/permission overrides,
are rejected on this node. The generated OpenAPI discriminated union includes its string bounds.

After indexing canonical fields, Core resolves references at the document view, lazy expansion and
bounded collection-page boundaries. Resolution batches distinct target IDs through the existing
200-ID metadata lifecycle. It never expands hidden sections or fetches other collection pages.
Reference trees have a 100,000-node, 128-level traversal budget and 10,000 distinct-target budget;
ordinary views without references keep their existing behavior and node identity.
The host measures the **normalized** UTF-8 JSON, including canonical specs, full values, warnings,
descriptions and escaping, against a 2 MiB budget for reference-bearing views/expansions/pages.
Oversized canonical targets or combined presentations fail with HTTP 413 before partial output;
the existing collection envelope independently enforces its full 2 MiB cap. Values are never clipped.
Consumers must retain a bounded text/detail fallback for very long scalars rather than presenting
hundreds of large editable strings merely because the provider's unresolved references are small.

An available reference becomes an ordinary `field` wire node with the canonical value ID, **the
same resolved spec and live value** as every other presentation, and canonical evidence/warnings.
Its optional label changes only the caption. Its description is appended to the canonical
description, preserving attribution. Targets are resolved once per outgoing tree, not once per
projection. Metadata validation may independently probe bindings under its existing contract.
The resolved presentation is never re-indexed as a new canonical publication.

Unknown, retired, unannotated unloaded, or no-longer-live targets remain `fieldReference` nodes.
The browser shows useful unavailable text with no editor or mutation fallback. Loading and failed
requests continue using the existing document/section/table loading, retry and stale-revision flows;
references add no independent asynchronous registry. Opening the canonical view and refreshing can
make a legacy loaded-only reference available. Presentation nodes have no identities of their own,
so references cannot chain or form reference cycles.

Eager table cells, nested tabs/tables, lazy sections and F8 query pages use the same normalization.
`DocumentMemberAdapter` qualifies `TargetValueId` with the same member policy as `FieldNode.ValueId`,
including opaque IDs. Group metadata discovery remains the group's explicit capability/policy.
Structural history rebuilds discard old presentations, re-resolve remembered expansions, retire
missing canonical bindings and rebind restored targets on undo/redo.

The browser reuses `FieldRow`, `FieldControl`, table cells and `DocumentStore`: each mounted control
gets a distinct React DOM ID and contextual label while commits, errors, dirty markers and history
remain keyed to the canonical value ID. Document and canonical spec read-only policy still apply.
Opening projections neither edits bytes nor creates undo entries. Rejected/pending drafts retain
the existing submitting-control diagnostic ownership rules.

## Concrete consumer and adoption boundary

UnrealGvas currently renders `GvasPropertyCollection` value cells as text, while Properties/Search
owns canonical scalar bindings. A concrete adoption is Jedi `HDRBrightness` in Search,
`gvas/properties`, and `jedi/settings`. An adopter can replace **eligible scalar value cells** with
references to those existing raw IDs after exposing targeted raw metadata and forwarding it through
`GvasQueryableDocument`. Preserve `UnknownIntegrityWarning`/`EditWarning`, confidence, exact specs and
existing read-only restrictions; becoming editable in named views is an intentional presentation
change requiring plugin tests. Actor tags/strings without canonical bindings must remain text.

This framework change does not migrate plugins, establish new native writers, prove gameplay
meaning, or deduplicate independent cross-ID aliases. F13's explicit canonical-alias contract is
separate. SDK-08 consumer adoption remains pending until that plugin work lands.

## Evidence and sources

- [UnrealGvas request](../plugins/UnrealGvas/README.md#sdk-requests),
  [current property projection](../plugins/UnrealGvas/SaveToolkit.Plugins.UnrealGvas/GvasPropertyCollection.cs),
  [raw canonical bindings](../plugins/UnrealGvas/SaveToolkit.Plugins.UnrealGvas/UnrealGvasDocument.cs).
- [Core normalization](../src/SaveToolkit.Core/Editing/FieldReferenceProjection.cs),
  [lifecycle tests](../tests/SaveToolkit.Core.Tests/FieldReferenceSessionTests.cs),
  [annotation tests](../tests/SaveToolkit.Core.Tests/FieldReferencePresentationTests.cs),
  [browser interaction test](../web/e2e/field-reference.e2e.ts).
- Microsoft primary documentation:
  [polymorphic serialization](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/polymorphism),
  [rejecting unmapped JSON members](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/missing-members).
