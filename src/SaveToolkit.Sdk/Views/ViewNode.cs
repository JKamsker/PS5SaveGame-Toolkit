using System.Text.Json.Serialization;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Collections;

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
[JsonDerivedType(typeof(QueryableTableNode), "queryableTable")]
[JsonDerivedType(typeof(GraphNode), "graph")]
[JsonDerivedType(typeof(MapNode), "map")]
public abstract record ViewNode;

/// <summary>
/// Titled group of nodes. When <see cref="LazyId"/> is set, <see cref="Children"/> is ignored and the
/// browser fetches the children via <see cref="ISaveDocument.ExpandNode"/> once the section is opened.
/// </summary>
public sealed record SectionNode(string Title, IReadOnlyList<ViewNode> Children) : ViewNode
{
    /// <summary>Optional section explanation or attribution.</summary>
    public string? Description { get; init; }

    /// <summary>Short secondary label, e.g. an item count.</summary>
    public string? Badge { get; init; }

    /// <summary>Initial collapsed state; users can expand the section.</summary>
    public bool Collapsed { get; init; }

    /// <summary>Stable expansion ID passed to the document instead of eagerly reading Children.</summary>
    public string? LazyId { get; init; }

    /// <summary>Creates an initially collapsed lazy section with no eager children.</summary>
    public static SectionNode Lazy(string title, string lazyId, string? badge = null) =>
        new(title, []) { LazyId = lazyId, Collapsed = true, Badge = badge };
}

/// <summary>One named page of a tab group, containing ordinary view nodes.</summary>
public sealed record Tab(string Title, IReadOnlyList<ViewNode> Children);

/// <summary>A group of named pages; all child bindings remain part of the same document.</summary>
public sealed record TabsNode(IReadOnlyList<Tab> Tabs) : ViewNode;

/// <summary>Evidence supporting a field's interpreted meaning, independent of plugin write maturity.</summary>
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
    /// <summary>Optional field explanation or attribution.</summary>
    public string? Description { get; init; }

    /// <summary>Evidence supporting this field's interpreted meaning, or null when no label is supplied.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Confidence>))]
    public Confidence? Confidence { get; init; }

    /// <summary>Shown next to the control, e.g. "Values above 999 may crash the game".</summary>
    public string? Warning { get; init; }
}

/// <summary>Relative presentation hint for a table column; the host chooses concrete dimensions.</summary>
public enum ColumnWidth
{
    /// <summary>Compact content such as counts or short identifiers.</summary>
    Narrow,
    /// <summary>Default relative column width.</summary>
    Normal,
    /// <summary>Additional room for names or explanatory content.</summary>
    Wide,
}

/// <summary>An eager table's stable column key and human label; row cells use the same key.</summary>
public sealed record TableColumn(string Key, string Label)
{
    /// <summary>Optional column explanation.</summary>
    public string? Description { get; init; }

    /// <summary>Relative width hint for the host renderer.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ColumnWidth>))]
    public ColumnWidth Width { get; init; } = ColumnWidth.Normal;
}

/// <summary>Row of a table. Cells are keyed by column key and are usually <see cref="FieldNode"/> or <see cref="TextNode"/>.</summary>
public sealed record TableRow(string Key, IReadOnlyDictionary<string, ViewNode> Cells)
{
    /// <summary>Confirmed empty slot, not an unknown item or merely a zero quantity. Its key and cells remain intact.</summary>
    public bool IsEmpty { get; init; }
}

/// <summary>Homogeneous records (inventory, party members, ...). The browser handles paging, search and virtualisation.</summary>
public sealed record TableNode(IReadOnlyList<TableColumn> Columns, IReadOnlyList<TableRow> Rows) : ViewNode
{
    /// <summary>Optional stable selection identity, unique among eager SelectionIds and F8 CollectionIds in this view.</summary>
    public string? SelectionId { get; init; }

    /// <summary>Optional display policy for confirmed empty slots; all saved rows and bindings remain intact.</summary>
    public EmptyRowPresentation? EmptyRows { get; init; }

    /// <summary>Optional table heading.</summary>
    public string? Title { get; init; }

    /// <summary>Optional table explanation or attribution.</summary>
    public string? Description { get; init; }
}

/// <summary>Semantic emphasis for text and actions; the host supplies the visual styling.</summary>
public enum Tone
{
    /// <summary>Ordinary content without additional emphasis.</summary>
    Normal,
    /// <summary>Secondary explanatory content.</summary>
    Muted,
    /// <summary>Informational content.</summary>
    Info,
    /// <summary>Successful or positive status.</summary>
    Success,
    /// <summary>A caution or unconfirmed limitation.</summary>
    Warning,
    /// <summary>A serious error or potentially destructive action.</summary>
    Danger,
}

/// <summary>Read-only text, optionally styled by semantic tone or rendered in a fixed-width font.</summary>
public sealed record TextNode(string Text) : ViewNode
{
    /// <summary>Semantic emphasis chosen by the plugin; the host chooses its styling.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Tone>))]
    public Tone Tone { get; init; } = Tone.Normal;

    /// <summary>Render in a fixed-width font (hashes, offsets, ids).</summary>
    public bool Monospace { get; init; }
}

/// <summary>A formatted, read-only headline value and its label.</summary>
public sealed record Stat(string Label, string Value)
{
    /// <summary>Optional explanation of the displayed statistic.</summary>
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
    /// <summary>
    /// Optional table selection binding for planned actions: an eager TableNode.SelectionId or F8 CollectionId
    /// in the same view. Null preserves placement-based scope. A bound sibling with no selection uses document scope.
    /// Member adapters map this identity with the table identity. This does not declare value ownership.
    /// </summary>
    public string? SelectionTableId { get; init; }

    /// <summary>False opts into isolated, verified inspection. Results are messages, never save or view changes.</summary>
    public bool Mutates { get; init; } = true;

    /// <summary>Requires IActionPlanningDocument; direct invocation is rejected. False preserves legacy behavior.</summary>
    public bool SupportsPlanning { get; init; }

    /// <summary>Optional action explanation or attribution.</summary>
    public string? Description { get; init; }

    /// <summary>Semantic emphasis for the action control.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Tone>))]
    public Tone Tone { get; init; } = Tone.Normal;

    /// <summary>When set, the browser asks for confirmation with this text first.</summary>
    public string? Confirm { get; init; }

    /// <summary>Typed input fields whose values supply defaults and whose IDs become parameter names.</summary>
    public IReadOnlyList<FieldNode> Parameters { get; init; } = [];
}

/// <summary>Read-only byte view served in pages by <see cref="ISaveDocument.ReadBlob"/>.</summary>
public sealed record HexNode(string BlobId, long Length) : ViewNode
{
    /// <summary>Optional heading for the byte view.</summary>
    public string? Title { get; init; }
}
