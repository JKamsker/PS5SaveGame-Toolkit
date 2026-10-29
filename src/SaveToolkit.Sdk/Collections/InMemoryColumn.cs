namespace SaveToolkit.Sdk.Collections;

/// <summary>Cheap model projections; rich view cells are never used for querying.</summary>
public sealed record InMemoryColumn<T>(string Key)
{
    public Func<T, string?>? SearchText { get; init; }
    public Func<T, string?>? FilterText { get; init; }

    /// <summary>Compare native values, including an explicit null policy, without numeric conversion.</summary>
    public IComparer<T>? Comparer { get; init; }
}

public sealed record InMemoryCollectionOptions<T>
{
    /// <summary>Null orders by ordinal native key. Equal values always use ascending ordinal keys.</summary>
    public IComparer<T>? DefaultComparer { get; init; }
    public bool ReverseDefaultOrderWhenDescending { get; init; } = true;
    public bool ClampPaging { get; init; } = true;
    public Func<T, bool>? IsEmpty { get; init; }
    public Func<T, string?>? ExtraSearchText { get; init; }

    /// <summary>Identity by default. Supply Trim/whitespace handling when required by the plugin.</summary>
    public Func<string?, string?> NormalizeSearchText { get; init; } = text => text;
    public Func<string?, string, bool> SearchMatches { get; init; } =
        (value, text) => value?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;
    public Func<string?, string, bool> FilterMatches { get; init; } =
        (value, text) => string.Equals(value, text, StringComparison.OrdinalIgnoreCase);
}
