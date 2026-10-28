using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.Fakes;

/// <summary>Synthetic format exercising the field protocol without changing production JSON plugins.</summary>
public sealed class ExactJsonPlugin : IGamePlugin
{
    public PluginInfo Info { get; } = new("test.exact-json", "Exact JSON fixture", "1.0.0");
    public ProbeResult Probe(ISaveFile file) => ProbeResult.Match(ProbeScore.Format, "Synthetic JSON");
    public ISaveDocument Open(ISaveFile file) => new ExactDocument(file);

    private sealed class ExactDocument : BoundSaveDocument
    {
        private static readonly UnionSpec ChoiceSpec = new([
            new("number", "Number", new ExactNumberSpec()), new("text", "Text", new TextSpec()),
            new("integer", "Integer", IntegerSpec.UInt64),
        ]);
        private readonly ISaveFile _file;
        private readonly byte[] _original;
        private readonly JsonElement _root;
        private readonly Dictionary<string, FieldValue> _values;
        private readonly Dictionary<string, FieldValue> _initial;

        public ExactDocument(ISaveFile file)
        {
            _file = file;
            _original = file.ReadAllBytes();
            using var json = JsonDocument.Parse(_original);
            _root = json.RootElement.Clone();
            _values = _root.EnumerateObject().ToDictionary(p => p.Name, p => ReadValue(p.Value));
            _initial = new(_values);
        }

        public override string Title => "Exact JSON fixture";

        private static FieldValue ReadValue(JsonElement json)
        {
            var value = json.ValueKind == JsonValueKind.Number ? FieldValue.ExactNumber(json.GetRawText()) : json.Deserialize<FieldValue>();
            // Like existing integer fields, a tagged union's integer string needs its known inner spec.
            if (value.Kind == FieldValueKind.Union && value.AsUnion() is { Type: "integer", Value.Kind: FieldValueKind.Text } choice)
                return FieldValue.Union(choice.Type, FieldValue.Of(Int128.Parse(choice.Value.AsString(), System.Globalization.CultureInfo.InvariantCulture)));
            return value;
        }

        public override ViewNode BuildView() => new SectionNode(Title,
            _values.Keys.Select(id => (ViewNode)Field(id, id, Spec(id), () => _values[id], value => _values[id] = value)).ToArray());

        private static FieldSpec Spec(string id) => id switch
        {
            "number" => new ExactNumberSpec(),
            "optional" => new NullableSpec(new TextSpec { Multiline = true }),
            "choice" => ChoiceSpec,
            _ => new TextSpec { ReadOnly = true },
        };

        public override IReadOnlyList<FileOutput> Serialize()
        {
            if (_values.All(p => _initial[p.Key] == p.Value)) return [new(_file.RelativePath, _original)];
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject();
                foreach (var property in _root.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    var value = _values[property.Name];
                    if (value.Kind == FieldValueKind.ExactNumber) writer.WriteRawValue(value.AsExactNumber());
                    else JsonSerializer.Serialize(writer, value);
                }
                writer.WriteEndObject();
            }
            return [new(_file.RelativePath, output.ToArray())];
        }
    }
}
