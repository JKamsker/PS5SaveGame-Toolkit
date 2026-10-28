using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Sdk.Diff;

/// <summary>Optional plugin capability exposing read-only semantic decoders to developer tools.</summary>
public interface ISaveDecoderProvider
{
    /// <summary>Stateless hooks with globally unique ids, distinct from plugin ids and built-in modes.</summary>
    IReadOnlyList<ISaveDecoderHook> Decoders { get; }
}

/// <summary>A decoder independent of UI pages. Implementations perform no I/O or edits.</summary>
public interface ISaveDecoderHook
{
    string Id { get; }

    /// <summary>Cheap check; return false for foreign data without throwing.</summary>
    bool CanDecode(ISaveFile file);

    /// <summary>Emit stable identity paths; stop walking when the sink returns false.</summary>
    void Decode(ISaveFile file, ISaveDecodeSink sink);
}

/// <summary>Host-owned bounded output. Limits apply per file, across the entire decoder.</summary>
public interface ISaveDecodeSink
{
    int MaxValues { get; }
    int MaxValueLength { get; }
    int MaxDepth { get; }
    long MaxFileBytes { get; }

    /// <summary>Returns false when no further values can be accepted.</summary>
    bool Add(string path, string kind, string value, string? label = null);

    void Warn(string message);
    void MarkTruncated(string reason);
}
