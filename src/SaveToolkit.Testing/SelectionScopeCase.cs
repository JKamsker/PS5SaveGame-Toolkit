using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Testing;

/// <summary>Explicit, opt-in evidence for one planned action and table. No plugin adopts this automatically.</summary>
/// <param name="ActionId">Published planned action identity.</param>
/// <param name="SelectionTableId">Published eager SelectionId or F8 CollectionId.</param>
/// <param name="RowKeys">Nonempty explicit selection to probe.</param>
public sealed record SelectionScopeCase(string ActionId, string SelectionTableId, IReadOnlyList<string> RowKeys)
{
    /// <summary>Deterministic inputs; defaults are deliberately not guessed from the view.</summary>
    public IReadOnlyDictionary<string, FieldValue> Parameters { get; init; } = new Dictionary<string, FieldValue>();
    /// <summary>Prepare meaningful edits on a fresh document after its first BuildView. Must throw on rejection.</summary>
    public Action<ISaveDocument>? Stage { get; init; }
    /// <summary>Known value ownership by stable row key, including unselected rows. Never inferred from ID prefixes.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> RowOwnedValueIds { get; init; } = new Dictionary<string, IReadOnlyList<string>>();
    /// <summary>Nonempty set of selected-row values that must meaningfully change in the plan.</summary>
    public IReadOnlyList<string> ExpectedChangedValueIds { get; init; } = [];
    /// <summary>Explicitly permitted shared effects outside row ownership.</summary>
    public IReadOnlyList<string> PermittedSharedValueIds { get; init; } = [];
    /// <summary>Explicitly permitted outside-row consequences, only when marked Derived in the plan.</summary>
    public IReadOnlyList<string> PermittedDerivedValueIds { get; init; } = [];
}

/// <summary>Bounded view evidence. Reaching any traversal cut makes the probe not evaluated.</summary>
public sealed record SelectionScopeLimits
{
    /// <summary>Maximum visited nodes and rows.</summary>
    public int MaxNodes { get; init; } = 100_000;
    /// <summary>Maximum view nesting depth.</summary>
    public int MaxDepth { get; init; } = 128;
    /// <summary>Maximum distinct lazy expansions.</summary>
    public int MaxLazyExpansions { get; init; } = 150;
    /// <summary>Maximum F8 pages across the whole view, including empty rows.</summary>
    public int MaxQueryPages { get; init; } = 12;
}

/// <summary>Advisory evidence outcome, never an automatic plugin conformance pass.</summary>
public enum SelectionScopeStatus
{
    /// <summary>Insufficient evidence, rejected/no-op plans, or a traversal cut.</summary>
    NotEvaluated,
    /// <summary>The specific staged case satisfied the supplied ownership expectations.</summary>
    Passed,
    /// <summary>A proven ownership, determinism, purity or expected-effect violation.</summary>
    Failed,
}

/// <summary>Advisory outcome and reasons; callers must report NotEvaluated separately from Passed.</summary>
/// <param name="Status">Outcome for this case only.</param>
/// <param name="Diagnostics">Evidence limitations or proven violations.</param>
public sealed record SelectionScopeResult(SelectionScopeStatus Status, IReadOnlyList<string> Diagnostics);
