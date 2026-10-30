---
title: API reference
---

# API reference

Both references are rebuilt from the checked-out source with every documentation build. Neither requires uploading a save or running the editor on this website.

## Plugin SDK · C#

The SDK reference comes from the current `SaveToolkit.Sdk` assembly and its XML documentation. Start with these contracts:

| Contract | Purpose |
| --- | --- |
| <xref:SaveToolkit.Sdk.IGamePlugin> | Recognize a file and open a document. |
| <xref:SaveToolkit.Sdk.ISaveDocument> | Describe, edit and serialize a document. |
| <xref:SaveToolkit.Sdk.Views.ViewNode> | Describe the abstract view rendered by the frontend. |
| <xref:SaveToolkit.Sdk.Values.FieldSpec> | Publish a field's type and constraints. |
| <xref:SaveToolkit.Sdk.BoundSaveDocument> | Register in-memory getter/setter bindings. |
| <xref:SaveToolkit.Sdk.Collections.IQueryableDocument> | Expose bounded collection queries. |
| <xref:SaveToolkit.Sdk.References.IReferenceContext> | Use document-pinned reference data. |

The [first plugin tutorial](../tutorials/first-plugin.md) connects these types in a working example. The [plugin guide](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/PLUGIN_GUIDE.md) owns contribution and data-integrity rules.

Shipped preservation and projection contracts have focused tutorials tied to committed tests:
[lossless JSON](../tutorials/lossless-json.md), [canonical field references](../tutorials/field-references.md),
and the [ParamSfo substrate](../tutorials/sfo-substrate.md). The generated pages for
`LosslessJsonDocument`, `FieldReferenceNode`, `FieldPresentation`, and `ParamSfoDocument` remain
the exact member reference. The UnrealGvas consumer for SDK-08 is now on `main`: eligible
non-generic Jedi/Sackboy scalar projections use canonical references, while generic summaries,
wide strings, actor observations and opaque values retain bounded/read-only fallbacks.
GenericJson and XorJson use the lossless JSON substrate, FF7 supplies semantic preset adoption,
and SuckerPunch supplies the manual Tsushima caption consumer. Automatic gameplay-to-caption
synchronization and Gearbox production action-effect adoption remain outstanding.

## Save-editing journeys and their contracts

These links connect the current user workflows to generated SDK members. The HTTP reference
below covers host preset-library transfer; transfer is separate from applying a preset to a save.

| Journey | Generated SDK contracts |
| --- | --- |
| [Save and transfer presets](../guides/presets-and-transfer.md) | <xref:SaveToolkit.Sdk.IPresetDocument>, <xref:SaveToolkit.Sdk.PresetCapability>, <xref:SaveToolkit.Sdk.PresetInstanceResolutionStatus> |
| [Review canonical fields and aliases](../guides/metadata-review.md) | <xref:SaveToolkit.Sdk.IFieldMetadataDocument>, <xref:SaveToolkit.Sdk.FieldMetadata>, <xref:SaveToolkit.Sdk.FieldPresentation> |
| [Edit a manual caption and export owned metadata](../guides/manual-caption.md) | <xref:SaveToolkit.Sdk.IAdditionalOutputOwner>, <xref:SaveToolkit.Sdk.Saves.ParamSfoDocument>, <xref:SaveToolkit.Sdk.Values.TextSpec> |
| [Preview actions and effects](../guides/action-effects.md) | <xref:SaveToolkit.Sdk.IActionEffectDocument>, <xref:SaveToolkit.Sdk.ActionEffectDescriptor> |

Typed effects have host/test-provider and synthetic renderer evidence today. These reference
pages describe the contract; their presence does not establish production Gearbox adoption.

## Local HTTP API · OpenAPI 3.1

Open the [rendered HTTP reference](http.md) for operation parameters, request bodies, response statuses and schemas. Its schema is exported from the current API's `/openapi/v1.json` using empty, isolated workspace and reference-pack roots, then compared with the existing reviewed contract. Only the variable server origin is replaced with the local default.

The reference is read-only documentation. Run your own server at `http://127.0.0.1:5180` when writing a client. The [HTTP behavior guide](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/API.md) explains sessions, revisions, errors, export and other behavioral rules that need more context than a schema.
