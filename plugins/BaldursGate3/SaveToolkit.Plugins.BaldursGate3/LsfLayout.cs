namespace SaveToolkit.Plugins.BaldursGate3;

/// <summary>Wire offsets and bit fields from LSLib's LSFCommon.cs.</summary>
internal static class LsfLayout
{
    internal const int Version = 4;
    internal const int MetadataOffset = 16;
    internal const int SizePairLength = 8;
    internal const int V5PairCount = 4;
    internal const int V6PairCount = 5;
    internal const int MetadataTrailerSize = 8;
    internal const int MetadataFormatOffset = 4;
    internal const int StringsPair = 0;
    internal const int KeysPair = 1;
    internal const int NodesPair = 2;
    internal const int AttributesPair = 3;
    internal const int ValuesPair = 4;
    internal const int CompactRecordSize = 12;
    internal const int AdjacencyRecordSize = 16;
    internal const int KeyRecordSize = 8;
    internal const int CompactFirstAttribute = 4;
    internal const int CompactParent = 8;
    internal const int ExtendedParent = 4;
    internal const int ExtendedSibling = 8;
    internal const int ExtendedFirstAttribute = 12;
    internal const int AttributeTypeAndLength = 4;
    internal const int AttributeLink = 8;
    internal const int ExtendedValueOffset = 12;
    internal const int KeyName = 4;
    internal const int NameBucketShift = 16;
    internal const int AttributeTypeBits = 6;
    internal const uint AttributeTypeMask = 0x3f;
}
