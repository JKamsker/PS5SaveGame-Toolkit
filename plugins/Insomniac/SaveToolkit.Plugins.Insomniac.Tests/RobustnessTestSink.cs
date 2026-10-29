using SaveToolkit.Sdk.Diff;

namespace SaveToolkit.Plugins.Insomniac.Tests;

internal sealed class RobustnessTestSink : ISaveDecodeSink
{
    public int MaxValues => 100;
    public int MaxValueLength => 256;
    public int MaxDepth { get; init; } = 64;
    public long MaxFileBytes { get; init; } = long.MaxValue;
    public bool AcceptsValues { get; init; } = true;
    public int AddCalls { get; private set; }
    public int Truncations { get; private set; }
    public bool Add(string path, string kind, string value, string? label = null) { AddCalls++; return AcceptsValues; }
    public void Warn(string message) { }
    public void MarkTruncated(string reason) => Truncations++;
}
