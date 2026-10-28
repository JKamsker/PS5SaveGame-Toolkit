using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal sealed record SaveFact(uint Hash, uint Value);

internal static class FactsReader
{
    public static IReadOnlyList<SaveFact> Read(ReadOnlySpan<byte> raw)
    {
        var position = sizeof(int);
        var count = BinaryData.PackedInt(raw, ref position);
        if (count < 0 || count > (raw.Length - position) / 8 || position + count * 8 != raw.Length)
        {
            throw new SaveFormatException("Cyberpunk FactsTable count does not match its node size.");
        }

        var result = new SaveFact[count];
        for (var index = 0; index < count; index++)
        {
            result[index] = new SaveFact(BinaryData.UInt32(raw, position + index * 4),
                BinaryData.UInt32(raw, position + count * 4 + index * 4));
        }

        return result;
    }
}
