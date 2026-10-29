using System.Text.Json.Serialization;

namespace SaveToolkit.Sdk.Views;

/// <summary>Known unlock state of a prerequisite item; Unknown does not imply locked or unavailable.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GraphState>))]
public enum GraphState
{
    /// <summary>Confirmed already unlocked.</summary>
    Unlocked,
    /// <summary>Confirmed locked.</summary>
    Locked,
    /// <summary>Available to unlock under the plugin's known rules.</summary>
    Available,
    /// <summary>No confirmed interpretation of the stored state.</summary>
    Unknown,
}

/// <summary>The kind of existing control or row reached by a navigation target.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ViewTargetKind>))]
public enum ViewTargetKind
{
    /// <summary>An existing field control, addressed by value ID.</summary>
    Field,
    /// <summary>An existing action control, addressed by action ID; navigation never invokes it.</summary>
    Action,
    /// <summary>An existing table row, addressed by stable row key.</summary>
    Row,
}

/// <summary>
/// Navigation only: never invokes an action or edits a field. Path opens enclosing containers using
/// section:&lt;lazyId or title&gt;, tab:&lt;title&gt;, row:&lt;key&gt;. Row targets name the row in its parent table.
/// Queryable rows must already be loaded; this contract does not issue searches for target ids.
/// </summary>
public sealed record ViewTarget(ViewTargetKind Kind, string Id)
{
    /// <summary>Enclosing section, tab and row path components in root-to-target order.</summary>
    public IReadOnlyList<string> Path { get; init; } = [];
}

/// <summary>A stable prerequisite item with optional explanatory cost, branch and navigation metadata.</summary>
public sealed record GraphItem(string Id, string Label, GraphState State)
{
    /// <summary>Display-only cost; the framework does not enforce a resource budget.</summary>
    public string? Cost { get; init; }
    /// <summary>Optional branch label for grouping and filtering.</summary>
    public string? Branch { get; init; }
    /// <summary>Optional explanation or attribution.</summary>
    public string? Description { get; init; }
    /// <summary>Optional navigation to an existing field or action control; graph items do not support row targets.</summary>
    public ViewTarget? Target { get; init; }
}

/// <summary>From is a prerequisite of To. Cycles and missing endpoints are rejected.</summary>
public sealed record GraphEdge(string From, string To);

/// <summary>
/// Bounded, read-only prerequisite DAG. TextAlternative describes the diagram; the browser always
/// supplies a full text list of items, states, costs, prerequisites and targets alongside it.
/// Split larger graphs into regional/branch nodes or lazy sections; see SpatialViewLimits.
/// </summary>
public sealed record GraphNode(string Title, IReadOnlyList<GraphItem> Items,
    IReadOnlyList<GraphEdge> Edges, string TextAlternative) : ViewNode;
