using System.Text.Json;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.GenericJson;

internal sealed class JsonValueToken(int index, string label, JsonTokenType kind, int offset, int length)
{
    private int _index = index;
    private string? _valueId;
    public int Index
    {
        get => _index;
        set { _index = value; _valueId = null; }
    }
    public string? StructureReview { get; set; }
    public string? EscapedPathLabel { get; set; }
    public string? FriendlyLabel { get; set; }
    public string Label { get; set; } = label;
    public string OriginalLabel { get; set; } = label;
    public int MemberNameLength { get; set; }
    public JsonTokenType Kind { get; } = kind;
    public int Offset { get; } = offset;
    public int Length { get; set; } = length;
    public int MemberOffset { get; set; } = offset;
    public byte[] Source { get; set; } = [];
    public ReadOnlyMemory<byte> Leading { get; set; }
    public ReadOnlyMemory<byte> Trailing { get; set; }
    public ReadOnlyMemory<byte> SeparatorBefore { get; set; }
    public ReadOnlyMemory<byte> DefaultSeparator { get; set; }
    public JsonValueToken? Parent { get; set; }
    public int Position { get; set; }
    public int Occurrence { get; set; } = 1;
    public bool Imported { get; set; } = true;
    public List<JsonValueToken> Children { get; } = [];
    // Read-only imported identities can be shared by clones without changing snapshot state.
    public IReadOnlyList<int> OriginalChildIds { get; set; } = [];
    public bool HasOriginalChildren => OriginalChildIds.SequenceEqual(Children.Select(child => child.Index));
    public FieldSpec? Spec { get; set; }
    public FieldValue Original { get; set; }
    public FieldValue Current { get; set; }
    public string? Warning { get; set; }
    public bool IsContainer => Kind is JsonTokenType.StartObject or JsonTokenType.StartArray;
    public string ValueId => _valueId ??= $"value/{Index}";

    public string? Validate(FieldValue value) => Spec?.ReadOnly == true ? "Field is read-only." : JsonScalar.Validate(value);

    public byte[] Encode() => JsonScalar.Encode(Current);

    public JsonValueToken Clone(JsonValueToken? parent = null)
    {
        var clone = new JsonValueToken(Index, Label, Kind, Offset, Length)
        {
            MemberOffset = MemberOffset, Source = Source, Leading = Leading, Trailing = Trailing,
            SeparatorBefore = SeparatorBefore, DefaultSeparator = DefaultSeparator, Parent = parent,
            Imported = Imported, Spec = Spec, Original = Original, Current = Current, Warning = Warning,
            Position = Position, Occurrence = Occurrence,
            OriginalLabel = OriginalLabel, MemberNameLength = MemberNameLength,
            OriginalChildIds = OriginalChildIds,
        };
        clone.Children.AddRange(Children.Select(child => child.Clone(clone)));
        return clone;
    }
}
