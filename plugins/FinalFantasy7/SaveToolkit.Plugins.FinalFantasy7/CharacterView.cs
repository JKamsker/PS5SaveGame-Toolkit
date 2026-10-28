using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Read-only character candidates backed by the measured GTC-compatible arrays.</summary>
internal sealed class CharacterView(Ff7Schema schema, ReferenceData references, Func<ScalarField, FieldNode> bind)
{
    private const int BaseOffset = 0x30;
    private const int RemakeStride = 0x40;
    private const int RebirthStride = 0x100;

    public TableNode Build()
    {
        var rows = new List<TableRow>(Ff7Schema.CharacterCount);
        for (var index = 0; index < Ff7Schema.CharacterCount; index++)
        {
            var baseOffset = BaseOffset + index * (schema.IsRemake ? RemakeStride : RebirthStride);
            var key = $"character/{index}";
            var cells = new Dictionary<string, ViewNode>(StringComparer.Ordinal)
            {
                ["name"] = new TextNode(references.Character(index)),
                ["source"] = new TextNode(references.CharacterSource(index)),
                ["level"] = bind(Field(key + "/level", "Level", baseOffset, IntegerSpec.UInt8, singleByte: true)),
                ["experience"] = bind(Field(key + "/experience", "Experience", baseOffset + 0x20, IntegerSpec.UInt32)),
                ["hp"] = bind(Field(key + "/hp", "Current HP", baseOffset + 0x10, IntegerSpec.UInt32)),
                ["maxHp"] = bind(Field(key + "/max-hp", "Max HP", baseOffset + 0x14, IntegerSpec.UInt32)),
            };
            if (!schema.IsRemake)
            {
                cells["weapon"] = bind(Field(key + "/weapon", "Weapon instance", baseOffset + 0x9C,
                    IntegerSpec.UInt32 with { Format = IntegerFormat.Hex }));
            }

            rows.Add(new TableRow(key, cells));
        }

        var columns = new List<TableColumn>
        {
            new("name", "Character"), new("level", "Level"), new("experience", "XP"),
            new("hp", "HP"), new("maxHp", "Max HP"),
            new("source", "Name source"),
        };
        if (!schema.IsRemake)
        {
            columns.Add(new("weapon", "Weapon instance"));
        }

        return new TableNode(columns, rows)
        {
            Title = "Character sheets",
            Description = "Candidate party records from the public MIT schema and matching PS5 sample values. Values are read-only and Inferred until controlled screenshots and before/after captures identify guest and cached records. " + EmbeddedCatalogs.Attribution,
        };
    }

    private static ScalarField Field(string id, string label, int offset, IntegerSpec spec, bool singleByte = false) =>
        new(id, label, offset, spec, Confidence.Inferred, Byte: singleByte,
            Warning: "Candidate character field; no controlled PS5 mutation proves its gameplay meaning.");
}
