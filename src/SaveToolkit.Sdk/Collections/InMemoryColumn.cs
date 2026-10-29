namespace SaveToolkit.Sdk.Collections;

/// <summary>Cheap model projections; rich view cells are never used for querying.</summary>
public sealed record InMemoryColumn<T>(string Key)
{
    /// <summary>Text searched for this column; null excludes the column from free-text search.</summary>
    public Func<T, string?>? SearchText { get; init; }
    /// <summary>Raw filter text for this column; null means column filtering is unsupported.</summary>
    public Func<T, string?>? FilterText { get; init; }

    /// <summary>Compare native values, including an explicit null policy, without numeric conversion.</summary>
    public IComparer<T>? Comparer { get; init; }
}

/// <summary>Explicit matching, ordering, paging and confirmed-empty-row policy for an in-memory collection.</summary>
public sealed record InMemoryCollectionOptions<T>
{
    /// <summary>Null orders by ordinal native key. Equal values always use ascending ordinal keys.</summary>
    public IComparer<T>? DefaultComparer { get; init; }
    /// <summary>Reverses the default ordering when descending is requested without an explicit sort column.</summary>
    public bool ReverseDefaultOrderWhenDescending { get; init; } = true;
    /// <summary>Clamps offsets and page sizes when true; false rejects values outside protocol bounds.</summary>
    public bool ClampPaging { get; init; } = true;
    /// <summary>Recognizes confirmed empty slots. Required when collection metadata declares empty-row presentation.</summary>
    public Func<T, bool>? IsEmpty { get; init; }
    /// <summary>Optional additional model text searched alongside searchable columns.</summary>
    public Func<T, string?>? ExtraSearchText { get; init; }

    /// <summary>Identity by default. Supply Trim/whitespace handling when required by the plugin.</summary>
    public Func<string?, string?> NormalizeSearchText { get; init; } = text => text;
    /// <summary>Matches projected text against normalized search text; defaults to ordinal case-insensitive substring matching.</summary>
    public Func<string?, string, bool> SearchMatches { get; init; } =
        (value, text) => value?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;
    /// <summary>Matches projected text against an exact filter; defaults to ordinal case-insensitive equality.</summary>
    public Func<string?, string, bool> FilterMatches { get; init; } =
        (value, text) => string.Equals(value, text, StringComparison.OrdinalIgnoreCase);
}
