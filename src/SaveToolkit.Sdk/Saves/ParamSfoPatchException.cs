namespace SaveToolkit.Sdk.Saves;

/// <summary>Describes malformed SFO data or an unsafe patch request.</summary>
public sealed class ParamSfoPatchException : FormatException
{
    /// <summary>Creates a contextual SFO patch error.</summary>
    public ParamSfoPatchException(string message) : base(message)
    {
    }
}
