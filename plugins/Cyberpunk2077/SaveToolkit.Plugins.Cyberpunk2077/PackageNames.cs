using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Cyberpunk2077;

// Appends names without renumbering any existing name/type/property references.
internal static class PackageNames
{
    private const int DescriptorSize = sizeof(uint);
    private const uint OffsetMask = 0x00FFFFFF;

    public static byte[] Append(byte[] original, int baseOffset, int nameData, int chunkDescriptors,
        int chunkCount, IReadOnlyList<string> names)
    {
        if (names.Count == 0) return original;
        if (names.Any(name => name.Length is < 1 or > 254 || name.Any(character => character is < ' ' or > '~')))
            throw new SaveFormatException("Unsupported appended RED name encoding or length.");
        var descriptorGrowth = checked(names.Count * DescriptorSize);
        var dataGrowth = names.Sum(name => name.Length + 1);
        var growth = checked(descriptorGrowth + dataGrowth);
        if ((long)chunkDescriptors + growth > OffsetMask || (long)original.Length + growth > BinaryData.MaxExpandedSize)
            throw new SaveFormatException("Appended RED names exceed package limits.");
        var result = new byte[checked(original.Length + growth)];
        original.AsSpan(0, baseOffset + nameData).CopyTo(result);
        original.AsSpan(baseOffset + nameData, chunkDescriptors - nameData)
            .CopyTo(result.AsSpan(baseOffset + nameData + descriptorGrowth));
        original.AsSpan(baseOffset + chunkDescriptors).CopyTo(result.AsSpan(baseOffset + chunkDescriptors + growth));
        for (var position = 0; position < nameData; position += DescriptorSize)
        {
            var descriptor = BinaryData.UInt32(original, baseOffset + position);
            var offset = (descriptor & OffsetMask) + (uint)descriptorGrowth;
            if (offset > OffsetMask) throw new SaveFormatException("RED name offset exceeds its 24-bit storage.");
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(baseOffset + position), (descriptor & ~OffsetMask) | offset);
        }
        var nextData = chunkDescriptors + descriptorGrowth;
        for (var index = 0; index < names.Count; index++)
        {
            var name = Encoding.ASCII.GetBytes(names[index] + '\0');
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(baseOffset + nameData + index * DescriptorSize),
                (uint)nextData | ((uint)name.Length << 24));
            name.CopyTo(result, baseOffset + nextData);
            nextData += name.Length;
        }
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(12), nameData + descriptorGrowth);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(16), chunkDescriptors + growth);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(20), BinaryData.Int32(original, 20) + growth);
        for (var index = 0; index < chunkCount; index++)
        {
            var oldPosition = baseOffset + chunkDescriptors + index * 8 + 4;
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(oldPosition + growth), BinaryData.Int32(original, oldPosition) + growth);
        }
        return result;
    }
}
