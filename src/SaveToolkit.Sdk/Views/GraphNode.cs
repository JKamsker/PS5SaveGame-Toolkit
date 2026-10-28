using System.Text.Json.Serialization;

namespace SaveToolkit.Sdk.Views;

[JsonConverter(typeof(JsonStringEnumConverter<GraphState>))]
public enum GraphState { Unlocked, Locked, Available, Unknown }

[JsonConverter(typeof(JsonStringEnumConverter<ViewTargetKind>))]
public enum ViewTargetKind { Field, Action, Row }

/// <summary>
/// Navigation only: never invokes an action or edits a field. Path opens enclosing containers using
/// section:&lt;lazyId or title&gt;, tab:&lt;title&gt;, row:&lt;key&gt;. Row targets name the row in its parent table.
/// Queryable rows must already be loaded; this contract does not issue searches for target ids.
/// </summary>
public sealed record ViewTarget(ViewTargetKind Kind, string Id)
{
    public IReadOnlyList<string> Path { get; init; } = [];
}

public sealed record GraphItem(string Id, string Label, GraphState State)
{
    public string? Cost { get; init; }
    public string? Branch { get; init; }
    public string? Description { get; init; }
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
