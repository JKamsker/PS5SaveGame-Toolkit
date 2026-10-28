using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Testing.Diff;

/// <summary>Adapts an SDK-only plugin hook to the shared F1 diff engine.</summary>
public sealed class HookSaveDecoder(ISaveDecoderHook hook) : ISaveDecoder
{
    public string Id => hook.Id;

    public bool CanDecode(ISaveFile file) => hook.CanDecode(file);

    public SemanticTree Decode(ISaveFile file, DecodeLimits limits)
    {
        if (file.Length > limits.MaxFileBytes)
        {
            throw new InvalidDataException($"File is {file.Length} bytes; the decode limit is {limits.MaxFileBytes}.");
        }

        var builder = new SemanticTreeBuilder(Id, limits);
        hook.Decode(file, new Sink(builder));
        return builder.Build();
    }

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
