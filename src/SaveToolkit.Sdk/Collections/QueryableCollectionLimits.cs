namespace SaveToolkit.Sdk.Collections;

/// <summary>Protocol defaults shared by plugins and the framework.</summary>
public static class QueryableCollectionLimits
{
    /// <summary>Default number of rows requested per page.</summary>
    public const int DefaultPageSize = 50;
    /// <summary>Maximum requested rows per page.</summary>
    public const int MaxPageSize = 200;
    /// <summary>Maximum free-text query length in UTF-16 code units.</summary>
    public const int MaxQueryLength = 256;
    /// <summary>Maximum distinct column filters in a query.</summary>
    public const int MaxFilterCount = 32;
    /// <summary>Maximum filter value length in UTF-16 code units.</summary>
    public const int MaxFilterLength = 256;
    /// <summary>Maximum opaque cursor length in UTF-16 code units.</summary>
    public const int MaxCursorLength = 4_096;
}
