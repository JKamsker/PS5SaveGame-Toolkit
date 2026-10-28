using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.TalesOfArise;

/// <summary>
/// Compares decrypted sections by schema ID rather than physical address. The encrypted directory is
/// shuffled between saves, so absolute payload offsets are not stable evidence keys.
/// </summary>
internal sealed class AriseDiff : ISaveDecoderHook
{
    private const int WordSize = sizeof(uint);
    private const int EvidencePathDepth = 3;

    public string Id => "tales-of-arise.sections-v1";

    public bool CanDecode(ISaveFile file) => new TalesOfArisePlugin().Probe(file).IsMatch;

    public void Decode(ISaveFile file, ISaveDecodeSink sink)
    {
        if (file.Length > sink.MaxFileBytes)
        {
            throw new SaveFormatException("Tales of Arise save exceeds the decoder file-size limit.");
        }

        if (sink.MaxDepth < EvidencePathDepth)
        {
            sink.MarkTruncated("Section evidence paths exceed the decoder depth limit.");
            return;
        }

        var save = AriseFormat.Decode(file.ReadAllBytes());
        sink.Warn("Raw little-endian uint32 evidence by schema ID and body offset; gameplay meanings and record identities are unknown. Outer header, screenshot, link pointers and gaps are excluded.");
        foreach (var section in save.Sections.OrderBy(section => section.Id))
        {
            for (var relative = 0; relative < section.Size; relative += WordSize)
            {
                if (!sink.Add($"evidence/{section.Id:X8}/{relative:X6}", "integer",
                    AriseFormat.U32(save.Payload, section.Offset + relative).ToString(CultureInfo.InvariantCulture),
                    $"{section.Name} +0x{relative:X} (unknown meaning)"))
                {
                    sink.MarkTruncated("Section evidence reached the decoder output limit.");
                    return;
                }
            }
        }
    }

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
