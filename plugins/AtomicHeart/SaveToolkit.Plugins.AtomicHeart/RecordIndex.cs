using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.AtomicHeart;

internal static class RecordKinds
{
    internal const string Actor = "actor";
    internal const string Component = "component";
    internal const string Subsystem = "subsystem";
}

internal static class InventoryClasses
{
    internal const string Player = "/BP_PlayerInventory.BP_PlayerInventory_C";
    internal const string Storage = "/BP_PlayerStorage.BP_PlayerStorage_C";
}

internal readonly record struct ByteSpan(int Offset, int Length)
{
    internal int End => checked(Offset + Length);
}

internal sealed record TaggedProperty(string Name, string Type, string Detail, int ArrayIndex,
    ByteSpan Tag, ByteSpan Payload, FieldValue? Value)
{
    internal IReadOnlyList<TaggedProperty> Children { get; init; } = [];
}

internal sealed record ObjectRecord(string Id, string Kind, string Owner, string Name,
    string ClassPath, string ClassSubPath, ByteSpan Span, ByteSpan Payload,
    IReadOnlyList<string> Tags, IReadOnlyList<TaggedProperty> Properties,
    ByteSpan UnknownTail, IReadOnlyList<string> PrefixIds);

internal sealed record LevelRecord(string Name, ByteSpan Span, int ActorCount);

internal sealed record RecordIndex(byte[] Plain, IReadOnlyList<TaggedProperty> Properties,
    IReadOnlyList<ObjectRecord> Objects, IReadOnlyList<LevelRecord> Levels)
{
    internal const int MaxRecords = 32768;
    internal const int MaxLevels = 4096;
    internal const int MaxProperties = 100000;
    internal const int MaxCollection = 32768;
    internal const int MaxNameUnits = 4096;
    internal const int MaxIndexPathUnits = 16 * 1024 * 1024;
}
