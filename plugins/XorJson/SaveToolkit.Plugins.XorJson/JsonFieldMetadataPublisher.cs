using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.XorJson;

/// <summary>Publishes only requested current scalar bindings, independently of any view or projection.</summary>
internal static class JsonFieldMetadataPublisher
{
    public static void Publish(JsonEditModel model, IReferenceContext references, bool canEdit,
        string writeWarning, IReadOnlyList<string> valueIds,
        Action<FieldMetadata, Func<FieldValue>, Action<FieldValue>?, Func<FieldValue, string?>> register)
    {
        JsonMetadataRequest.Validate(valueIds);
        foreach (var valueId in valueIds.Distinct(StringComparer.Ordinal))
        {
            if (!JsonActions.TryFind(model, valueId, out var node) || node.IsContainer) continue;
            var binding = JsonScalarBindingPolicy.Create(node, references, canEdit, writeWarning);
            var spec = FieldSpecMetadata.Snapshot(binding.Spec);
            var index = node.Index;
            register(new FieldMetadata(node.ValueId, binding.Label, spec)
            {
                BindingKind = FieldBindingKind.Primary,
                CanonicalValueId = node.ValueId,
                Presentation = binding.Presentation,
            }, () => model.Find(index).Current,
                canEdit && !spec.ReadOnly ? value => model.Find(index).Current = value : null,
                binding.Validate);
        }
    }
}
