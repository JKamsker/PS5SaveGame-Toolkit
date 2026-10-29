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
    /// <summary>Globally unique hook ID using ASCII letters, digits, dots, underscores or hyphens, at most 128 characters.</summary>
    string Id { get; }

    /// <summary>Cheap check; return false for foreign data without throwing.</summary>
    bool CanDecode(ISaveFile file);

    /// <summary>Emit stable identity paths; stop walking when the sink returns false.</summary>
    void Decode(ISaveFile file, ISaveDecodeSink sink);
}

/// <summary>Host-owned bounded output. Limits apply per file, across the entire decoder.</summary>
public interface ISaveDecodeSink
{
    /// <summary>Host value-count budget for the complete file.</summary>
    int MaxValues { get; }
    /// <summary>Host budget for an emitted value's text length.</summary>
    int MaxValueLength { get; }
    /// <summary>Host nesting-depth budget to honor while parsing or walking the model.</summary>
    int MaxDepth { get; }
    /// <summary>Host input-file size budget in bytes.</summary>
    long MaxFileBytes { get; }

    /// <summary>Returns false when no further values can be accepted.</summary>
    bool Add(string path, string kind, string value, string? label = null);

    /// <summary>Records an evidence limitation without declaring omitted output.</summary>
    void Warn(string message);
    /// <summary>Declares incomplete output and explains the traversal bound or identity limitation.</summary>
    void MarkTruncated(string reason);
}
