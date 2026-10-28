namespace SaveToolkit.Plugins.TalesOfArise;

/// <summary>
/// Compares decrypted sections by schema ID rather than physical address. The encrypted directory is
/// shuffled between saves, so absolute payload offsets are not stable evidence keys.
/// </summary>
internal static class AriseDiff
{
    public static IReadOnlyList<AriseSectionDiff> Compare(AriseSave left, AriseSave right)
    {
        var leftById = left.Sections.ToDictionary(section => section.Id);
        var rightById = right.Sections.ToDictionary(section => section.Id);
        if (!leftById.Keys.ToHashSet().SetEquals(rightById.Keys))
        {
            throw new InvalidOperationException("Cannot align Tales of Arise saves: schema section IDs differ.");
        }

        return left.Sections.Select(section => CompareSection(section, rightById[section.Id], left.Payload, right.Payload)).ToArray();
    }

    private static AriseSectionDiff CompareSection(AriseSection left, AriseSection right, byte[] leftPayload, byte[] rightPayload)
    {
        if (left.Version != right.Version || left.Size != right.Size)
        {
            throw new InvalidOperationException($"Section 0x{left.Id:X8} has incompatible version or size.");
        }

        var changed = new List<int>();
        for (var relative = 0; relative < left.Size; relative++)
        {
            if (leftPayload[left.Offset + relative] != rightPayload[right.Offset + relative])
            {
                changed.Add(relative);
            }
        }

        return new AriseSectionDiff(left.Name, left.Id, left.Version, left.Size, changed);
    }
}

internal sealed record AriseSectionDiff(string Name, uint Id, uint Version, int Size, IReadOnlyList<int> ChangedOffsets)
{
    public int ChangedBytes => ChangedOffsets.Count;
}
