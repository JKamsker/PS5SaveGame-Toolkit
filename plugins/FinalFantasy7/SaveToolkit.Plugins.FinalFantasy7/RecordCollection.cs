using System.Globalization;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Queryable header index; record bodies remain in the lazy explorer.</summary>
internal static class RecordCollection
{
    public const string Id = "records";

    public static CollectionQuery Create(IReadOnlyList<LinkedRecord> records) => new(new(Id, "Linked records",
        [new("index", "#", IntegerSpec.UInt32), new("tag", "Tag"), new("offset", "Offset"), new("length", "Span bytes", IntegerSpec.UInt32),
         new("next", "Next pointer"), new("version", "Raw +0x20"), new("state", "State byte")], records.Count)
    {
        Description = "Absolute offsets, sample-verified framing. Empty allocations retain their full span. Tail pointer is literal. Use the lazy explorer for raw bodies.",
    }, () => records.Select(record => new CollectionEntry($"record-row/{record.Index}", new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["index"] = record.Index.ToString(CultureInfo.InvariantCulture), ["tag"] = record.Tag,
        ["offset"] = $"0x{record.Offset:X}", ["length"] = record.Length.ToString(CultureInfo.InvariantCulture),
        ["next"] = $"0x{record.Next:X}", ["version"] = $"0x{record.Version:X}", ["state"] = $"0x{record.State:X}",
    })));
}
