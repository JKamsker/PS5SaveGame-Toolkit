namespace SaveToolkit.Core.Editing;

/// <summary>A resolved field presentation exceeds the host's bounded JSON response budget.</summary>
public sealed class FieldReferenceResponseLimitException(string message) : Exception(message);
