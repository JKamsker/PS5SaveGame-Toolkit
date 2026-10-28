using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Insomniac;

internal sealed record DdlField(string Name, DdlValueKind Kind, int Count, int Offset,
    IReadOnlyList<FieldValue> Values)
{
    internal FieldSpec Spec => Kind switch
    {
        DdlValueKind.UInt8 => IntegerSpec.UInt8,
        DdlValueKind.UInt16 => IntegerSpec.UInt16,
        DdlValueKind.UInt32 => IntegerSpec.UInt32,
        DdlValueKind.UInt64 or DdlValueKind.Identifier or DdlValueKind.Asset => IntegerSpec.UInt64,
        DdlValueKind.Int8 => IntegerSpec.Int8,
        DdlValueKind.Int16 => IntegerSpec.Int16,
        DdlValueKind.Int32 => IntegerSpec.Int32,
        DdlValueKind.Int64 => IntegerSpec.Int64,
        DdlValueKind.Float => FloatSpec.Single,
        DdlValueKind.Double => FloatSpec.Double,
        DdlValueKind.Bool => new BoolSpec(),
        _ => new TextSpec(),
    };
}
