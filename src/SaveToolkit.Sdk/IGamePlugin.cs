using System.Text.Json.Serialization;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Sdk;

/// <summary>
/// Entry point of a game/format plugin. Implementations need a public parameterless constructor and
/// must be stateless/thread-safe: the framework shares one instance across all sessions.
/// </summary>
public interface IGamePlugin
{
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
}

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

public sealed record GameTitle(string TitleId, string Name)
{
    /// <summary>e.g. "EU", "US", "JP", "Asia".</summary>
    public string? Region { get; init; }
}

public sealed record PluginInfo(string Id, string Name, string Version)
{
    public string? Description { get; init; }

    /// <summary>Titles this plugin targets. Empty for engine/format-generic plugins.</summary>
    public IReadOnlyList<GameTitle> Games { get; init; } = [];

    public PluginMaturity Maturity { get; init; } = PluginMaturity.Experimental;

    /// <summary>Reference URLs (format docs, upstream tools) shown in the UI.</summary>
    public IReadOnlyList<string> References { get; init; } = [];
}

/// <summary>Probe scores; the highest-scoring plugin becomes the default for a file.</summary>
public static class ProbeScore
{
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

public sealed record ProbeResult(int Score)
{
    /// <summary>What the plugin thinks the file is, e.g. "Gameplay save, slot 3".</summary>
    public string? Label { get; init; }

    public static ProbeResult None { get; } = new(ProbeScore.None);

    public bool IsMatch => Score > ProbeScore.None;

    public static ProbeResult Match(int score, string? label = null) => new(score) { Label = label };
}

/// <summary>The file is not valid for this plugin. The message is shown to the user.</summary>
public sealed class SaveFormatException : Exception
{
    public SaveFormatException(string message) : base(message) { }
    public SaveFormatException(string message, Exception inner) : base(message, inner) { }
    public SaveFormatException() { }
}
