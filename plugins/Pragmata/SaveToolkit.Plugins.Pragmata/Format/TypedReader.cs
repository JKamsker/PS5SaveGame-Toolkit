using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Pragmata.Format;

internal sealed class TypedReader(byte[] data)
{
    private const int AlignmentOrigin = 16;
    private const int MaximumDepth = 100;
    private const int MaximumArrayElements = 1_000_000;
    private const int MaximumNodes = 2_000_000;
    private const uint ClassArrayMarker = 0xffeeffee;
    private const int ValueArray = 0;
    private const int ClassArray = 1;
    private int _position;
    private int _nodeCount;
    public List<SaveNode> Classes { get; } = [];
    public int ScalarCount { get; private set; }
    public int Position => _position;

    public IReadOnlyList<SaveNode> Parse()
    {
        var roots = new List<SaveNode>();
        var hashes = new HashSet<uint>();
        while (_position < data.Length)
        {
            var hash = UInt32();
            if (!hashes.Add(hash)) Fail("Duplicate root hash");
            var root = NewNode(hash, FieldTags.Class);
            ReadClass(root, 0);
            roots.Add(root);
        }
        if (_position != data.Length || roots.Count == 0) Fail("Incomplete typed payload");
        return roots;
    }

    private void ReadClass(SaveNode node, int depth)
    {
        if (depth > MaximumDepth) Fail("Typed stream nesting limit exceeded");
        node.Offset = _position;
        var count = Count(50_000, 8);
        node.ClassHash = UInt32();
        Classes.Add(node);
        var children = new SaveNode[count];
        var hashes = new HashSet<uint>();
        for (var i = 0; i < count; i++)
        {
            var hash = UInt32();
            var type = Int32();
            if (!hashes.Add(hash)) Fail("Duplicate class field hash");
            var field = NewNode(hash, type, node);
            ReadValue(field, depth + 1);
            children[i] = field;
            Align(4);
        }
        node.Children = children;
    }

    private void ReadValue(SaveNode node, int depth)
    {
        if (depth > MaximumDepth) Fail("Typed stream nesting limit exceeded");
        switch (node.Type)
        {
            case FieldTags.Class:
                ReadClass(node, depth);
                return;
            case FieldTags.String:
                Align(4);
                ReadString(node);
                return;
            case FieldTags.Array:
                ReadArray(node, depth);
                return;
            default:
                Align(4);
                ReadSized(node, CheckedSize(UInt32()));
                return;
        }
    }

    private void ReadString(SaveNode node)
    {
        var count = Count(data.Length / 2, 2);
        node.Offset = _position;
        node.Size = count * 2;
        Read(node.Size);
        ScalarCount++;
    }

    private void ReadSized(SaveNode node, int size)
    {
        Align(size);
        node.Offset = _position;
        node.Size = size;
        if (node.Type != FieldTags.Struct && !PrimitiveCodec.IsSupported(node.Type, size))
            Fail($"Unsupported primitive type/size {node.Type}/{size}");
        Read(size);
        ScalarCount++;
    }

    private void ReadArray(SaveNode node, int depth)
    {
        Align(4);
        node.Offset = _position;
        var memberType = Int32();
        var memberSize = CheckedSize(UInt32());
        var count = Count(MaximumArrayElements, 1);
        var arrayType = Int32();
        if (arrayType is not (ValueArray or ClassArray)) Fail("Unsupported array kind");
        if (arrayType == ClassArray)
        {
            if (UInt32() == ClassArrayMarker) Read(checked(count * sizeof(uint)));
            else _position -= sizeof(uint);
        }
        var children = new SaveNode[count];
        for (var i = 0; i < count; i++)
        {
            var child = NewNode(0, arrayType == ClassArray ? FieldTags.Class : memberType, node, i);
            if (arrayType == ClassArray) ReadClass(child, depth + 1);
            else if (memberType == FieldTags.String) ReadValue(child, depth + 1);
            else ReadSized(child, memberSize);
            children[i] = child;
        }
        node.Children = children;
        Align(4);
    }

    private SaveNode NewNode(uint hash, int type, SaveNode? parent = null, int index = -1)
    {
        if (++_nodeCount > MaximumNodes) Fail("Typed stream node limit exceeded");
        return new SaveNode(hash, type, parent, index);
    }

    private ReadOnlySpan<byte> Read(int count)
    {
        if (count < 0 || count > data.Length - _position) Fail("Truncated typed payload");
        var result = data.AsSpan(_position, count);
        _position += count;
        return result;
    }

    private void Align(int size)
    {
        if (size is < 1 or > 65536) Fail("Unsupported typed payload alignment");
        // Preserve the serializer's bit-mask rule, including for non-powers of two.
        var absolute = _position + AlignmentOrigin;
        var target = ((absolute + size - 1) & ~(size - 1)) - AlignmentOrigin;
        Read(target - _position);
    }

    private uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Read(sizeof(uint)));
    private int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Read(sizeof(int)));

    private int Count(int maximum, int minimumBytes)
    {
        var count = UInt32();
        if (count > maximum || count > (data.Length - _position) / minimumBytes) Fail("Implausible typed element count");
        return (int)count;
    }

    private int CheckedSize(uint size)
    {
        if (size > 65536) Fail("Unsupported typed member size");
        return (int)size;
    }

    private void Fail(string message) => throw new SaveFormatException($"{message} at plaintext 0x{_position:x}.");
}
