using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.UnrealGvas.Gvas;

internal sealed class GvasParser(byte[] bytes)
{
    private readonly List<SizeRegion> _sizes = [];
    private GvasHeader _header = null!;
    private int _nodeCount;

    public GvasFile Parse()
    {
        var reader = new GvasCursor(bytes);
        if (!reader.Take(4).SequenceEqual("GVAS"u8)) throw new SaveFormatException("Not a GVAS SaveGame file.");
        var version = reader.Int32();
        if (version is < 1 or > 3) throw new SaveFormatException($"Unsupported GVAS save version {version}.");
        var package = reader.Int32();
        int? package5 = version >= 3 ? reader.Int32() : null;
        var major = reader.UInt16();
        var minor = reader.UInt16();
        var patch = reader.UInt16();
        var build = reader.UInt32();
        var branch = reader.String();
        if (major is < 4 or > 5) throw new SaveFormatException($"Unsupported Unreal engine version {major}.{minor}.");
        var count = 0;
        if (major >= 5 || minor >= 12)
        {
            var format = reader.Int32();
            if (format != 3) throw new SaveFormatException($"Unsupported custom version format {format}; expected GUID format 3.");
            count = reader.Count();
            reader.Take(checked(count * 20));
        }
        var saveClass = reader.String();
        _header = new GvasHeader(version, package, package5, major, minor, patch, build, branch, count, saveClass);
        if (_header.CompleteTypeNames)
        {
            if (reader.Byte() != 0) throw new SaveFormatException("Unsupported UE5 root serialization control flags.");
        }
        var nodes = ReadProperties(reader, 0);
        // Bytes after the None terminator belong to the game and are preserved verbatim.
        return new GvasFile(bytes, _header, nodes, _sizes.ToArray());
    }

    public GvasFile ParseHeaderless(GvasHeader header)
    {
        _header = header;
        var nodes = ReadProperties(new GvasCursor(bytes), 0);
        return new GvasFile(bytes, header, nodes, _sizes.ToArray());
    }

    internal IReadOnlyList<GvasNode> ReadProperties(GvasCursor reader, int depth)
    {
        CheckDepth(depth);
        var nodes = new List<GvasNode>();
        while (true)
        {
            var tag = GvasTag.Read(reader, _header);
            if (tag is null) return nodes;
            if (tag.Size > reader.Remaining) throw new SaveFormatException($"Truncated GVAS property '{tag.Name}'.");
            var start = reader.Position;
            var node = NewNode(tag.Name, tag.Type, start, tag.Size);
            if (tag.BoolOffset is { } boolOffset)
            {
                if (tag.Size != 0) throw new SaveFormatException("BoolProperty must have a zero-length payload.");
                if (!_header.CompleteTypeNames)
                    node.Scalar = GvasPrimitives.Read(new GvasCursor(bytes, boolOffset, boolOffset + 1), "BoolProperty");
                else node.Scalar = new GvasScalar(GvasScalarKind.Boolean, boolOffset, 1, tag.BoolValue,
                    booleanMask: 16, booleanOriginal: bytes[boolOffset]);
            }
            else
            {
                _sizes.Add(new SizeRegion(tag.SizeOffset, start, tag.Size));
                ReadPayload(node, tag, new GvasCursor(bytes, start, start + tag.Size), depth + 1);
            }
            nodes.Add(node);
            reader.Position = start + tag.Size;
        }
    }

    private void ReadPayload(GvasNode node, GvasTag tag, GvasCursor reader, int depth)
    {
        var sizeCheckpoint = _sizes.Count;
        try
        {
            if (tag.Native && tag.Type == "StructProperty" && !IsKnownNativeStruct(tag.StructType))
                throw new UnsupportedGvasValueException("UE5 native binary struct serialization.");
            if (tag.Type == "StructProperty")
                node.Children = ReadStruct(reader, tag.StructType, depth);
            else if (tag.Type == "SoftObjectProperty")
                node.Children = ReadSoftObject(reader, depth);
            else if (tag.Type is "ArrayProperty" or "SetProperty" or "MapProperty")
                ReadContainer(node, tag, reader, depth);
            else if (tag.Type == "TextProperty")
                node.Scalar = GvasPrimitives.ReadText(reader);
            else if (tag.Type == "ByteProperty" && tag.InnerType != "None" && tag.InnerType is not null)
                node.Scalar = GvasPrimitives.Read(reader, "StrProperty");
            else
                node.Scalar = GvasPrimitives.Read(reader, tag.Type);
            if (reader.Remaining != 0) throw new UnsupportedGvasValueException("Payload layout is not fully understood.");
        }
        catch (UnsupportedGvasValueException ex)
        {
            node.Scalar = null;
            node.Children = [];
            node.Note = ex.Message + " Preserved as opaque bytes; read-only.";
            _sizes.RemoveRange(sizeCheckpoint, _sizes.Count - sizeCheckpoint);
        }
    }

    private IReadOnlyList<GvasNode> ReadStruct(GvasCursor reader, string? type, int depth)
    {
        CheckDepth(depth);
        if (type is "SoftObjectPath" or "SoftClassPath") return ReadSoftObject(reader, depth);
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
            try { return ReadProperties(reader, depth + 1); }
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
        return components.Select(name => ReadElement(reader, name, primitive, null, depth + 1)).ToArray();
    }

    private void ReadContainer(GvasNode node, GvasTag tag, GvasCursor reader, int depth)
    {
        CheckDepth(depth);
        if (tag.Type is "SetProperty" or "MapProperty")
        {
            if (reader.Int32() != 0) throw new UnsupportedGvasValueException("Container has removed entries.");
        }
        var byteArray = tag.Type == "ArrayProperty" && tag.InnerType is "ByteProperty" or "UInt8Property";
        var count = reader.Count(byteArray ? GvasFile.MaxFileLength : MaxNodesForRemainingBudget());
        if (tag.Type == "ArrayProperty" && tag.InnerType is "ByteProperty" or "UInt8Property")
        {
            reader.Take(count);
            node.Note = $"{count} raw bytes. {DescribeBlob(reader.Data.Span.Slice(node.Offset + 4, count))}";
            return;
        }
        string? structType = tag.ElementStructType;
        SizeRegion? innerSize = null;
        if (tag.Type == "ArrayProperty" && tag.InnerType == "StructProperty" && !_header.CompleteTypeNames && _header.HasPropertyGuid)
        {
            var inner = GvasTag.Read(reader, _header);
            if (inner is null || inner.Type != "StructProperty") throw new SaveFormatException("Missing struct array inner tag.");
            structType = inner.StructType;
            if (inner.Size != reader.Remaining) throw new UnsupportedGvasValueException("Struct array inner size differs from payload length.");
            innerSize = new SizeRegion(inner.SizeOffset, reader.Position, inner.Size);
        }
        if (tag.InnerType == "StructProperty" && structType is null)
            throw new UnsupportedGvasValueException("Container struct elements need a game-specific schema.");
        if (tag.Type == "MapProperty" && tag.ValueType == "StructProperty")
            throw new UnsupportedGvasValueException("Map struct values need a game-specific schema.");
        var children = new List<GvasNode>();
        for (var i = 0; i < count; i++)
        {
            children.Add(ReadElement(reader, $"[{i}]" + (tag.Type == "MapProperty" ? " Key" : ""), tag.InnerType!, structType, depth + 1));
            if (tag.Type == "MapProperty") children.Add(ReadElement(reader, $"[{i}] Value", tag.ValueType!, null, depth + 1));
        }
        node.Children = children;
        node.Container = new GvasContainerInfo(tag.Type, tag.InnerType!, tag.ValueType, tag.Type == "ArrayProperty" ? sizeof(int) : 2 * sizeof(int));
        if (innerSize is not null) _sizes.Add(innerSize);
    }

    private GvasNode ReadElement(GvasCursor reader, string name, string type, string? structType, int depth)
    {
        CheckDepth(depth);
        var start = reader.Position;
        if (type is "StructProperty" or "SoftObjectProperty")
        {
            var children = type == "SoftObjectProperty" ? ReadSoftObject(reader, depth + 1) : ReadStruct(reader, structType, depth + 1);
            return NewNode(name, structType ?? type, start, reader.Position - start, children: children);
        }
        var scalar = type == "TextProperty" ? GvasPrimitives.ReadText(reader) : GvasPrimitives.Read(reader, type);
        return NewNode(name, type, start, reader.Position - start, scalar);
    }

    private IReadOnlyList<GvasNode> ReadSoftObject(GvasCursor reader, int depth)
    {
        var names = _header.PackageVersionUe5 >= 1007
            ? new[] { "AssetPathName", "PackageName", "AssetName" }
            : ["AssetPathName", "SubPathString"];
        return names.Select(name => ReadElement(reader, name, "StrProperty", null, depth + 1)).ToArray();
    }

    private GvasNode NewNode(string name, string type, int start, int length, GvasScalar? scalar = null, IReadOnlyList<GvasNode>? children = null)
    {
        if (++_nodeCount > GvasFile.MaxNodes) throw new SaveFormatException("GVAS exceeds the 100,000-node safety limit.");
        return new GvasNode(name, type, start, length) { Scalar = scalar, Children = children ?? [] };
    }

    private int MaxNodesForRemainingBudget() => GvasFile.MaxNodes - _nodeCount;

    private static bool IsKnownNativeStruct(string? type) => type is "Vector" or "Rotator" or "Vector2D" or "Quat" or "Vector4"
        or "LinearColor" or "Color" or "Guid" or "DateTime" or "Timespan" or "SoftObjectPath" or "SoftClassPath";

    private static void CheckDepth(int depth)
    {
        if (depth > GvasFile.MaxDepth) throw new SaveFormatException("GVAS exceeds the nesting safety limit.");
    }

    private static string DescribeBlob(ReadOnlySpan<byte> blob) => blob.StartsWith(new byte[] { 0xC1, 0x83, 0x2A, 0x9E })
        ? "Unreal compressed chunks (Hogwarts databases use Oodle); not decoded."
        : blob.Length >= 2 && blob[0] == 0x78 ? "Possible zlib payload; not decoded." : "Opaque byte array.";
}

internal sealed class UnsupportedGvasValueException(string message) : Exception(message);
