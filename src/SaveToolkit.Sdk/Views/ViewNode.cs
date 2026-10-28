using System.Text.Json.Serialization;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Sdk.Views;

/// <summary>
/// Abstract, style-free description of what to show. Plugins compose these nodes; the browser decides
/// how each node type looks. Keep trees reasonably small: wrap large or deep content in a lazy
/// <see cref="SectionNode"/> so it is only built when the user opens it.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SectionNode), "section")]
[JsonDerivedType(typeof(TabsNode), "tabs")]
[JsonDerivedType(typeof(FieldNode), "field")]
[JsonDerivedType(typeof(TableNode), "table")]
[JsonDerivedType(typeof(TextNode), "text")]
[JsonDerivedType(typeof(StatsNode), "stats")]
[JsonDerivedType(typeof(ImageNode), "image")]
[JsonDerivedType(typeof(ActionNode), "action")]
[JsonDerivedType(typeof(HexNode), "hex")]
public abstract record ViewNode;

/// <summary>
/// Titled group of nodes. When <see cref="LazyId"/> is set, <see cref="Children"/> is ignored and the
/// browser fetches the children via <see cref="ISaveDocument.ExpandNode"/> once the section is opened.
/// </summary>
public sealed record SectionNode(string Title, IReadOnlyList<ViewNode> Children) : ViewNode
{
    public string? Description { get; init; }

    /// <summary>Short secondary label, e.g. an item count.</summary>
    public string? Badge { get; init; }

    public bool Collapsed { get; init; }

    public string? LazyId { get; init; }

    public static SectionNode Lazy(string title, string lazyId, string? badge = null) =>
        new(title, []) { LazyId = lazyId, Collapsed = true, Badge = badge };
}

public sealed record Tab(string Title, IReadOnlyList<ViewNode> Children);

public sealed record TabsNode(IReadOnlyList<Tab> Tabs) : ViewNode;

public enum Confidence
{
    /// <summary>Meaning confirmed (e.g. name hash matched, in-game test done).</summary>
    Verified,

    /// <summary>Meaning inferred from observation; may be wrong.</summary>
    Inferred,

    /// <summary>Raw value with no known meaning.</summary>
    Unknown,
}

/// <summary>
/// A single value bound to <see cref="ValueId"/>. Value ids must be stable for the document's lifetime
/// and unique within it; the framework routes edits by id and validates them against <see cref="Spec"/>.
/// </summary>
public sealed record FieldNode(string ValueId, string Label, FieldSpec Spec, FieldValue Value) : ViewNode
{
    public string? Description { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<Confidence>))]
    public Confidence? Confidence { get; init; }

    /// <summary>Shown next to the control, e.g. "Values above 999 may crash the game".</summary>
    public string? Warning { get; init; }
}

public enum ColumnWidth
{
    Narrow,
    Normal,
    Wide,
}

public sealed record TableColumn(string Key, string Label)
{
    public string? Description { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<ColumnWidth>))]
    public ColumnWidth Width { get; init; } = ColumnWidth.Normal;
}

/// <summary>Row of a table. Cells are keyed by column key and are usually <see cref="FieldNode"/> or <see cref="TextNode"/>.</summary>
public sealed record TableRow(string Key, IReadOnlyDictionary<string, ViewNode> Cells);

/// <summary>Homogeneous records (inventory, party members, ...). The browser handles paging, search and virtualisation.</summary>
public sealed record TableNode(IReadOnlyList<TableColumn> Columns, IReadOnlyList<TableRow> Rows) : ViewNode
{
    public string? Title { get; init; }

    public string? Description { get; init; }
}

public enum Tone
{
    Normal,
    Muted,
    Info,
    Success,
    Warning,
    Danger,
}

public sealed record TextNode(string Text) : ViewNode
{
    [JsonConverter(typeof(JsonStringEnumConverter<Tone>))]
    public Tone Tone { get; init; } = Tone.Normal;

    /// <summary>Render in a fixed-width font (hashes, offsets, ids).</summary>
    public bool Monospace { get; init; }
}

public sealed record Stat(string Label, string Value)
{
    public string? Hint { get; init; }
}

/// <summary>Read-only headline figures, e.g. play time, level, slot.</summary>
public sealed record StatsNode(IReadOnlyList<Stat> Items) : ViewNode;

/// <summary>Image served by <see cref="ISaveDocument.GetAsset"/>.</summary>
public sealed record ImageNode(string AssetId, string Alt) : ViewNode;

/// <summary>
/// Plugin-defined command (e.g. "Max all items"). Parameters are rendered as a small form; their
/// <see cref="FieldNode.ValueId"/> is the parameter name passed to <see cref="ISaveDocument.InvokeAction"/>.
/// </summary>
public sealed record ActionNode(string ActionId, string Label) : ViewNode
{
    /// <summary>Requires IActionPlanningDocument; direct invocation is rejected. False preserves legacy behavior.</summary>
    public bool SupportsPlanning { get; init; }

    public string? Description { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<Tone>))]
    public Tone Tone { get; init; } = Tone.Normal;

    /// <summary>When set, the browser asks for confirmation with this text first.</summary>
    public string? Confirm { get; init; }

    public IReadOnlyList<FieldNode> Parameters { get; init; } = [];
}

/// <summary>Read-only byte view served in pages by <see cref="ISaveDocument.ReadBlob"/>.</summary>
public sealed record HexNode(string BlobId, long Length) : ViewNode
{
    public string? Title { get; init; }
}
