using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.UnrealGvas;

internal static class GvasScalarField
{
    public static FieldSpec Spec(GvasScalar scalar) => scalar.Kind switch
    {
        GvasScalarKind.Boolean => new BoolSpec(),
        GvasScalarKind.String => new TextSpec { MaxLength = GvasFile.MaxStringLength - 1 },
        GvasScalarKind.Float => FloatSpec.Single,
        GvasScalarKind.Double => FloatSpec.Double,
        GvasScalarKind.Signed => new IntegerSpec(-(Int128.One << (scalar.Length * 8 - 1)), (Int128.One << (scalar.Length * 8 - 1)) - 1),
        _ => new IntegerSpec(0, (Int128.One << (scalar.Length * 8)) - 1),
    };

    public static FieldValue ToFieldValue(GvasScalar scalar) => scalar.Kind switch
    {
        GvasScalarKind.Boolean => FieldValue.Of((bool)scalar.Value),
        GvasScalarKind.String => FieldValue.Of((string)scalar.Value),
        GvasScalarKind.Float or GvasScalarKind.Double => FieldValue.Of((double)scalar.Value),
        _ => FieldValue.Of((Int128)scalar.Value),
    };

    public static string? Validate(GvasScalar scalar, FieldValue value)
    {
        try
        {
            var candidate = new GvasScalar(scalar.Kind, scalar.Offset, scalar.Length, scalar.Value);
            SetScalar(candidate, value);
            _ = candidate.Encode();
            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException or InvalidOperationException) { return ex.Message; }
    }

    public static void SetScalar(GvasScalar scalar, FieldValue value)
    {
        object next = scalar.Kind switch
        {
            GvasScalarKind.Boolean => value.AsBool(),
            GvasScalarKind.String => value.AsString(),
            GvasScalarKind.Float => (double)(float)value.AsDouble(),
            GvasScalarKind.Double => value.AsDouble(),
            _ => value.AsInteger(),
        };
        if (next is double number && !double.IsFinite(number)) throw new ArgumentException("Floating point value must be finite.");
        scalar.Value = next;
    }
}
