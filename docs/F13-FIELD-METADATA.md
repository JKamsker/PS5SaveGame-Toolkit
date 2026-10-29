# F13 field metadata publishing

F13 lets a plugin publish bounded field metadata independently of a rendered view. Implement
`IFieldMetadataDocument` and use `BoundSaveDocument.RegisterField(FieldMetadata, ...)` for every
published binding. A registration supplies the stable `ValueId`, canonical label, `FieldSpec`,
getter, optional setter and optional navigation `Path`; it does not build or expand a view.

## Canonical aliases

`FieldMetadata.CanonicalValueId` defaults to the field's own ID. A plugin may set it to a different
ID only for a read-only `Derived` field. The target must be a published writable `Primary` field,
and its type constraints must agree with the alias after the read-only projection is applied. The
host rejects missing targets, cycles, contradictory specs, writable derived bindings and invalid
metadata before an edit or action plan reaches the plugin. Equal values, labels or specs never
create an alias.

Undo and redo replay each canonical primary once. Derived aliases remain reported consequences and
are recomputed by the plugin's atomic setter. Structural restores clear bindings; the next
metadata resolution always targets the restored candidate model.

## Presentation annotations

`FieldMetadata.Presentation` is optional. A null value means the plugin has not declared annotation
coverage for an unloaded reference. A non-null `FieldPresentation`, including an empty instance,
declares coverage and can carry `Confidence`, `Warning` and `Description`. The host never invents
`Verified` confidence or permission from these annotations. Rendered `FieldNode` annotations remain
authoritative when a view is available.

## Groups and semantic review

`DocumentMemberAdapter.ResolveFieldMetadata` qualifies both `ValueId` and `CanonicalValueId` and
maps a published navigation path through the member identity policy. `DocumentMemberRouter` batches
mixed requests after validating every owner. Group staging forwards to the currently published
candidate, so restores cannot retain member setters from an earlier publication.

When an edit or planned action explicitly affects an unloaded published ID, semantic review captures
its original value and metadata from the pinned original document. Labels and real paths are retained
in the change log. Missing, contradictory or over-budget descriptors make coverage incomplete and
never appear as an invented unchanged value. Resolution is batched at
`FieldMetadataLimits.MaxBatchSize` (200 IDs); no whole-document enumeration occurs.

Legacy documents that do not implement `IFieldMetadataDocument` retain their existing view-walk and
replay behavior.
