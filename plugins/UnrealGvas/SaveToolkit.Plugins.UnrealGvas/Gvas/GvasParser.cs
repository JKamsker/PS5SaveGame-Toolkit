using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Binary;

namespace SaveToolkit.Plugins.UnrealGvas.Gvas;

internal sealed class GvasParser(byte[] bytes)
{
    private readonly List<SizeRegion> _sizes = [];
    private GvasHeader _header = null!;
    private int _nodeCount;

    public GvasFile Parse()
    {
        using var stream = new MemoryStream(bytes, writable: false);
        _header = GvasHeaderReader.Read(stream);
        var reader = new BoundedBinaryCursor(bytes, GvasBinary.Context, (int)stream.Position);
        var nodes = ReadProperties(ref reader, 0);
        // Bytes after the None terminator belong to the game and are preserved verbatim.
        return new GvasFile(bytes, _header, nodes, _sizes.ToArray());
    }

    public GvasFile ParseHeaderless(GvasHeader header)
    {
        _header = header;
        var reader = new BoundedBinaryCursor(bytes, GvasBinary.Context);
        var nodes = ReadProperties(ref reader, 0);
        return new GvasFile(bytes, header, nodes, _sizes.ToArray());
    }

    internal IReadOnlyList<GvasNode> ReadProperties(ref BoundedBinaryCursor reader, int depth)
    {
        CheckDepth(depth);
        var nodes = new List<GvasNode>();
        while (true)
        {
            var tag = GvasTag.Read(ref reader, _header);
            if (tag is null) return nodes;
            if (tag.Size > reader.Remaining) throw new SaveFormatException($"Truncated GVAS property '{tag.Name}'.");
            var start = reader.Position;
            var node = NewNode(tag.Name, tag.Type, start, tag.Size);
            if (tag.BoolOffset is { } boolOffset)
            {
                if (tag.Size != 0) throw new SaveFormatException("BoolProperty must have a zero-length payload.");
                if (!_header.CompleteTypeNames)
                {
                    var boolean = new BoundedBinaryCursor(bytes, GvasBinary.Context, boolOffset, boolOffset + 1);
                    node.Scalar = GvasPrimitives.Read(ref boolean, "BoolProperty");
                }
                else node.Scalar = new GvasScalar(GvasScalarKind.Boolean, boolOffset, 1, tag.BoolValue,
                    booleanMask: 16, booleanOriginal: bytes[boolOffset]);
            }
            else
            {
                _sizes.Add(new SizeRegion(tag.SizeOffset, start, tag.Size));
                var payload = new BoundedBinaryCursor(bytes, GvasBinary.Context, start, start + tag.Size);
                ReadPayload(node, tag, ref payload, depth + 1);
            }
            nodes.Add(node);
            reader.Position = start + tag.Size;
        }
    }

    private void ReadPayload(GvasNode node, GvasTag tag, ref BoundedBinaryCursor reader, int depth)
    {
        var sizeCheckpoint = _sizes.Count;
        try
        {
            if (tag.Native && tag.Type == "StructProperty" && !IsKnownNativeStruct(tag.StructType))
                throw new UnsupportedGvasValueException("UE5 native binary struct serialization.");
            if (tag.Type == "StructProperty")
                node.Children = ReadStruct(ref reader, tag.StructType, depth);
            else if (tag.Type == "SoftObjectProperty")
                node.Children = ReadSoftObject(ref reader, depth);
            else if (tag.Type is "ArrayProperty" or "SetProperty" or "MapProperty")
                ReadContainer(node, tag, ref reader, depth);
            else if (tag.Type == "TextProperty")
                node.Scalar = GvasPrimitives.ReadText(ref reader);
            else if (tag.Type == "ByteProperty" && tag.InnerType != "None" && tag.InnerType is not null)
                node.Scalar = GvasPrimitives.Read(ref reader, "StrProperty");
            else
                node.Scalar = GvasPrimitives.Read(ref reader, tag.Type);
            if (reader.Remaining != 0)
            {
                if (tag.Type == "StructProperty" && !IsKnownNativeStruct(tag.StructType))
                    throw new UnsupportedGvasValueException("Payload layout is not fully understood.");
                throw new GvasRejectionException($"GVAS property '{tag.Name}' size disagrees with its {tag.Type} contents ({reader.Remaining} trailing bytes).");
            }
        }
        catch (UnsupportedGvasValueException ex)
        {
            node.Scalar = null;
            node.Children = [];
            node.Note = ex.Message + " Preserved as opaque bytes; read-only.";
            _sizes.RemoveRange(sizeCheckpoint, _sizes.Count - sizeCheckpoint);
        }
    }

    private IReadOnlyList<GvasNode> ReadStruct(ref BoundedBinaryCursor reader, string? type, int depth)
    {
        CheckDepth(depth);
        if (type is "SoftObjectPath" or "SoftClassPath") return ReadSoftObject(ref reader, depth);
        var components = type switch
        {
            "Vector" => new[] { "X", "Y", "Z" },
            "Rotator" => ["Pitch", "Yaw", "Roll"],
            "Vector2D" => ["X", "Y"],
            "Quat" or "Vector4" => ["X", "Y", "Z", "W"],
            "LinearColor" or "Color" => ["R", "G", "B", "A"],
            "Guid" => ["A", "B", "C", "D"],
            "DateTime" or "Timespan" => ["Ticks"],
            _ => null,
        };
        if (components is null)
        {
            // Custom native structs may not use tagged properties. Do not infer a schema from their bytes.
            var checkpoint = _sizes.Count;
            try { return ReadProperties(ref reader, depth + 1); }
            catch (SaveFormatException)
            {
                _sizes.RemoveRange(checkpoint, _sizes.Count - checkpoint);
                throw new UnsupportedGvasValueException($"Native or unsupported struct {type}.");
            }
        }
        var primitive = type switch
        {
            "Guid" => "UInt32Property",
            "Color" => "ByteProperty",
            "DateTime" or "Timespan" => "Int64Property",
            "LinearColor" => "FloatProperty",
            _ => _header.EngineMajor >= 5 ? "DoubleProperty" : "FloatProperty",
        };
        var children = new GvasNode[components.Length];
        for (var i = 0; i < components.Length; i++)
            children[i] = ReadElement(ref reader, components[i], primitive, null, depth + 1);
        return children;
    }

    private void ReadContainer(GvasNode node, GvasTag tag, ref BoundedBinaryCursor reader, int depth)
    {
        CheckDepth(depth);
        if (tag.Type is "SetProperty" or "MapProperty")
        {
            if (reader.ReadInt32LE() != 0) throw new UnsupportedGvasValueException("Container has removed entries.");
        }
        var byteArray = tag.Type == "ArrayProperty" && tag.InnerType is "ByteProperty" or "UInt8Property";
        const int NodesPerMapEntry = 2;
        var maximum = byteArray ? GvasFile.MaxFileLength :
            MaxNodesForRemainingBudget() / (tag.Type == "MapProperty" ? NodesPerMapEntry : 1);
        int count;
        try { count = reader.ReadCount(maximum); }
        catch (SaveFormatException ex)
        {
            // A recognized container's invalid count is not evidence of an unknown native layout.
            throw new GvasRejectionException(GvasBinary.LegacyError(ex).Message);
        }
        if (tag.Type == "ArrayProperty" && tag.InnerType is "ByteProperty" or "UInt8Property")
        {
            var blob = reader.Take(count);
            node.Note = $"{count} raw bytes. {DescribeBlob(blob)}";
            return;
        }
        string? structType = tag.ElementStructType;
        SizeRegion? innerSize = null;
        if (tag.Type == "ArrayProperty" && tag.InnerType == "StructProperty" && !_header.CompleteTypeNames && _header.HasPropertyGuid)
        {
            var inner = GvasTag.Read(ref reader, _header);
            if (inner is null || inner.Type != "StructProperty") throw new SaveFormatException("Missing struct array inner tag.");
            structType = inner.StructType;
            if (inner.Size != reader.Remaining)
                throw new GvasRejectionException($"GVAS struct array '{tag.Name}' inner size differs from payload length.");
            innerSize = new SizeRegion(inner.SizeOffset, reader.Position, inner.Size);
        }
        if (tag.InnerType == "StructProperty" && structType is null)
            throw new UnsupportedGvasValueException("Container struct elements need a game-specific schema.");
        if (tag.Type == "MapProperty" && tag.ValueType == "StructProperty")
            throw new UnsupportedGvasValueException("Map struct values need a game-specific schema.");
        var children = new List<GvasNode>();
        for (var i = 0; i < count; i++)
        {
            children.Add(ReadElement(ref reader, $"[{i}]" + (tag.Type == "MapProperty" ? " Key" : ""), tag.InnerType!, structType, depth + 1));
            if (tag.Type == "MapProperty") children.Add(ReadElement(ref reader, $"[{i}] Value", tag.ValueType!, null, depth + 1));
        }
        node.Children = children;
        node.Container = new GvasContainerInfo(tag.Type, tag.InnerType!, tag.ValueType, tag.Type == "ArrayProperty" ? sizeof(int) : 2 * sizeof(int));
        if (innerSize is not null) _sizes.Add(innerSize);
    }

    private GvasNode ReadElement(ref BoundedBinaryCursor reader, string name, string type, string? structType, int depth)
    {
        CheckDepth(depth);
        var start = reader.Position;
        if (type is "StructProperty" or "SoftObjectProperty")
        {
            var children = type == "SoftObjectProperty" ? ReadSoftObject(ref reader, depth + 1) : ReadStruct(ref reader, structType, depth + 1);
            return NewNode(name, structType ?? type, start, reader.Position - start, children: children);
        }
        var scalar = type == "TextProperty" ? GvasPrimitives.ReadText(ref reader) : GvasPrimitives.Read(ref reader, type);
        return NewNode(name, type, start, reader.Position - start, scalar);
    }

    private IReadOnlyList<GvasNode> ReadSoftObject(ref BoundedBinaryCursor reader, int depth)
    {
        var names = _header.PackageVersionUe5 >= 1007
            ? new[] { "AssetPathName", "PackageName", "AssetName" }
            : ["AssetPathName", "SubPathString"];
        var children = new GvasNode[names.Length];
        for (var i = 0; i < names.Length; i++)
            children[i] = ReadElement(ref reader, names[i], "StrProperty", null, depth + 1);
        return children;
    }

    private GvasNode NewNode(string name, string type, int start, int length, GvasScalar? scalar = null, IReadOnlyList<GvasNode>? children = null)
    {
        if (++_nodeCount > GvasFile.MaxNodes) throw new GvasRejectionException("GVAS exceeds the 100,000-node safety limit.");
        return new GvasNode(name, type, start, length) { Scalar = scalar, Children = children ?? [] };
    }

    private int MaxNodesForRemainingBudget() => GvasFile.MaxNodes - _nodeCount;

    private static bool IsKnownNativeStruct(string? type) => type is "Vector" or "Rotator" or "Vector2D" or "Quat" or "Vector4"
        or "LinearColor" or "Color" or "Guid" or "DateTime" or "Timespan" or "SoftObjectPath" or "SoftClassPath";

    private static void CheckDepth(int depth)
    {
        if (depth > GvasFile.MaxDepth) throw new GvasRejectionException("GVAS exceeds the nesting safety limit.");
    }

    private static string DescribeBlob(ReadOnlySpan<byte> blob) => blob.StartsWith(new byte[] { 0xC1, 0x83, 0x2A, 0x9E })
        ? "Unreal compressed chunks (Hogwarts databases use Oodle); not decoded."
        : blob.Length >= 2 && blob[0] == 0x78 ? "Possible zlib payload; not decoded." : "Opaque byte array.";
}

internal sealed class UnsupportedGvasValueException(string message) : Exception(message);
