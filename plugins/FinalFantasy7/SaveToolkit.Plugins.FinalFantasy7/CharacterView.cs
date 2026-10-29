using System.Buffers.Binary;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Candidate live records; all writes retain source and console warnings.</summary>
internal sealed class CharacterView
{
    private const uint MaximumExperience = 9_999_999;
    private readonly ReferenceData _references;
    private readonly byte[] _bytes;
    private readonly Func<ScalarField, FieldNode> _bind;
    private readonly List<Dictionary<string, ScalarField>> _fields = [];

    public CharacterView(byte[] bytes, Ff7Schema schema, ReferenceData references,
        Func<ScalarField, FieldNode> bind, ProgressionEdits progression)
    {
        _references = references;
        _bytes = bytes;
        _bind = bind;
        if (bytes.Length == Ff7Schema.CommonFileLength) return;
        var curves = ProgressionEdits.CharacterCurves(schema);
        var weapons = new WeaponEquipment(bytes, schema, references);
        for (var index = 0; index < Ff7Schema.CharacterCount; index++)
        {
            var offset = CharacterLayout.Offset(schema, index);
            var key = $"character/{index}";
            var level = bytes[offset];
            var xp = U32(bytes, offset + CharacterLayout.ExperienceDelta);
            var editable = level >= 1 && level <= curves[index].Length;
            if (editable) progression.Add(key + "/experience", key + "/level", curves[index], xp, level);
            var fields = new Dictionary<string, ScalarField>(StringComparer.Ordinal)
            {
                ["level"] = new(key + "/level", "Level", offset,
                    IntegerSpec.UInt8 with { Min = Math.Min(level, curves[index].Count(value => value == 0)), Max = Math.Max(level, curves[index].Length) },
                    Confidence.Inferred, Byte: true, Editable: editable, Warning: WriteWarnings.Progression),
                ["experience"] = new(key + "/experience", "Experience", offset + CharacterLayout.ExperienceDelta,
                    IntegerSpec.UInt32 with { Max = Math.Max(MaximumExperience, xp) }, Confidence.Inferred,
                    Editable: editable, Warning: WriteWarnings.Progression),
            };
            AddVital(fields, "hp", "HP", key, bytes, offset + CharacterLayout.HpDelta, editable);
            AddVital(fields, "mp", "MP", key, bytes, offset + CharacterLayout.MpDelta, editable);
            if (!schema.IsRemake) fields["weapon"] = weapons.Field(index);
            _fields.Add(fields);
        }
    }

    public ScalarField Definition(int character, string suffix) => _fields[character][suffix];

    public uint Value(int character, string suffix)
    {
        var field = Definition(character, suffix);
        return field.Byte ? _bytes[field.Offset] : U32(_bytes, field.Offset);
    }

    public TableNode Build(IReadOnlyList<ActionNode>? actions = null)
    {
        var rows = _fields.Select((fields, index) =>
        {
            var cells = fields.ToDictionary(pair => pair.Key, pair => (ViewNode)_bind(pair.Value), StringComparer.Ordinal);
            cells["name"] = new TextNode(_references.Character(index));
            cells["source"] = new TextNode(_references.CharacterSource(index));
            foreach (var action in actions ?? []) cells[action.ActionId] = action;
            return new TableRow($"character/{index}", cells);
        }).ToArray();
        List<TableColumn> columns = [new("name", "Character"), new("level", "Level"), new("experience", "XP"),
            new("hp", "HP"), new("max-hp", "Max HP"), new("mp", "MP"), new("max-mp", "Max MP"), new("source", "Source")];
        if (_fields.Any(fields => fields.ContainsKey("weapon"))) columns.Add(new("weapon", "Weapon instance"));
        if (actions is not null) columns.AddRange(actions.Select(action => new TableColumn(action.ActionId, action.Label)));
        return new(columns, rows)
        {
            SelectionId = CharacterPlanning.SelectionTableId,
            Title = "Character sheets",
            Description = WriteWarnings.Progression + "HP/MP are bounded by the original stored maxima. Maxima remain read-only caches. " +
                "Select rows, then use a table action to preview only that selection; an empty selection changes nothing. " +
                "The separate controls above the table target all seven records. " + EmbeddedCatalogs.Attribution,
        };
    }

    private static void AddVital(Dictionary<string, ScalarField> fields, string suffix, string label,
        string key, byte[] bytes, int offset, bool editable)
    {
        fields[suffix] = new(key + "/" + suffix, "Current " + label, offset,
            IntegerSpec.UInt32 with { Max = Math.Max(U32(bytes, offset), U32(bytes, offset + CharacterLayout.MaximumVitalDelta)) }, Confidence.Inferred,
            Editable: editable, Warning: WriteWarnings.Experimental + "Based on public schema and sample values; original maximum is the conservative editor bound.");
        fields["max-" + suffix] = new(key + "/max-" + suffix, "Max " + label, offset + CharacterLayout.MaximumVitalDelta,
            IntegerSpec.UInt32, Confidence.Inferred, Warning: "Derived/cached maximum; preserved as stored.");
    }

    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
}
