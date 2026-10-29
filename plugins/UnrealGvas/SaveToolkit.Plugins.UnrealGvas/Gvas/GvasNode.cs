using System.Buffers.Binary;

namespace SaveToolkit.Plugins.UnrealGvas.Gvas;

public enum GvasScalarKind { Boolean, Signed, Unsigned, Float, Double, String }

public sealed record GvasContainerInfo(string Kind, string ElementType, string? ValueType, int PrefixBytes);

/// <summary>A scalar retains its exact original encoding until its value changes.</summary>
public sealed class GvasScalar(GvasScalarKind kind, int offset, int length, object value, bool wide = false,
    byte booleanMask = 1, byte booleanOriginal = 0)
{
    private readonly object _original = value;
    public GvasScalarKind Kind { get; } = kind;
    public int Offset { get; } = offset;
    public int Length { get; } = length;
    public object Value { get; set; } = value;
    public bool Changed => !Value.Equals(_original);
    internal bool IsOriginalValue(object candidate) => candidate.Equals(_original);

    internal byte[] Encode()
    {
        if (Kind == GvasScalarKind.String) return GvasBinary.EncodeString((string)Value, wide);
        var result = new byte[Length];
        switch (Kind)
        {
            case GvasScalarKind.Boolean:
                result[0] = (byte)((booleanOriginal & ~booleanMask) | ((bool)Value ? booleanMask : 0));
                break;
            case GvasScalarKind.Float: BinaryPrimitives.WriteSingleLittleEndian(result, checked((float)(double)Value)); break;
            case GvasScalarKind.Double: BinaryPrimitives.WriteDoubleLittleEndian(result, (double)Value); break;
            default:
                var integer = (Int128)Value;
                var bits = Length * 8;
                var min = Kind == GvasScalarKind.Signed ? -(Int128.One << (bits - 1)) : 0;
                var max = Kind == GvasScalarKind.Signed ? (Int128.One << (bits - 1)) - 1 : (Int128.One << bits) - 1;
                if (integer < min || integer > max) throw new ArgumentOutOfRangeException(nameof(Value));
                for (var i = 0; i < Length; i++) result[i] = (byte)((integer >> (i * 8)) & byte.MaxValue);
                break;
        }
        return result;
    }
}

public sealed class GvasNode(string name, string type, int offset, int length)
{
    public string Name { get; } = name;
    public string Type { get; } = type;
    public int Offset { get; } = offset;
    public int Length { get; } = length;
    public GvasScalar? Scalar { get; internal set; }
    public IReadOnlyList<GvasNode> Children { get; internal set; } = [];
    public string? Note { get; internal set; }
    public GvasContainerInfo? Container { get; internal set; }
}

public sealed record GvasHeader(int SaveVersion, int PackageVersion, int? PackageVersionUe5,
    ushort EngineMajor, ushort EngineMinor, ushort EnginePatch, uint EngineBuild,
    string EngineBranch, int CustomVersionCount, string SaveClass)
{
    public bool HasPropertyGuid => EngineMajor >= 5 || (EngineMajor == 4 && EngineMinor >= 12);
    public bool CompleteTypeNames => EngineMajor > 5 || (EngineMajor == 5 && EngineMinor >= 4);
}

internal sealed record SizeRegion(int SizeOffset, int Start, int Length);
internal sealed record BytePatch(int Offset, int Length, byte[] Bytes);
