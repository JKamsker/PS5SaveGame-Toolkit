using SaveToolkit.Sdk.Diff;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

internal sealed class FaithDecodeTestSink : ISaveDecodeSink
{
    public int MaxValues => int.MaxValue;
    public int MaxValueLength => 256;
    public int MaxDepth { get; init; } = 64;
    public long MaxFileBytes { get; init; } = long.MaxValue;
    public bool AcceptsValues { get; init; } = true;
    public int AddCalls { get; private set; }
    public bool Truncated { get; private set; }
    public bool Add(string path, string kind, string value, string? label = null)
    {
        AddCalls++;
        return AcceptsValues;
    }
    public void Warn(string message) => Assert.Fail(message);
    public void MarkTruncated(string reason) => Truncated = true;
}

