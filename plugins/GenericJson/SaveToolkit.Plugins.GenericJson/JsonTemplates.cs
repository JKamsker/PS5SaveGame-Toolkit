using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GenericJson;

/// <summary>Explicit JSON type templates; never infer a game schema from a neighboring entry.</summary>
internal static class JsonTemplates
{
    internal const int MaxInsertedText = 1024 * 1024;
    private const string Custom = "custom";
    private static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["null"] = "null", ["string"] = "\"\"", ["number"] = "0", ["boolean"] = "false",
        ["object"] = "{}", ["array"] = "[]",
    };

    public static IReadOnlyList<FieldNode> Parameters => [
        new("template", "Explicit JSON type template", new EnumSpec([
            new(Custom, "Custom JSON (use text below)"),
            new("schema", "Generate from explicit local schema"),
            .. Defaults.Keys.Select(type => new EnumOption(type, $"Empty {type}")),
        ]), FieldValue.Of(Custom)),
        new("json", "Custom JSON value", new TextSpec { Multiline = true, MaxLength = MaxInsertedText }, FieldValue.Of("null")),
        new("schema", "Optional local template schema (documented subset)", new TextSpec { Multiline = true, MaxLength = JsonTemplateSchema.MaxSchemaLength }, FieldValue.Of(""))
        {
            Description = "Explicit insertion schema only; never inferred from game keys or retained as a document constraint. "
                + "Supports type, default, object properties/required/boolean additionalProperties, array items/minItems/maxItems. "
                + "Other keywords and external references reject. Source: https://json-schema.org/understanding-json-schema/ (original subset implementation).",
        },
    ];

    public static string Read(IReadOnlyDictionary<string, FieldValue> parameters)
    {
        // Omitted selector preserves the round-1 custom-template action contract.
        var type = parameters.TryGetValue("template", out var selection) ? Text(selection) : Custom;
        var schemaText = parameters.TryGetValue("schema", out var supplied) ? Text(supplied) : "";
        var schema = schemaText.Length == 0 ? null : JsonTemplateSchema.Parse(schemaText);
        string json;
        if (type == "schema") json = schema?.Generate() ?? throw new ArgumentException("Supply an explicit local template schema.");
        else if (type == Custom) json = ReadCustom(parameters);
        else if (!Defaults.TryGetValue(type, out json!)) throw new ArgumentException("Unknown JSON type template.");
        if (json.Length > MaxInsertedText) throw new ArgumentException("Generated JSON template exceeds 1 MiB of text.");
        var parsed = JsonTokenTree.Parse(json);
        schema?.Validate(parsed.Nodes[0]);
        return json;
    }

    private static string ReadCustom(IReadOnlyDictionary<string, FieldValue> parameters)
    {
        if (!parameters.TryGetValue("json", out var value) || value.Kind != FieldValueKind.Text || value.AsString().Length > MaxInsertedText)
            throw new ArgumentException("Supply a JSON template of at most 1 MiB of text.");
        return value.AsString();
    }

    private static string Text(FieldValue value) => value.Kind == FieldValueKind.Text ? value.AsString()
        : throw new ArgumentException("Select a JSON type template.");
}
