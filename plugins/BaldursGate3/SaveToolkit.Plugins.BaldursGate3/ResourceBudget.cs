namespace SaveToolkit.Plugins.BaldursGate3;

/// <summary>One budget per opened document; charge before allocating each resource's expanded tables.</summary>
internal sealed class ResourceBudget
{
    private long _bytes;
    private long _nodes;
    private long _attributes;

    internal void Reserve(long bytes, int nodes, int attributes)
    {
        _bytes += bytes;
        _nodes += nodes;
        _attributes += attributes;
        BinaryData.Require(_bytes <= BinaryData.MaxBufferSize, "BG3 expanded resource sections exceed 256 MiB per document.");
        BinaryData.Require(_nodes <= BinaryData.MaxRecords && _attributes <= BinaryData.MaxRecords,
            "BG3 document exceeds one million resource nodes or attributes.");
    }
}
