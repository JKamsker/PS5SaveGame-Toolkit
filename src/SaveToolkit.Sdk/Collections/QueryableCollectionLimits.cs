namespace SaveToolkit.Sdk.Collections;

/// <summary>Protocol defaults shared by plugins and the framework.</summary>
public static class QueryableCollectionLimits
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
    public const int MaxQueryLength = 256;
    public const int MaxFilterCount = 32;
    public const int MaxFilterLength = 256;
    public const int MaxCursorLength = 4_096;
}
