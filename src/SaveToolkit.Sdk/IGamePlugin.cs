using System.Text.Json.Serialization;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Sdk;

/// <summary>
/// Entry point of a game/format plugin. Implementations need a public parameterless constructor and
/// must be stateless/thread-safe: the framework shares one instance across all sessions.
/// </summary>
public interface IGamePlugin
{
    /// <summary>Stable plugin identity, version, supported titles, write maturity and upstream references.</summary>
    PluginInfo Info { get; }

    /// <summary>
    /// Cheap check whether this plugin understands <paramref name="file"/>. Must not throw for foreign
    /// data; read only what is needed (prefer <see cref="ISaveFile.ReadHeader"/>).
    /// </summary>
    ProbeResult Probe(ISaveFile file);

    /// <summary>
    /// Parses the file. Throw <see cref="SaveFormatException"/> with a user-facing message for invalid data.
    /// </summary>
    ISaveDocument Open(ISaveFile file);

    /// <summary>
    /// Parses the file with reference data (names, limits, icons) pinned for this document. The framework
    /// always calls this overload; the default ignores <paramref name="references"/>, so plugins that only
    /// implement <see cref="Open(ISaveFile)"/> keep working. Plugins that use reference packs implement this
    /// and let <see cref="Open(ISaveFile)"/> forward with <see cref="IReferenceContext.Empty"/>.
    /// </summary>
    ISaveDocument Open(ISaveFile file, IReferenceContext references) => Open(file);
}

/// <summary>Evidence level for the plugin's ability to write saves that load in-game.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PluginMaturity>))]
public enum PluginMaturity
{
    /// <summary>Shows data, cannot write.</summary>
    ReadOnly,

    /// <summary>Writes, but not confirmed to load on a console.</summary>
    Experimental,

    /// <summary>Edits verified in-game.</summary>
    Stable,
}

/// <summary>A supported title and its human game name; list regional title IDs separately.</summary>
public sealed record GameTitle(string TitleId, string Name)
{
    /// <summary>e.g. "EU", "US", "JP", "Asia".</summary>
    public string? Region { get; init; }
}

/// <summary>Plugin metadata shared across sessions. Id is a stable lower-case identifier; Version is SemVer.</summary>
public sealed record PluginInfo(string Id, string Name, string Version)
{
    /// <summary>Optional explanation of supported formats and editing limits.</summary>
    public string? Description { get; init; }

    /// <summary>Titles this plugin targets. Empty for engine/format-generic plugins.</summary>
    public IReadOnlyList<GameTitle> Games { get; init; } = [];

    /// <summary>Write confidence; only confirmed in-game edits qualify as Stable.</summary>
    public PluginMaturity Maturity { get; init; } = PluginMaturity.Experimental;

    /// <summary>Reference URLs (format docs, upstream tools) shown in the UI.</summary>
    public IReadOnlyList<string> References { get; init; } = [];
}

/// <summary>Probe scores; the highest-scoring plugin becomes the default for a file.</summary>
public static class ProbeScore
{
    /// <summary>No format recognition.</summary>
    public const int None = 0;

    /// <summary>The built-in raw viewer.</summary>
    public const int Fallback = 1;

    /// <summary>Generic container/format recognised (JSON, zlib, ...).</summary>
    public const int Format = 20;

    /// <summary>Engine-level format recognised (Unreal GVAS, RE Engine DSSS, ...).</summary>
    public const int Engine = 50;

    /// <summary>Title id and file layout/magic both match.</summary>
    public const int Title = 100;
}

/// <summary>Recognition confidence and an optional file label; positive scores participate in plugin selection.</summary>
public sealed record ProbeResult(int Score)
{
    /// <summary>What the plugin thinks the file is, e.g. "Gameplay save, slot 3".</summary>
    public string? Label { get; init; }

    /// <summary>No recognition of the supplied file.</summary>
    public static ProbeResult None { get; } = new(ProbeScore.None);

    /// <summary>Whether the score is positive.</summary>
    public bool IsMatch => Score > ProbeScore.None;

    /// <summary>Creates a recognition result using a score from <see cref="ProbeScore"/> and an optional file label.</summary>
    public static ProbeResult Match(int score, string? label = null) => new(score) { Label = label };
}

/// <summary>The file is not valid for this plugin. The message is shown to the user.</summary>
public sealed class SaveFormatException : Exception
{
    /// <summary>Creates a format rejection with a user-facing message.</summary>
    public SaveFormatException(string message) : base(message) { }
    /// <summary>Creates a format rejection retaining its underlying parse failure.</summary>
    public SaveFormatException(string message, Exception inner) : base(message, inner) { }
    /// <summary>Creates a format rejection without a specific diagnostic.</summary>
    public SaveFormatException() { }
}
