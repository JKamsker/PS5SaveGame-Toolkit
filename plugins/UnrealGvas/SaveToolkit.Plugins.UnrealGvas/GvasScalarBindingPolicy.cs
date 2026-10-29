using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.UnrealGvas;

/// <summary>Creates the one canonical binding policy shared by rendered and metadata-only fields.</summary>
internal sealed record GvasScalarBinding(
    FieldSpec Spec,
    Func<FieldValue> Get,
    Action<FieldValue>? Set,
    Func<FieldValue, string?> Validate);

internal static class GvasScalarBindingPolicy
{
    public static GvasScalarBinding Create(GvasNode node, bool canEdit, bool integrityNode, bool actorLength)
    {
        var scalar = node.Scalar ?? throw new ArgumentException("The node is not scalar.", nameof(node));
        var writable = canEdit && !integrityNode && !actorLength;
        var spec = GvasScalarField.Spec(scalar);
        if (!writable) spec = spec with { ReadOnly = true };

        return new GvasScalarBinding(
            spec,
            () => GvasScalarField.ToFieldValue(scalar),
            writable ? value => GvasScalarField.SetScalar(scalar, value) : null,
            value => GvasScalarField.Validate(scalar, value));
    }
}
