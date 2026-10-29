namespace SaveToolkit.Core.Presets;

/// <summary>Signals a transient or unavailable external preset-store write.</summary>
public sealed class PresetStorageException(string message, Exception innerException) : IOException(message, innerException);
