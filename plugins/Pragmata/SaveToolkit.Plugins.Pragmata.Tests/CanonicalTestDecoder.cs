using SaveToolkit.Plugins.Pragmata.Diff;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Pragmata.Tests;

/// <summary>Thin F1 adapter for exercising the production SDK hook and synthetic parsed trees.</summary>
internal sealed class CanonicalTestDecoder : ISaveDecoder
{
    private readonly PragmataDecoder _hook = new();
    public string Id => _hook.Id;
    public bool CanDecode(ISaveFile file) => _hook.CanDecode(file);
    public SemanticTree Decode(ISaveFile file, DecodeLimits limits) => new HookSaveDecoder(_hook).Decode(file, limits);

    internal SemanticTree Decode(byte[] plaintext, IReadOnlyList<SaveNode> roots, DecodeLimits limits)
    {
        var builder = new SemanticTreeBuilder(Id, limits);
        _hook.Decode(plaintext, roots, new Sink(builder));
        return builder.Build();
    }

    // No traversal or path logic lives here; shared F1 enforces every output bound.
    private sealed class Sink(SemanticTreeBuilder builder) : ISaveDecodeSink
    {
        public int MaxValues => builder.Limits.MaxValues;
        public int MaxValueLength => builder.Limits.MaxValueLength;
        public int MaxDepth => builder.Limits.MaxDepth;
        public long MaxFileBytes => builder.Limits.MaxFileBytes;
        public bool Add(string path, string kind, string value, string? label = null) => builder.Add(path, kind, value, label);
        public void Warn(string message) => builder.Warn(message);
        public void MarkTruncated(string reason) => builder.MarkTruncated(reason);
    }
}
